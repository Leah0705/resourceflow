import { Linking, Pressable, View } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import { Icon } from "@/components/common/Icon";
import { LinkedText } from "@/components/common/LinkedText";
import OpeningHoursTable from "@/components/venue/OpeningHoursTable";
import WalkInNotice from "@/components/booking/WalkInNotice";
import type { VenueDto } from "@/api/venues";
import { useAppTheme } from "@/hooks/use-app-theme";
import { LocationResourceMap } from "./LocationResourceMap";
import { styles } from "./LocationListItem.styles";
import { resolveServerUrl } from "@/utils/serverUrl";

export interface LocationDetailsPanelProps {
  venue: VenueDto;
  /** True for a location that never takes online bookings, which earns the standing notice. */
  walkInLocation: boolean;
  onJoinWaitlist: () => void;
  isDark: boolean;
  borderColor: string;
  mutedColor: string;
  primaryColor: string;
  surface2: string;
  accentSoft: string;
  accentBorder: string;
}

function MapLink({
  provider,
  url,
  textColor,
  mutedColor,
  borderColor,
  primaryColor,
  surface2,
}: {
  provider: string;
  url: string;
  textColor: string;
  mutedColor: string;
  borderColor: string;
  primaryColor: string;
  surface2: string;
}) {
  const { t } = useTranslation();
  return (
    <Pressable
      style={({ hovered, pressed }: { hovered?: boolean; pressed: boolean }) => [
        styles.mapLink,
        {
          backgroundColor: surface2,
          borderColor: hovered || pressed ? primaryColor : borderColor,
        },
      ]}
      onPress={() => Linking.openURL(url)}
      accessibilityRole="link"
      accessibilityLabel={t("venue.details.openInProviderMaps", { provider })}
    >
      <Icon name="navigate-outline" size="xs" color={mutedColor} />
      <ThemedText style={[styles.mapLinkText, { color: textColor }]}>{provider}</ThemedText>
    </Pressable>
  );
}

/** Everything a location row hides behind its Details accordion. */
export function LocationDetailsPanel({
  venue,
  walkInLocation,
  onJoinWaitlist,
  isDark,
  borderColor,
  mutedColor,
  primaryColor,
  surface2,
  accentSoft,
  accentBorder,
}: LocationDetailsPanelProps) {
  const { colors } = useAppTheme();
  const { t } = useTranslation();
  const mapLinkTheme = { textColor: colors.text, mutedColor, borderColor, primaryColor, surface2 };

  return (
    <View style={[styles.expandedBody, { borderTopColor: borderColor }]}>
      {venue.description ? (
        <LinkedText text={venue.description} style={styles.description} />
      ) : null}

      {venue.guideUrl ? (
        <Pressable
          style={({ hovered, pressed }: { hovered?: boolean; pressed: boolean }) => [
            styles.guideButton,
            {
              backgroundColor: hovered || pressed ? accentSoft : surface2,
              borderColor: hovered || pressed ? accentBorder : borderColor,
            },
          ]}
          onPress={() => Linking.openURL(resolveServerUrl(venue.guideUrl!))}
          accessibilityRole="link"
          accessibilityLabel={t("venue.details.openMenu")}
        >
          <Icon name="document-text-outline" size="md" color={primaryColor} />
          <ThemedText style={[styles.guideButtonText, { color: primaryColor }]}>
            {t("venue.details.viewGuide")}
          </ThemedText>
          <Icon name="open-outline" size={13} color={mutedColor} style={styles.guideButtonEnd} />
        </Pressable>
      ) : null}

      {venue.address && (
        <View style={styles.subSection}>
          <ThemedText type="defaultSemiBold" style={styles.subHeading}>
            {t("venue.details.directionsHeading")}
          </ThemedText>
          <View style={styles.mapLinks}>
            <MapLink
              provider="Google"
              url={`https://maps.google.com/?q=${encodeURIComponent(venue.address)}`}
              {...mapLinkTheme}
            />
            <MapLink
              provider="Apple"
              url={`https://maps.apple.com/?q=${encodeURIComponent(venue.address)}`}
              {...mapLinkTheme}
            />
          </View>
        </View>
      )}

      <View style={styles.subSection}>
        <ThemedText type="defaultSemiBold" style={styles.subHeading}>
          {t("venue.details.openingHoursHeading")}
        </ThemedText>
        <OpeningHoursTable venue={venue} />
      </View>

      {walkInLocation && <WalkInNotice scope="location" onJoinWaitlist={onJoinWaitlist} />}

      <LocationResourceMap
        venue={venue}
        isDark={isDark}
        borderColor={borderColor}
        mutedColor={mutedColor}
        primaryColor={primaryColor}
      />
    </View>
  );
}

export default LocationDetailsPanel;
