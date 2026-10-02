import { useEffect, useRef, useState } from "react";
import { GestureResponderEvent, Linking, Pressable, View } from "react-native";
import { haptics } from "@/utils/haptics";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import { useAppTheme } from "@/hooks/use-app-theme";
import { VenueDto } from "@/api/venues";
import {
  getHoursForDate,
  getIsoDayFromDateString,
  getNextOpening,
  isoDayShortName,
} from "@/utils/openingHours";
import { isWalkInOnlyOnDay, walkInBadgeLabel } from "@/utils/walkIn";
import { hexToRgb } from "@/utils/colors";
import { getOpenDaysList } from "@/utils/venueTime";
import { AnimatedAccordion } from "@/components/common/AnimatedAccordion";
import { cardStyles } from "@/components/venue/cardStyles";
import type { TimeWindow } from "@/components/venue/LocationsFilterBar";
import { LocationDetailsPanel } from "./LocationDetailsPanel";
import { VenueTags } from "./VenueTags";
import { LocationSlotRow } from "./LocationSlotRow";
import { LocationThumbnail } from "./LocationThumbnail";
import { useLocationSlots } from "./useLocationSlots";
import { styles } from "./LocationListItem.styles";
import { Icon } from "@/components/common/Icon";
import Button from "@/components/common/Button";
import { resolveServerUrl } from "@/utils/serverUrl";

const SLOTS_SHOWN_WIDE = 5;
const SLOTS_SHOWN_COMPACT = 3;

/**
 * A single location row in the Locations list: header + times now, details behind an accordion.
 */
export default function LocationListItem({
  venue,
  partySize,
  date,
  timeWindow,
  today,
  defaultExpanded = false,
  compact = false,
  registerRef,
  onExpand,
  onBook,
  onJoinWaitlist,
  onAvailabilityChange,
}: {
  venue: VenueDto;
  partySize: number;
  date: string;
  timeWindow: TimeWindow;
  /** Today's date in the brand's timezone, for "today"-relative copy. */
  today: string;
  defaultExpanded?: boolean;
  /** Phone-width layout: stacked rows, smaller thumbnail, fewer slots. */
  compact?: boolean;
  registerRef: (id: number, ref: View | null) => void;
  onExpand?: (id: number) => void;
  onBook: (venue: VenueDto, time: string) => void;
  onJoinWaitlist: (venue: VenueDto) => void;
  /**
   * Reports how many times this location can offer under the current filters, so the
   * page bar can summarise ("2 of 3 locations have resources"). `null` while loading.
   */
  onAvailabilityChange?: (id: number, availableSlots: number | null) => void;
}) {
  const { colors, isDark, primaryColor } = useAppTheme();
  const { t } = useTranslation();
  const mutedColor = colors.muted;
  const borderColor = colors.border;

  const [expanded, setExpanded] = useState(defaultExpanded);
  const itemRef = useRef<View>(null);

  const selectedIsoDay = getIsoDayFromDateString(date);
  const isToday = date === today;
  const dayHours = getHoursForDate(venue, date);
  const openDaysList = getOpenDaysList(venue);
  const closedOnDate = !openDaysList.includes(selectedIsoDay);
  const walkInLocation = !!venue.walkInOnly;
  const walkInOnDate = !walkInLocation && isWalkInOnlyOnDay(venue, selectedIsoDay);
  const walkInBadgeText = walkInBadgeLabel(venue);
  /** Whether the selected date can be booked — governs the slot strip. */
  const bookable = !closedOnDate && !walkInLocation && !walkInOnDate;
  const tags = venue.tags ?? [];
  /**
   * Whether the location takes online bookings on *any* day — governs the Book now CTA.
   * Today being shut, or walk-in only, is no reason to withhold the route to next Tuesday;
   * the panel opens on the notice for this day and its date picker moves the guest on. Only
   * a location with no bookable day anywhere has nothing behind the button.
   */
  const takesOnlineBookings = openDaysList.some((day) => !isWalkInOnlyOnDay(venue, day));

  useEffect(() => {
    registerRef(venue.id, itemRef.current);
    return () => registerRef(venue.id, null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [venue.id]);

  const { slotsLoading, usableSlots } = useLocationSlots({
    venueId: venue.id,
    date,
    partySize,
    timeWindow,
    bookable,
    isToday,
    timezone: venue.timezone ?? "UTC",
    onAvailabilityChange,
  });

  const accent = hexToRgb(primaryColor);
  const accentSoft = `rgba(${accent.r},${accent.g},${accent.b},${isDark ? 0.15 : 0.12})`;
  const accentBorder = `rgba(${accent.r},${accent.g},${accent.b},0.3)`;
  const surface2 = colors.surfaceAlt;

  const nextOpening = closedOnDate ? getNextOpening(venue, selectedIsoDay, openDaysList) : null;

  const toggleExpanded = (event?: GestureResponderEvent) => {
    // The card body is the toggle, so the controls slot on top of it (the guide link, the
    // Details and Book now buttons) have to keep their press from reaching it.
    event?.stopPropagation?.();
    haptics.selection();
    setExpanded((prev) => {
      const next = !prev;
      if (next && onExpand) onExpand(venue.id);
      return next;
    });
  };

  const bodyPressProps = {
    onPress: toggleExpanded,
    accessibilityRole: "button" as const,
    accessibilityState: { expanded },
    accessibilityLabel: expanded
      ? t("venue.locationListItem.hideDetailsFor", { name: venue.name })
      : t("venue.locationListItem.showDetailsFor", { name: venue.name }),
  };

  const walkInBadge = walkInBadgeText ? (
    <View style={[cardStyles.badge, cardStyles.badgeMuted]}>
      <Icon name="walk-outline" size={11} color="#fff" />
      <ThemedText style={cardStyles.badgeText}>{walkInBadgeText}</ThemedText>
    </View>
  ) : null;

  const detailsToggle = (
    <Pressable
      testID={`location-details-toggle-${venue.id}`}
      onPress={toggleExpanded}
      accessibilityRole="button"
      accessibilityState={{ expanded }}
      accessibilityLabel={
        expanded ? t("venue.locationListItem.hideDetails") : t("venue.locationListItem.showDetails")
      }
      style={({ hovered, pressed }: { hovered?: boolean; pressed: boolean }) => [
        cardStyles.viewBtn,
        styles.detailsBtn,
        (hovered || pressed) && { backgroundColor: surface2 },
      ]}
    >
      <ThemedText style={[cardStyles.viewBtnText, { color: primaryColor }]}>
        {t("venue.locationListItem.detailsLabel")}
      </ThemedText>
      <Icon name={expanded ? "chevron-up" : "chevron-down"} size={13} color={primaryColor} />
    </Pressable>
  );

  // Opens the booking panel without going through a time chip — the guest may want a time
  // that isn't on the strip, and a location with nothing free today has no chip to press at
  // all. The panel's own pickers take it from here, so the earliest slot is only a seed.
  const bookNowButton = takesOnlineBookings ? (
    <Button
      testID={`location-book-now-${venue.id}`}
      size="sm"
      accessibilityLabel={t("venue.locationListItem.bookResourceAt", { name: venue.name })}
      onPress={(event) => {
        // Button fires its own haptic; this only has to keep the press off the card body.
        event?.stopPropagation?.();
        onBook(venue, usableSlots[0]?.time ?? dayHours.open);
      }}
    >
      {t("venue.locationListItem.bookNow")}
    </Button>
  ) : null;

  // The waitlist is today's queue, so a walk-in day offers it only when that day is today.
  const takesWaitlist = walkInLocation || (walkInOnDate && isToday);
  const joinWaitlist = () => onJoinWaitlist(venue);
  const joinWaitlistButton =
    takesWaitlist && !bookNowButton ? (
      <Button
        testID={`location-join-waitlist-${venue.id}`}
        size="sm"
        accessibilityLabel={t("venue.locationListItem.joinWaitlistAt", {
          name: venue.name,
        })}
        onPress={(event) => {
          event?.stopPropagation?.();
          joinWaitlist();
        }}
      >
        {t("venue.locationListItem.joinWaitlist")}
      </Button>
    ) : null;

  const headerActions = (
    <View style={styles.headerActions}>
      {detailsToggle}
      {bookNowButton ?? joinWaitlistButton}
    </View>
  );

  const thumbnail = (
    <LocationThumbnail
      name={venue.name}
      imageUrl={venue.imageUrl}
      size={compact ? 64 : 108}
      compact={compact}
      accent={accent}
    />
  );

  const walkInLine =
    walkInLocation || walkInOnDate ? (
      <View style={styles.walkInRow}>
        <Icon name="walk-outline" size={15} color={mutedColor} />
        <ThemedText style={[styles.walkInText, { color: mutedColor }]}>
          {t("venue.locationListItem.noReservationsFirstCome")}
        </ThemedText>
      </View>
    ) : null;

  const slotRow = bookable ? (
    <LocationSlotRow
      venueId={venue.id}
      venueName={venue.name}
      slots={usableSlots}
      loading={slotsLoading}
      maxShown={compact ? SLOTS_SHOWN_COMPACT : SLOTS_SHOWN_WIDE}
      compact={compact}
      primaryColor={primaryColor}
      mutedColor={mutedColor}
      borderColor={borderColor}
      surface2={surface2}
      onBook={(time) => onBook(venue, time)}
    />
  ) : (
    walkInLine
  );

  const addressMeta = venue.address ? (
    <View style={styles.metaItem}>
      <Icon name="location-outline" size="xs" color={mutedColor} />
      <ThemedText style={[styles.metaText, { color: mutedColor }]} numberOfLines={1}>
        {venue.address}
      </ThemedText>
    </View>
  ) : null;

  const guideLink = venue.guideUrl ? (
    <Pressable
      style={styles.metaItem}
      onPress={(event) => {
        event?.stopPropagation?.();
        Linking.openURL(resolveServerUrl(venue.guideUrl!));
      }}
      accessibilityRole="link"
      accessibilityLabel={t("venue.locationListItem.viewGuide")}
    >
      <Icon name="document-text-outline" size="xs" color={primaryColor} />
      <ThemedText style={[styles.metaText, styles.metaLink, { color: primaryColor }]}>
        {t("venue.locationListItem.guideLabel")}
      </ThemedText>
    </Pressable>
  ) : null;

  const dayLabel = isToday ? t("venue.locationListItem.today") : isoDayShortName(selectedIsoDay);
  const closedLabel = t("venue.locationListItem.closedLabel", { day: dayLabel });
  const hoursLabel = !closedOnDate
    ? t("venue.locationListItem.hoursRange", {
        open: dayHours.open,
        close: dayHours.close,
        day: dayLabel,
      })
    : nextOpening
      ? t("venue.locationListItem.closedOpensNext", {
          closedLabel,
          day: isoDayShortName(nextOpening.isoDay),
          time: nextOpening.open,
        })
      : closedLabel;

  const hoursMeta = (
    <View style={styles.metaItem}>
      <Icon name="time-outline" size="xs" color={mutedColor} />
      <ThemedText style={[styles.metaText, { color: mutedColor }]}>{hoursLabel}</ThemedText>
    </View>
  );

  return (
    <View
      ref={itemRef}
      testID={`location-item-${venue.id}`}
      style={[
        styles.item,
        { backgroundColor: colors.card, borderColor },
        expanded && { borderColor: colors.borderStrong },
      ]}
    >
      {compact ? (
        <View style={styles.compactHeader}>
          <Pressable
            {...bodyPressProps}
            testID={`location-body-${venue.id}`}
            style={styles.compactTopRow}
          >
            {thumbnail}
            <View style={styles.compactIdentity}>
              <ThemedText style={styles.name} numberOfLines={1}>
                {venue.name}
              </ThemedText>
              {addressMeta}
              <VenueTags tags={tags} style={styles.tagRow} />
              {walkInBadge ? <View style={styles.badgeRow}>{walkInBadge}</View> : null}
            </View>
          </Pressable>

          {bookable ? slotRow : null}

          <View style={[styles.compactFoot, { borderTopColor: borderColor }]}>
            <View testID={`location-foot-lead-${venue.id}`} style={styles.compactFootLead}>
              {walkInLine ?? hoursMeta}
            </View>
            {headerActions}
          </View>
        </View>
      ) : (
        <View style={styles.wideHeader}>
          {thumbnail}
          <View style={styles.wideContent}>
            <View style={styles.wideTopRow}>
              <Pressable
                {...bodyPressProps}
                testID={`location-body-${venue.id}`}
                style={styles.wideIdentity}
              >
                <View style={styles.badgeRow}>
                  <ThemedText style={styles.name} numberOfLines={1}>
                    {venue.name}
                  </ThemedText>
                  {walkInBadge}
                </View>
                <View style={styles.metaRow}>
                  {addressMeta}
                  {hoursMeta}
                  {guideLink}
                </View>
                <VenueTags tags={tags} style={styles.tagRow} />
              </Pressable>
              {headerActions}
            </View>
            {slotRow}
          </View>
        </View>
      )}

      <AnimatedAccordion expanded={expanded}>
        <LocationDetailsPanel
          venue={venue}
          walkInLocation={walkInLocation}
          onJoinWaitlist={joinWaitlist}
          isDark={isDark}
          borderColor={borderColor}
          mutedColor={mutedColor}
          primaryColor={primaryColor}
          surface2={surface2}
          accentSoft={accentSoft}
          accentBorder={accentBorder}
        />
      </AnimatedAccordion>
    </View>
  );
}
