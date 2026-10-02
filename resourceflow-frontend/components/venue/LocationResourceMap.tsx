import { useMemo } from "react";
import { View } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import { ThemedView } from "@/components/themed-view";
import { Icon } from "@/components/common/Icon";
import type { VenueDto } from "@/api/venues";
import { groupDisplayName, groupedResourceIds } from "@/utils/resourceGroups";
import { styles } from "./LocationListItem.styles";

export interface LocationResourceMapProps {
  venue: VenueDto;
  isDark: boolean;
  borderColor: string;
  mutedColor: string;
  primaryColor: string;
}

/**
 * Sections and their resources, plus the groups an admin has flagged as combinable.
 *
 * Grouped resources stay listed individually because they stay individually bookable — the
 * link glyph marks them rather than folding them away. A resource kept for walk-ins carries the
 * walk-in glyph and a line under its capacity, so a guest sees why it is never offered.
 */
export function LocationResourceMap({
  venue,
  isDark,
  borderColor,
  mutedColor,
  primaryColor,
}: LocationResourceMapProps) {
  const { t } = useTranslation();
  const resourceGroups = venue.groups ?? [];
  // Keyed off venue.groups, not resourceGroups — the `?? []` fallback is a fresh array each
  // render, which would defeat the memo on locations that have no groups.
  const groupMemberIds = useMemo(() => groupedResourceIds(venue.groups ?? []), [venue.groups]);

  if (venue.sections.length === 0) return null;

  return (
    <View style={styles.subSection}>
      <ThemedText type="defaultSemiBold" style={styles.subHeading}>
        {t("venue.resourceMap.heading")}
      </ThemedText>
      <View style={styles.sectionsGrid}>
        {venue.sections.map((section) => (
          <ThemedView key={section.id} style={[styles.sectionCard, { borderColor }]}>
            <ThemedText style={styles.sectionName}>{section.name}</ThemedText>
            <View style={styles.resourceGrid}>
              {section.resources.map((resource) => (
                <View
                  key={resource.id}
                  style={[
                    styles.resourceChip,
                    {
                      backgroundColor: isDark ? "rgba(255,255,255,0.06)" : "rgba(0,0,0,0.04)",
                      borderColor,
                    },
                  ]}
                >
                  <View style={styles.resourceNameRow}>
                    <ThemedText style={styles.resourceName}>
                      {resource.name ??
                        t("venue.resourceMap.resourceFallbackName", { id: resource.id })}
                    </ThemedText>
                    {groupMemberIds.has(resource.id) && (
                      <Icon name="link" size={11} color={primaryColor} />
                    )}
                    {resource.walkInOnly && (
                      <Icon name="walk-outline" size={11} color={mutedColor} />
                    )}
                  </View>
                  <ThemedText style={[styles.resourceCapacity, { color: mutedColor }]}>
                    {t("venue.resourceMap.capacityCount", { count: resource.capacity })}
                  </ThemedText>
                  {resource.walkInOnly && (
                    <ThemedText style={[styles.resourceCapacity, { color: mutedColor }]}>
                      {t("venue.resourceMap.walkInOnly")}
                    </ThemedText>
                  )}
                </View>
              ))}
            </View>
          </ThemedView>
        ))}
      </View>

      {resourceGroups.length > 0 && (
        <View style={styles.groupBlock}>
          <ThemedText style={[styles.groupBlockHeading, { color: mutedColor }]}>
            {t("venue.resourceMap.combinableHeading")}
          </ThemedText>
          {resourceGroups.map((group) => (
            <View
              key={group.id}
              style={[
                styles.groupRow,
                { borderColor: `${primaryColor}55`, backgroundColor: `${primaryColor}12` },
              ]}
            >
              <Icon name="link" size={15} color={primaryColor} />
              <View style={styles.groupTextCol}>
                <ThemedText style={styles.groupName}>{groupDisplayName(group)}</ThemedText>
                <ThemedText style={[styles.groupCapacity, { color: mutedColor }]}>
                  {t("venue.resourceMap.capacityUpTo", { count: group.combinedCapacity })}
                </ThemedText>
              </View>
            </View>
          ))}
        </View>
      )}
    </View>
  );
}

export default LocationResourceMap;
