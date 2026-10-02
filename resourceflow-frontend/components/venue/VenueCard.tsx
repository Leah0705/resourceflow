import { ThemedText } from "@/components/themed-text";
import { VenueDto } from "@/api/venues";
import { useRouter, type Href } from "expo-router";
import { ActivityIndicator, Linking, Platform, Pressable, StyleSheet, View } from "react-native";
import { Image } from "expo-image";
import { haptics } from "@/utils/haptics";
import { useAppTheme } from "@/hooks/use-app-theme";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import { fetchAvailability, TimeSlotDto } from "@/api/availability";
import { getHoursForDay, hasCustomHours } from "@/utils/openingHours";
import { isWalkInOnlyOnDay, walkInBadgeLabel } from "@/utils/walkIn";
import { hexToRgb } from "@/utils/colors";
import { getVenueDate, getVenueNow, getOpenDaysList } from "@/utils/venueTime";
import { cardStyles, openBadgeColor } from "@/components/venue/cardStyles";
import { styles } from "./VenueCard.styles";
import { Icon } from "@/components/common/Icon";
import { VenueTags } from "@/components/venue/VenueTags";
import { resolveServerUrl } from "@/utils/serverUrl";
import { APPLE_MAPS_SEARCH, GOOGLE_MAPS_SEARCH, openDirections } from "@/utils/directions";

function opensLaterToday(venue: VenueDto, t: TFunction): string | null {
  const timezone = venue.timezone ?? "UTC";
  const { totalMins, isoDay } = getVenueNow(timezone || "UTC");
  const { open: openTime } = getHoursForDay(venue, isoDay);
  const [oh, om] = openTime.split(":").map(Number);
  const openDaysList = getOpenDaysList(venue);
  if (openDaysList.length > 0 && !openDaysList.includes(isoDay)) return null;
  const openMins = oh * 60 + (om || 0);
  if (totalMins >= openMins) return null;
  const diffMins = openMins - totalMins;
  const diffHours = Math.floor(diffMins / 60);
  const diffRemMins = diffMins % 60;
  if (diffHours >= 1 && diffRemMins === 0)
    return t("venue.card.opensInHours", { hours: diffHours });
  if (diffHours >= 1)
    return t("venue.card.opensInHoursMinutes", { hours: diffHours, minutes: diffRemMins });
  return t("venue.card.opensInMinutes", { minutes: diffMins });
}

function isOpenNow(venue: VenueDto): boolean {
  const timezone = venue.timezone ?? "UTC";
  const { totalMins, isoDay } = getVenueNow(timezone || "UTC");
  const { open: openTime, close: closeTime } = getHoursForDay(venue, isoDay);
  const [oh, om] = openTime.split(":").map(Number);
  const [ch, cm] = closeTime.split(":").map(Number);
  if (isNaN(oh) || isNaN(ch)) return true;
  const openDaysList = getOpenDaysList(venue);
  if (openDaysList.length > 0 && !openDaysList.includes(isoDay)) return false;
  return totalMins >= oh * 60 + om && totalMins < ch * 60 + cm;
}

export default function VenueCard({ venue, party = 2 }: { venue: VenueDto; party?: number }) {
  const { colors, isDark, primaryColor } = useAppTheme();
  const mutedColor = colors.muted;
  const router = useRouter();
  const { t } = useTranslation();

  // Every route this card can take answers a tap, so each one confirms the tap first.
  // The haptic is a no-op on web and on devices without an actuator.
  const openLocation = () => {
    haptics.selection();
    router.push(`/(user)/locations/${venue.id}` as Href);
  };

  const [slots, setSlots] = useState<TimeSlotDto[]>([]);
  const [slotsLoading, setSlotsLoading] = useState(true);

  useEffect(() => {
    const tz = venue.timezone ?? "UTC";
    const { totalMins, isoDay } = getVenueNow(tz);
    const openDaysList = getOpenDaysList(venue);
    if (
      (openDaysList.length > 0 && !openDaysList.includes(isoDay)) ||
      isWalkInOnlyOnDay(venue, isoDay)
    ) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSlots([]);

      setSlotsLoading(false);
      return;
    }
    const date = getVenueDate(tz);
    fetchAvailability(venue.id, date, party).then((data) => {
      if (data && Array.isArray(data.slots)) {
        const future = data.slots.filter((s) => {
          if (!s.isAvailable) return false;
          const [h, m] = s.time.split(":").map(Number);
          return h * 60 + (m || 0) > totalMins;
        });
        setSlots(future.slice(0, 5));
      } else {
        setSlots([]);
      }
      setSlotsLoading(false);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [venue.id, venue.timezone, venue.openDays, venue.walkInOnly, venue.walkInDays, party]);

  const open = isOpenNow(venue);
  const opensLabel = !open ? opensLaterToday(venue, t) : null;
  const walkInLocation = !!venue.walkInOnly;
  const walkInToday =
    !walkInLocation && isWalkInOnlyOnDay(venue, getVenueNow(venue.timezone ?? "UTC").isoDay);
  const walkInBadgeText = walkInBadgeLabel(venue);
  const todayIsoDay = getVenueNow(venue.timezone ?? "UTC").isoDay;
  const todayHours = getHoursForDay(venue, todayIsoDay);
  const hoursVary = hasCustomHours(venue);
  const closedToday = !getOpenDaysList(venue).includes(todayIsoDay);
  const tags = venue.tags ?? [];
  const address = venue.address || "";

  const { r: accentR, g: accentG, b: accentB } = hexToRgb(primaryColor);

  const cardBg = colors.card;
  const borderColor = colors.border;
  const surface2 = colors.surfaceAlt;

  const cardShadow =
    Platform.OS === "web"
      ? isDark
        ? ({ boxShadow: "0 8px 24px -12px rgba(0,0,0,0.6)" } as object)
        : ({ boxShadow: "0 8px 24px -16px rgba(60,40,10,0.18)" } as object)
      : ({
          shadowColor: "#000",
          shadowOffset: { width: 0, height: 4 },
          shadowOpacity: 0.15,
          shadowRadius: 12,
          elevation: 4,
        } as object);

  return (
    <Pressable
      accessibilityRole="link"
      accessibilityLabel={t("venue.card.viewDetailsAndBook", { name: venue.name })}
      onPress={() => openLocation()}
      style={({ hovered, pressed }: { hovered?: boolean; pressed: boolean }) => [
        styles.card,
        cardShadow,
        { backgroundColor: cardBg, borderColor },
        hovered && Platform.OS === "web" && { borderColor: colors.borderStrong },
        // Touch has no hover to fall back on, so the press itself has to answer: the
        // whole card takes the accent edge and settles a hair into the page.
        pressed && { borderColor: primaryColor, transform: [{ scale: 0.99 }] },
      ]}
    >
      <View style={styles.cardClip}>
        <View
          style={[
            styles.imageArea,
            venue.imageUrl
              ? Platform.OS === "web"
                ? ({
                    backgroundImage: `url(${venue.imageUrl})`,
                    backgroundSize: "cover",
                    backgroundPosition: "center",
                  } as object)
                : { backgroundColor: "#111" }
              : Platform.OS === "web"
                ? ({
                    background: `linear-gradient(145deg,
                    rgb(${Math.floor(accentR * 0.1)},${Math.floor(accentG * 0.1)},${Math.floor(accentB * 0.13)}) 0%,
                    rgb(${Math.floor(accentR * 0.38)},${Math.floor(accentG * 0.38)},${Math.floor(accentB * 0.42)}) 55%,
                    rgb(${Math.floor(accentR * 0.6)},${Math.floor(accentG * 0.6)},${Math.floor(accentB * 0.65)}) 100%)`,
                  } as object)
                : {
                    backgroundColor: `rgb(${Math.floor(accentR * 0.15)},${Math.floor(accentG * 0.15)},${Math.floor(accentB * 0.18)})`,
                  },
          ]}
        >
          {/* Native background image via expo-image (web uses CSS backgroundImage above) */}
          {
            // istanbul ignore next
            venue.imageUrl && Platform.OS !== "web" && (
              <Image
                source={{ uri: resolveServerUrl(venue.imageUrl) }}
                style={StyleSheet.absoluteFill}
                contentFit="cover"
                accessible={false}
                accessibilityElementsHidden
                importantForAccessibility="no-hide-descendants"
              />
            )
          }
          {!venue.imageUrl && (
            <>
              <View style={styles.phRingTopRight} />
              <View style={styles.phRingBottomLeft} />
              <View style={styles.phCenter}>
                <Icon name="business-outline" size={28} color="rgba(255,255,255,0.2)" />
                <ThemedText style={styles.phInitial}>
                  {venue.name.charAt(0).toUpperCase()}
                </ThemedText>
              </View>
            </>
          )}

          <View style={styles.imageTopRow}>
            <View
              style={[
                cardStyles.badge,
                open
                  ? { backgroundColor: openBadgeColor(accentR, accentG, accentB) }
                  : opensLabel
                    ? { backgroundColor: `rgba(${accentR},${accentG},${accentB},0.72)` }
                    : cardStyles.badgeMuted,
              ]}
            >
              {open && <View style={cardStyles.badgeDot} />}
              <ThemedText style={cardStyles.badgeText}>
                {open
                  ? t("venue.card.openTill", { time: todayHours.close })
                  : (opensLabel ?? t("venue.card.closedBadge"))}
              </ThemedText>
            </View>
            {walkInBadgeText && (
              <View style={[cardStyles.badge, cardStyles.badgeMuted]} testID="walk-in-badge">
                <Icon name="walk-outline" size="xs" color="#fff" />
                <ThemedText style={cardStyles.badgeText}>{walkInBadgeText}</ThemedText>
              </View>
            )}
          </View>
        </View>

        <View style={styles.body}>
          <View style={styles.nameRow}>
            <View style={{ flex: 1, minWidth: 0 }}>
              <ThemedText style={styles.name} numberOfLines={1}>
                {venue.name}
              </ThemedText>
              <View style={styles.meta}>
                <Icon name="location-outline" size={11} color={mutedColor} />
                <ThemedText style={[styles.metaText, { color: mutedColor }]} numberOfLines={1}>
                  {venue.address || t("venue.card.multipleAreas")}
                </ThemedText>
              </View>
            </View>
            {/* A new tab is a browser idea. Off web the whole card already opens the
                location, so a second control doing the same thing is only clutter. */}
            {Platform.OS === "web" && (
              <Pressable
                style={[styles.iconBtn, { backgroundColor: surface2, borderColor }]}
                onPress={(e) => {
                  e.stopPropagation?.();
                  window.open(`/(user)/locations/${venue.id}`, "_blank");
                }}
                accessibilityRole="link"
                accessibilityLabel={t("venue.card.openBookingPageNewTab")}
              >
                <Icon name="open-outline" size="sm" color={mutedColor} />
              </Pressable>
            )}
          </View>

          <VenueTags tags={tags} />

          <View style={styles.mapLinks}>
            {Platform.OS === "web" ? (
              <>
                <ThemedText style={[styles.mapLinksLabel, { color: mutedColor }]}>
                  {t("venue.card.directions")}
                </ThemedText>
                <Pressable
                  style={({ hovered, pressed }: { hovered?: boolean; pressed: boolean }) => [
                    styles.mapLink,
                    {
                      backgroundColor: surface2,
                      borderColor: hovered || pressed ? primaryColor : borderColor,
                    },
                  ]}
                  onPress={(e) => {
                    e.stopPropagation?.();
                    Linking.openURL(`${GOOGLE_MAPS_SEARCH}${encodeURIComponent(address)}`);
                  }}
                  accessibilityRole="link"
                  accessibilityLabel={t("venue.card.openInGoogleMaps")}
                >
                  <Icon name="navigate-outline" size={11} color={mutedColor} />
                  <ThemedText style={[styles.mapLinkText, { color: colors.text }]}>
                    Google
                  </ThemedText>
                </Pressable>
                <Pressable
                  style={({ hovered, pressed }: { hovered?: boolean; pressed: boolean }) => [
                    styles.mapLink,
                    {
                      backgroundColor: surface2,
                      borderColor: hovered || pressed ? primaryColor : borderColor,
                    },
                  ]}
                  onPress={(e) => {
                    e.stopPropagation?.();
                    Linking.openURL(`${APPLE_MAPS_SEARCH}${encodeURIComponent(address)}`);
                  }}
                  accessibilityRole="link"
                  accessibilityLabel={t("venue.card.openInAppleMaps")}
                >
                  <Icon name="navigate-outline" size={11} color={mutedColor} />
                  <ThemedText style={[styles.mapLinkText, { color: colors.text }]}>
                    Apple
                  </ThemedText>
                </Pressable>
              </>
            ) : (
              // One pill, opening the maps app the phone has: a choice between Google and
              // Apple is a browser's question, and on Android one of the two answers is a
              // web page. The pill takes the label the web row uses as its caption.
              <Pressable
                testID="card-directions"
                style={({ pressed }: { pressed: boolean }) => [
                  styles.mapLink,
                  styles.mapLinkNative,
                  {
                    backgroundColor: surface2,
                    borderColor: pressed ? primaryColor : borderColor,
                  },
                ]}
                onPress={(e) => {
                  e.stopPropagation?.();
                  openDirections(address);
                }}
                accessibilityRole="link"
                accessibilityLabel={t("venue.card.directions")}
              >
                <Icon name="navigate-outline" size="sm" color={primaryColor} />
                <ThemedText style={[styles.mapLinkText, { color: colors.text }]}>
                  {t("venue.card.directions")}
                </ThemedText>
              </Pressable>
            )}
          </View>

          {/* Time slots (or a no-reservations-needed empty state when bookings are disabled).
            Wrapped in a fixed-min-height area so cards line up whether or not they show slots. */}
          <View style={styles.slotsArea}>
            {walkInLocation || walkInToday ? (
              <View style={styles.walkInEmptyState} testID="walk-in-slot-notice">
                <Icon name="walk-outline" size="lg" color={mutedColor} />
                <ThemedText style={[styles.walkInEmptyText, { color: mutedColor }]}>
                  {t("venue.card.noReservationsRequired")}
                </ThemedText>
              </View>
            ) : (
              <>
                <View style={styles.slotLabel}>
                  <ThemedText style={[styles.slotLabelText, { color: mutedColor }]}>
                    {t("venue.card.availableSlots")}
                  </ThemedText>
                  <ThemedText style={[styles.slotLabelWhen, { color: colors.text }]}>
                    {t("venue.card.guestsToday", { count: party })}
                  </ThemedText>
                </View>
                {slotsLoading ? (
                  <ActivityIndicator
                    size="small"
                    color={primaryColor}
                    style={{ alignSelf: "flex-start" }}
                  />
                ) : slots.length === 0 ? (
                  <ThemedText style={[styles.noSlotsText, { color: mutedColor }]}>
                    {t("venue.card.noAvailableSlotsToday")}
                  </ThemedText>
                ) : (
                  <View style={styles.slotRow}>
                    {slots.map((s) => (
                      <Pressable
                        key={s.time}
                        onPress={(e) => {
                          e.stopPropagation?.();
                          haptics.selection();
                          router.push(
                            `/(user)/locations/${venue.id}?time=${encodeURIComponent(s.time)}&party=${party}` as Href
                          );
                        }}
                        accessibilityRole="button"
                        accessibilityLabel={t("venue.card.bookAt", {
                          time: s.time,
                          name: venue.name,
                        })}
                        style={({ hovered, pressed }: { hovered?: boolean; pressed: boolean }) => [
                          styles.slot,
                          {
                            backgroundColor: hovered || pressed ? primaryColor : surface2,
                            borderColor: hovered || pressed ? primaryColor : borderColor,
                          },
                        ]}
                      >
                        <ThemedText style={styles.slotText}>{s.time}</ThemedText>
                      </Pressable>
                    ))}
                  </View>
                )}
              </>
            )}
          </View>

          <View style={[styles.cardFoot, { borderTopColor: borderColor }]}>
            <View style={styles.hoursRow}>
              <Icon name="time-outline" size="xs" color={mutedColor} style={{ marginRight: 5 }} />
              {closedToday ? (
                <ThemedText style={[styles.hoursTime, { color: colors.text }]}>
                  {t("venue.card.closedToday")}
                </ThemedText>
              ) : (
                <ThemedText style={[styles.hoursTime, { color: colors.text }]}>
                  {hoursVary
                    ? t("venue.card.hoursToday", {
                        hours: `${todayHours.open} – ${todayHours.close}`,
                      })
                    : t("venue.card.hoursOpen", {
                        hours: `${todayHours.open} – ${todayHours.close}`,
                      })}
                </ThemedText>
              )}
            </View>
            <Pressable
              style={({ pressed }) => [
                cardStyles.viewBtn,
                pressed && { backgroundColor: surface2 },
              ]}
              onPress={() => openLocation()}
              accessibilityRole="link"
              accessibilityLabel={t("venue.card.seeDetailsFor", { name: venue.name })}
            >
              <ThemedText style={[cardStyles.viewBtnText, { color: primaryColor }]}>
                {t("venue.card.seeDetails")}
              </ThemedText>
              <Icon name="arrow-forward" size={13} color={primaryColor} />
            </Pressable>
          </View>
        </View>
      </View>
    </Pressable>
  );
}
