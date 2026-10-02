import { useState } from "react";
import { View, Pressable } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import Input from "@/components/common/Input";
import Select from "@/components/common/Select";
import { theme, getThemeColors } from "@/theme/theme";
import {
  ResourceDto,
  deleteResource,
  updateResource,
  fetchResourceDeleteImpact,
} from "@/api/venues";
import { useAppTheme } from "@/hooks/use-app-theme";
import { hexToRgba } from "@/utils/colors";
import { buildPartySizeOptions } from "@/utils/partySizeOptions";
import { styles as settingsStyles } from "./settings.styles";
import { styles } from "./ResourceRow.styles";
import Button from "@/components/common/Button";
import { ButtonRow } from "@/components/common/ButtonRow";
import { RowTextButton } from "@/components/common/RowTextButton";
import { Icon } from "@/components/common/Icon";

/**
 * Group membership context for a resource row. When present, the row renders inside a
 * combinable group: a ⛓ chip with the group label and a remove (unlink) affordance.
 */
export interface ResourceRowGroupContext {
  id: number;
  /** Pre-formatted chip label, e.g. "Resources 8 + 9 (8 combined)" or "Quiet pods (8 places)". */
  label: string;
  combinedCapacity: number;
}

/**
 * One resource in the admin's section list, in four mutually exclusive modes: selection (picking
 * resources to combine), delete confirmation, edit, and the default row.
 *
 * Destroying a resource is two taps, and the confirmation replaces the row in place rather than
 * opening a centre-screen modal, so the consequence it names — the count of future bookings that
 * would lose their resource reference — is read next to the resource it applies to.
 */
export function ResourceRow({
  resource,
  venueId,
  sectionId,
  isDark,
  borderColor,
  onUpdated,
  onDeleted,
  group,
  onLink,
  onUnlink,
  selectionMode = false,
  selected = false,
  onToggleSelect,
  disabledInSelection = false,
}: {
  resource: ResourceDto;
  venueId: number;
  sectionId: number;
  isDark: boolean;
  borderColor: string;
  onUpdated: (t: ResourceDto) => void;
  onDeleted: () => void;
  /** Group membership context; undefined when the resource is standalone. */
  group?: ResourceRowGroupContext;
  /** Enter selection mode to start a new group from this resource. Standalone rows only. */
  onLink?: () => void;
  /** Remove this resource from its group. Grouped rows only. */
  onUnlink?: () => void;
  /** True while SectionBlock is in combine-selection mode. */
  selectionMode?: boolean;
  /** Whether this row is currently selected in selection mode. */
  selected?: boolean;
  /** Toggle selection in selection mode. */
  onToggleSelect?: () => void;
  /** Disabled (already grouped) in selection mode. */
  disabledInSelection?: boolean;
}) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [draftName, setDraftName] = useState(resource.name ?? "");
  const [draftCapacity, setDraftCapacity] = useState(resource.capacity);
  const [draftWalkInOnly, setDraftWalkInOnly] = useState(!!resource.walkInOnly);
  const [saving, setSaving] = useState(false);
  const [deleteStep, setDeleteStep] = useState<"idle" | "confirm">("idle");
  /** Null while loading, and when the read fails — the confirmation falls back to generic copy. */
  const [impact, setImpact] = useState<number | null>(null);
  const [impactLoading, setImpactLoading] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const colors = getThemeColors(isDark);
  const mutedColor = colors.muted;
  const { primaryColor } = useAppTheme();

  const resourceName =
    resource.name ?? t("admin.settings.resourceRow.resourceFallbackName", { id: resource.id });
  // Shared with SocialLinkRow / HighlightsCard so the resource tiles are indistinguishable from the
  // rest of the settings cards.
  const surface2 = isDark ? "#252729" : "#f9fafb";
  const cardBg = isDark ? "#1e2022" : "#ffffff";
  const surfaceMuted = isDark ? "rgba(255,255,255,0.08)" : "rgba(0,0,0,0.06)";

  const startDelete = async () => {
    setDeleteStep("confirm");
    setImpact(null);
    setImpactLoading(true);
    const result = await fetchResourceDeleteImpact(venueId, sectionId, resource.id);
    // Best-effort: a failed read must not block the destructive action behind a count.
    setImpact(result?.bookings ?? null);
    setImpactLoading(false);
  };

  const cancelDelete = () => {
    setDeleteStep("idle");
    setImpact(null);
    setImpactLoading(false);
  };

  const confirmDelete = async () => {
    setDeleting(true);
    const success = await deleteResource(venueId, sectionId, resource.id);
    setDeleting(false);
    if (success) onDeleted();
  };

  // A resource already in a group keeps its chip but is locked: a resource belongs to one group.
  if (selectionMode) {
    const isGrouped = !!group || disabledInSelection;
    return (
      <Pressable
        testID={`resource-select-row-${resource.id}`}
        onPress={isGrouped ? undefined : onToggleSelect}
        disabled={isGrouped}
        style={[
          styles.selectTile,
          {
            borderColor,
            backgroundColor: selected ? hexToRgba(primaryColor, 0.08) : surface2,
          },
          isGrouped && styles.selectTileLocked,
        ]}
      >
        <Icon
          name={isGrouped ? "lock-closed" : selected ? "checkbox" : "square-outline"}
          size="md"
          color={isGrouped ? mutedColor : primaryColor}
        />
        <View style={settingsStyles.tileCopy}>
          <ThemedText style={settingsStyles.tileTitle} numberOfLines={1}>
            {resource.name ??
              t("admin.settings.resourceRow.resourceFallbackShort", { id: resource.id })}
          </ThemedText>
          <View style={styles.capacityRow}>
            <Icon name="people-outline" size="xs" color={mutedColor} />
            <ThemedText style={[styles.capacityText, { color: mutedColor }]}>
              {t("admin.settings.resourceRow.capacityCount", { count: resource.capacity })}
            </ThemedText>
            {isGrouped && group && (
              <View
                testID={`resource-group-chip-${resource.id}`}
                style={[
                  styles.groupChip,
                  styles.groupChipSelect,
                  { backgroundColor: hexToRgba(primaryColor, 0.12) },
                ]}
              >
                <Icon name="link" size={11} color={primaryColor} />
                <ThemedText style={[styles.groupChipText, { color: primaryColor }]}>
                  {group.label}
                </ThemedText>
              </View>
            )}
          </View>
        </View>
      </Pressable>
    );
  }

  if (!editing && deleteStep === "confirm") {
    return (
      <View
        style={[
          styles.confirmTile,
          {
            borderColor: hexToRgba(theme.colors.error, 0.4),
            backgroundColor: isDark
              ? hexToRgba(theme.colors.error, 0.1)
              : hexToRgba(theme.colors.error, 0.05),
          },
        ]}
      >
        <View style={settingsStyles.confirmCopy}>
          <Icon
            name="warning-outline"
            size={15}
            color={theme.colors.error}
            style={settingsStyles.confirmIcon}
          />
          <ThemedText style={settingsStyles.confirmText}>
            <ThemedText style={settingsStyles.confirmTextStrong}>
              {t("admin.settings.resourceRow.deleteConfirmTitle", { name: resourceName })}
            </ThemedText>{" "}
            {impactLoading
              ? t("admin.settings.resourceRow.impactLoading")
              : impact && impact > 0
                ? t("admin.settings.resourceRow.impactCount", { count: impact })
                : t("admin.settings.resourceRow.impactUnknown")}{" "}
            {t("admin.settings.resourceRow.cannotBeUndone")}
          </ThemedText>
        </View>
        <ButtonRow>
          <Button
            testID="resource-delete-cancel-btn"
            variant="secondary"
            tone="neutral"
            size="md"
            onPress={cancelDelete}
            disabled={deleting}
          >
            {t("common.actions.cancel")}
          </Button>
          <Button
            testID="resource-delete-confirm-btn"
            tone="danger"
            size="md"
            disabled={deleting}
            loading={deleting}
            onPress={confirmDelete}
          >
            {deleting
              ? t("admin.settings.resourceRow.deleting")
              : t("admin.settings.resourceRow.yesDelete")}
          </Button>
        </ButtonRow>
      </View>
    );
  }

  if (!editing) {
    return (
      <View style={[settingsStyles.tile, { backgroundColor: surface2, borderColor }]}>
        <View style={[settingsStyles.tileIcon, { backgroundColor: cardBg, borderColor }]}>
          <Icon name={group ? "link" : "grid-outline"} size="lg" color={primaryColor} />
        </View>

        <View style={settingsStyles.tileCopy}>
          <ThemedText style={settingsStyles.tileTitle} numberOfLines={1}>
            {resource.name ??
              t("admin.settings.resourceRow.resourceFallbackShort", { id: resource.id })}
          </ThemedText>
          <View style={styles.rowCapacityRow}>
            <Icon name="people-outline" size="xs" color={mutedColor} />
            <ThemedText style={[styles.capacityText, { color: mutedColor }]}>
              {t("admin.settings.resourceRow.capacityCount", { count: resource.capacity })}
            </ThemedText>
            {group && (
              <View
                testID={`resource-group-chip-${resource.id}`}
                style={[
                  styles.groupChip,
                  styles.groupChipRow,
                  { backgroundColor: hexToRgba(primaryColor, 0.14) },
                ]}
              >
                <Icon name="link" size={11} color={primaryColor} />
                <ThemedText style={[styles.groupChipText, { color: primaryColor }]}>
                  {group.label}
                </ThemedText>
                <Pressable
                  testID={`resource-unlink-btn-${resource.id}`}
                  onPress={onUnlink}
                  accessibilityRole="button"
                  accessibilityLabel={t("admin.settings.resourceRow.removeFromGroupLabel", {
                    name: resourceName,
                  })}
                  hitSlop={{ top: 6, bottom: 6, left: 6, right: 6 }}
                  style={styles.unlinkBtn}
                >
                  <Icon name="close" size="xs" color={primaryColor} />
                </Pressable>
              </View>
            )}
            {resource.walkInOnly && (
              <View
                testID={`resource-walk-in-chip-${resource.id}`}
                style={[styles.groupChip, styles.walkInChip, { backgroundColor: surfaceMuted }]}
              >
                <Icon name="walk-outline" size={11} color={mutedColor} />
                <ThemedText style={[styles.groupChipText, { color: mutedColor }]}>
                  {t("admin.settings.resourceRow.walkInOnlyChip")}
                </ThemedText>
              </View>
            )}
          </View>
        </View>

        {/* Trailing actions: every one is a named pill. The admin runs on tablets, often for
            staff who do not use the app daily, so a bare glyph is not enough of a label. */}
        <View style={styles.rowActions}>
          {!group && (
            <RowTextButton
              testID={`resource-link-btn-${resource.id}`}
              label={t("admin.settings.resourceRow.combine")}
              icon="link-outline"
              color={primaryColor}
              onPress={onLink}
              accessibilityLabel={t("admin.settings.resourceRow.combineLabel", {
                name: resourceName,
              })}
            />
          )}
          <RowTextButton
            testID={`resource-edit-btn-${resource.id}`}
            label={t("admin.settings.resourceRow.edit")}
            icon="pencil-outline"
            color={mutedColor}
            onPress={() => {
              setDraftName(resource.name ?? "");
              setDraftCapacity(resource.capacity);
              setDraftWalkInOnly(!!resource.walkInOnly);
              setEditing(true);
            }}
            accessibilityLabel={t("admin.settings.resourceRow.editLabel", { name: resourceName })}
          />
          <RowTextButton
            testID={`resource-delete-btn-${resource.id}`}
            label={t("admin.settings.resourceRow.delete")}
            icon="trash-outline"
            color={theme.colors.error}
            onPress={startDelete}
            accessibilityLabel={t("admin.settings.resourceRow.deleteLabel", { name: resourceName })}
          />
        </View>
      </View>
    );
  }

  return (
    <View
      style={[
        styles.editCard,
        {
          borderColor: primaryColor,
          backgroundColor: isDark ? hexToRgba(primaryColor, 0.08) : hexToRgba(primaryColor, 0.04),
        },
      ]}
    >
      <ThemedText style={[styles.editHeading, { color: primaryColor }]}>
        {t("admin.settings.resourceRow.editingHeading", {
          name:
            resource.name ??
            t("admin.settings.resourceRow.resourceFallbackName", { id: resource.id }),
        })}
      </ThemedText>
      <View style={styles.editFields}>
        <View style={styles.editNameField}>
          <ThemedText style={[styles.editFieldLabel, { color: mutedColor }]}>
            {t("admin.settings.resourceRow.nameFieldLabel")}
          </ThemedText>
          <Input
            value={draftName}
            onChangeText={setDraftName}
            placeholder={t("admin.settings.resourceRow.namePlaceholder")}
          />
        </View>
        <View style={styles.editPartySizeField}>
          <ThemedText style={[styles.editFieldLabel, { color: mutedColor }]}>
            {t("admin.settings.resourceRow.capacityFieldLabel")}
          </ThemedText>
          <Select
            selectedValue={draftCapacity}
            onSelect={(v) => setDraftCapacity(v as number)}
            options={buildPartySizeOptions()}
            placeholder={t("admin.settings.resourceRow.capacityPlaceholder")}
          />
        </View>
      </View>
      <Pressable
        testID={`resource-walk-in-toggle-${resource.id}`}
        role="checkbox"
        aria-checked={draftWalkInOnly}
        accessibilityState={{ checked: draftWalkInOnly }}
        onPress={() => setDraftWalkInOnly((v) => !v)}
        style={styles.walkInToggle}
      >
        <Icon
          name={draftWalkInOnly ? "checkbox" : "square-outline"}
          size="md"
          color={primaryColor}
        />
        <View style={settingsStyles.tileCopy}>
          <ThemedText style={styles.walkInToggleLabel}>
            {t("admin.settings.resourceRow.walkInOnlyLabel")}
          </ThemedText>
          <ThemedText style={[styles.capacityText, { color: mutedColor }]}>
            {t("admin.settings.resourceRow.walkInOnlyHint")}
          </ThemedText>
        </View>
      </Pressable>
      <ButtonRow style={styles.editActions}>
        <Button
          variant="secondary"
          tone="neutral"
          size="md"
          onPress={() => setEditing(false)}
          accessibilityLabel={t("admin.settings.resourceRow.cancelEditLabel", {
            name: resourceName,
          })}
        >
          {t("common.actions.cancel")}
        </Button>
        <Button
          size="md"
          disabled={saving}
          loading={saving}
          accessibilityLabel={t("admin.settings.resourceRow.saveLabel", { name: resourceName })}
          onPress={async () => {
            setSaving(true);
            const result = await updateResource(venueId, sectionId, resource.id, {
              name: draftName.trim() || undefined,
              capacity: draftCapacity,
              walkInOnly: draftWalkInOnly,
            });
            setSaving(false);
            if (result) {
              onUpdated(result);
              setEditing(false);
            }
          }}
        >
          {saving ? t("common.status.saving") : t("admin.settings.resourceRow.save")}
        </Button>
      </ButtonRow>
    </View>
  );
}
