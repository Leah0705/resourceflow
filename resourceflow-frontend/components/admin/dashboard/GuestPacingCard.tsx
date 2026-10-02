import { View } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import type { LocationPacingDto } from "@/api/admin";
import { useAppTheme } from "@/hooks/use-app-theme";
import { theme } from "@/theme/theme";
import { BAR_MAX_HEIGHT, styles } from "./GuestPacingCard.styles";

/**
 * Today's arrivals per slot at each location with a guest cap, one bar per slot scaled to the
 * cap, so a manager can see where the cap bites. A full slot turns red. Renders nothing when no
 * location has a cap.
 *
 * @see [GuestPacingCard.test.tsx](../../../tests/components/admin/dashboard/GuestPacingCard.test.tsx):
 * pins that it stays silent without a cap and marks a slot at the cap as full.
 */
export function GuestPacingCard({ pacing }: { pacing: LocationPacingDto[] }) {
  const { t } = useTranslation();
  const { colors, primaryColor, isDark } = useAppTheme();
  if (pacing.length === 0) return null;

  const trackColor = isDark ? "rgba(255,255,255,0.08)" : "rgba(0,0,0,0.06)";

  return (
    <View
      testID="guest-pacing-card"
      style={[styles.card, { backgroundColor: colors.card, borderColor: colors.border }]}
    >
      <ThemedText style={styles.title}>{t("admin.dashboard.pacing.title")}</ThemedText>
      {pacing.map((location) => (
        <View key={location.venueId} style={styles.location}>
          <View style={styles.locationHeader}>
            <ThemedText style={styles.locationName}>{location.venueName}</ThemedText>
            <ThemedText style={[styles.meta, { color: colors.muted }]}>
              {t("admin.dashboard.pacing.cap", { count: location.maxGuestsPerSlot })}
            </ThemedText>
          </View>
          {location.slots.length === 0 ? (
            <ThemedText style={[styles.meta, { color: colors.muted }]}>
              {t("admin.dashboard.pacing.empty")}
            </ThemedText>
          ) : (
            <View style={styles.bars}>
              {location.slots.map((slot) => {
                const full = slot.guests >= location.maxGuestsPerSlot;
                const height =
                  Math.min(1, slot.guests / location.maxGuestsPerSlot) * BAR_MAX_HEIGHT;
                return (
                  <View
                    key={slot.time}
                    testID={`pacing-slot-${location.venueId}-${slot.time}`}
                    accessible
                    role="img"
                    accessibilityLabel={t("admin.dashboard.pacing.slotLabel", {
                      time: slot.time,
                      guests: slot.guests,
                      cap: location.maxGuestsPerSlot,
                    })}
                    style={styles.slot}
                  >
                    <ThemedText style={[styles.slotText, { color: colors.muted }]}>
                      {slot.guests}
                    </ThemedText>
                    <View style={[styles.track, { backgroundColor: trackColor }]}>
                      <View
                        testID={full ? "pacing-bar-full" : undefined}
                        style={[
                          styles.bar,
                          { height, backgroundColor: full ? theme.colors.error : primaryColor },
                        ]}
                      />
                    </View>
                    <ThemedText style={[styles.slotText, { color: colors.muted }]}>
                      {slot.time}
                    </ThemedText>
                  </View>
                );
              })}
            </View>
          )}
        </View>
      ))}
    </View>
  );
}
