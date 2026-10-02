import { useEffect, useState } from "react";
import { Modal, Pressable, View, ScrollView, ActivityIndicator } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import Button from "@/components/common/Button";
import { ButtonRow } from "@/components/common/ButtonRow";
import { useAppTheme } from "@/hooks/use-app-theme";
import {
  adminGetVenues,
  pauseVenueBookings,
  unpauseVenueBookings,
  extendVenueBookings,
  BookingDetailDto,
} from "@/api/admin";
import { styles } from "./VenueActionModal.styles";
import { theme } from "@/theme/theme";
import { Icon } from "@/components/common/Icon";
import { fmtTime } from "@/utils/formatters";

interface VenueActionModalProps {
  visible: boolean;
  onClose: () => void;
  actionType: "pause" | "extend";
  onSuccess?: (message: string) => void;
}

export default function VenueActionModal({
  visible,
  onClose,
  actionType,
  onSuccess,
}: VenueActionModalProps) {
  const { t } = useTranslation();
  const { colors, primaryColor } = useAppTheme();
  const [venues, setVenues] = useState<
    { id: number; name: string; bookingsPausedUntil?: string; activeBookingsCount?: number }[]
  >([]);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState<number | null>(null);
  const [extendedBookings, setExtendedBookings] = useState<BookingDetailDto[] | null>(null);
  const [willPauseUntil, setWillPauseUntil] = useState("");

  async function loadVenues() {
    setExtendedBookings(null);
    setWillPauseUntil(fmtTime(new Date(Date.now() + 60 * 60 * 1000)));
    setLoading(true);
    try {
      const data = await adminGetVenues();
      setVenues(data);
    } catch (err) {
      console.error("Failed to load locations", err);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (visible) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      loadVenues();
    }
  }, [visible]);

  async function handleAction(venueId: number, venueName: string, isCurrentlyPaused: boolean) {
    if (submitting !== null) return;
    setSubmitting(venueId);

    try {
      if (actionType === "extend") {
        const result = await extendVenueBookings(venueId, 60);
        if (result.ok) {
          if (result.extendedBookings.length > 0) {
            setExtendedBookings(result.extendedBookings);
          } else {
            onSuccess?.(t("admin.bookings.venueAction.noActiveToExtend", { name: venueName }));
            onClose();
          }
        }
      } else {
        if (isCurrentlyPaused) {
          await unpauseVenueBookings(venueId);
          onSuccess?.(t("admin.bookings.venueAction.resumedSuccess", { name: venueName }));
        } else {
          await pauseVenueBookings(venueId, 60);
          onSuccess?.(
            t("admin.bookings.venueAction.pausedSuccess", {
              name: venueName,
              time: willPauseUntil,
            })
          );
        }
        onClose();
      }
    } catch (err) {
      console.error(`Failed to ${actionType} bookings for venue`, err);
    } finally {
      setSubmitting(null);
    }
  }

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <View style={styles.backdrop}>
        <View
          role="dialog"
          aria-modal
          accessibilityViewIsModal
          accessibilityLabel={
            extendedBookings
              ? t("admin.bookings.venueAction.a11y.extended")
              : actionType === "pause"
                ? t("admin.bookings.venueAction.a11y.pause")
                : t("admin.bookings.venueAction.a11y.extend")
          }
          style={[styles.content, { backgroundColor: colors.card, borderColor: colors.border }]}
        >
          <View style={styles.header}>
            <ThemedText style={styles.title} accessibilityRole="header">
              {extendedBookings
                ? t("admin.bookings.venueAction.title.extended")
                : actionType === "pause"
                  ? t("admin.bookings.venueAction.title.pause")
                  : t("admin.bookings.venueAction.title.extend")}
            </ThemedText>
            <Pressable
              onPress={onClose}
              style={styles.closeBtn}
              testID="close-modal-button"
              accessibilityRole="button"
              accessibilityLabel={t("common.actions.close")}
              hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}
            >
              <Icon name="close" size="xxl" color={colors.muted} />
            </Pressable>
          </View>

          <ThemedText style={[styles.subtitle, { color: colors.muted }]}>
            {extendedBookings
              ? t("admin.bookings.venueAction.extendedSubtitle", {
                  count: extendedBookings.length,
                })
              : actionType === "pause"
                ? t("admin.bookings.venueAction.pauseSubtitle")
                : t("admin.bookings.venueAction.extendSubtitle")}
          </ThemedText>

          {loading ? (
            <ActivityIndicator
              style={styles.spinner}
              color={primaryColor}
              testID="loading-indicator"
              accessibilityLabel={t("admin.bookings.venueAction.loadingVenues")}
            />
          ) : extendedBookings ? (
            <>
              <ScrollView style={styles.list}>
                {extendedBookings.map((b) => (
                  <View key={b.id} style={[styles.item, { borderBottomColor: colors.border }]}>
                    <View style={styles.itemMain}>
                      <ThemedText style={styles.itemName}>{b.customerEmail}</ThemedText>
                      <ThemedText style={[styles.itemMeta, { color: colors.muted }]}>
                        {fmtTime(new Date(b.date))}
                        {" → "}
                        {b.endTime
                          ? fmtTime(new Date(b.endTime))
                          : t("admin.bookings.venueAction.extendedFallback")}
                        {` · ${t("booking.form.partySize", { count: b.partySize })}`}
                      </ThemedText>
                    </View>
                  </View>
                ))}
              </ScrollView>
              <ButtonRow style={styles.footer}>
                <Button size="md" onPress={onClose}>
                  {t("admin.bookings.venueAction.doneAction")}
                </Button>
              </ButtonRow>
            </>
          ) : venues.length === 0 ? (
            <View style={styles.empty}>
              <ThemedText style={{ color: colors.muted }}>
                {t("admin.bookings.venueAction.noVenues")}
              </ThemedText>
            </View>
          ) : (
            <ScrollView style={styles.list}>
              {venues.map((r) => {
                const isPaused = r.bookingsPausedUntil
                  ? new Date(r.bookingsPausedUntil) > new Date()
                  : false;

                return (
                  <Pressable
                    key={r.id}
                    onPress={() => handleAction(r.id, r.name, isPaused)}
                    disabled={submitting !== null}
                    accessibilityRole="button"
                    accessibilityLabel={t(
                      isPaused
                        ? "admin.bookings.venueAction.resumeLabel"
                        : "admin.bookings.venueAction.pauseLabel",
                      { name: r.name }
                    )}
                    accessibilityState={{ disabled: submitting !== null }}
                    style={({ pressed }) => [
                      styles.item,
                      { borderBottomColor: colors.border },
                      pressed && { backgroundColor: `${colors.muted}10` },
                    ]}
                  >
                    <View style={styles.itemMain}>
                      <View style={{ flexDirection: "row", alignItems: "center", gap: 8 }}>
                        <ThemedText style={styles.itemName}>{r.name}</ThemedText>
                        {isPaused && (
                          <View
                            style={{
                              backgroundColor: "#fee2e2",
                              paddingHorizontal: 6,
                              paddingVertical: 2,
                              borderRadius: 4,
                            }}
                          >
                            <ThemedText
                              style={{ color: theme.colors.error, fontSize: 10, fontWeight: "700" }}
                            >
                              {t("admin.bookings.venueAction.pausedBadge")}
                            </ThemedText>
                          </View>
                        )}
                      </View>
                      <ThemedText style={[styles.itemMeta, { color: colors.muted }]}>
                        {isPaused
                          ? t("admin.bookings.venueAction.pausedUntil", {
                              time: fmtTime(new Date(r.bookingsPausedUntil!)),
                            })
                          : actionType === "pause"
                            ? t("admin.bookings.venueAction.willPauseUntil", {
                                time: willPauseUntil,
                              })
                            : t("admin.bookings.venueAction.activeBookingsCount", {
                                count: r.activeBookingsCount ?? 0,
                              })}
                      </ThemedText>
                    </View>
                    {submitting === r.id ? (
                      <ActivityIndicator size="small" color={primaryColor} />
                    ) : (
                      <Icon
                        name={
                          actionType === "extend"
                            ? "time-outline"
                            : isPaused
                              ? "play-circle-outline"
                              : "pause-circle-outline"
                        }
                        size="xl"
                        color={
                          isPaused && actionType === "pause" ? theme.colors.success : primaryColor
                        }
                      />
                    )}
                  </Pressable>
                );
              })}
            </ScrollView>
          )}
        </View>
      </View>
    </Modal>
  );
}
