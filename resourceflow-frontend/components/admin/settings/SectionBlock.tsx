import { useMemo, useState } from "react";
import { View, Pressable } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import {
  SectionDto,
  ResourceDto,
  ResourceGroupDto,
  updateSection,
  deleteSection,
  addResource,
  fetchSectionDeleteImpact,
  createResourceGroup,
  updateResourceGroup,
  deleteResourceGroup,
} from "@/api/venues";
import { ResourceRow, ResourceRowGroupContext } from "./ResourceRow";
import { AddRow } from "./AddRow";
import { RowIconButton } from "./RowIconButton";
import Button from "@/components/common/Button";
import { ButtonRow } from "@/components/common/ButtonRow";
import { RowTextButton } from "@/components/common/RowTextButton";
import { theme } from "@/theme/theme";
import { useAppTheme } from "@/hooks/use-app-theme";
import { hexToRgba } from "@/utils/colors";
import { buildPartySizeOptions } from "@/utils/partySizeOptions";
import { styles as settingsStyles } from "./settings.styles";
import { styles } from "./SectionBlock.styles";
import Input from "@/components/common/Input";
import Select from "@/components/common/Select";
import { Icon } from "@/components/common/Icon";

export function SectionBlock({
  section,
  venueId,
  isDark,
  borderColor,
  mutedColor,
  groups,
  onSectionRenamed,
  onSectionDeleted,
  onResourceAdded,
  onResourceUpdated,
  onResourceDeleted,
  onGroupsChanged,
  isFirst,
  isLast,
  moveDisabled,
  onMoveUp,
  onMoveDown,
}: {
  section: SectionDto;
  venueId: number;
  isDark: boolean;
  borderColor: string;
  mutedColor: string;
  /** Venue-level combinable groups; SectionBlock renders the ones touching this section. */
  groups: ResourceGroupDto[];
  onSectionRenamed: (name: string) => void;
  onSectionDeleted: () => void;
  onResourceAdded: (t: ResourceDto) => void;
  onResourceUpdated: (t: ResourceDto) => void;
  onResourceDeleted: (id: number) => void;
  /** Replace the venue's full group list after a create/update/delete/dissolve. */
  onGroupsChanged: (groups: ResourceGroupDto[]) => void;
  isFirst: boolean;
  isLast: boolean;
  moveDisabled?: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
}) {
  const { t } = useTranslation();
  const { primaryColor } = useAppTheme();
  const surface2 = isDark ? "#252729" : "#f9fafb";
  const cardBg = isDark ? "#1e2022" : "#ffffff";
  const totalCapacity = section.resources.reduce((s, t) => s + t.capacity, 0);

  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(section.name);
  const [saving, setSaving] = useState(false);
  // Two-step delete friction — same inline pattern as ResourceRow. `Delete…` reveals
  // an inline confirmation naming the consequence (incl. the count of future bookings that would lose
  // their section/resource reference); a second explicit tap destroys. `impact` is best-effort.
  const [deleteStep, setDeleteStep] = useState<"idle" | "confirm">("idle");
  const [impact, setImpact] = useState<number | null>(null);
  const [impactLoading, setImpactLoading] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // ── Combinable groups ─────────────────────────────────────────────────────
  // Selection mode: the admin taps Link on a standalone resource, then picks other standalone resources
  // to combine into a new group. `linkingFromId` is the source resource; `selectedIds` the checked set.
  const [linkingFromId, setLinkingFromId] = useState<number | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [combining, setCombining] = useState(false);
  // Inline edit for a group's combined capacity.
  const [editingGroupId, setEditingGroupId] = useState<number | null>(null);
  const [draftCombinedCapacity, setDraftCombinedCapacity] = useState("");

  const sectionResourceIds = useMemo(
    () => new Set(section.resources.map((t) => t.id)),
    [section.resources]
  );

  // Groups that have at least one member in this section. A group is rendered inside every section
  // it touches (selection is within one section, so in practice all members are same-section).
  const sectionGroups = useMemo(
    () => groups.filter((g) => g.members.some((m) => sectionResourceIds.has(m.id))),
    [groups, sectionResourceIds]
  );

  // resourceId → its group, for quick chip rendering on rows.
  const resourceIdToGroup = useMemo(() => {
    const map = new Map<number, ResourceGroupDto>();
    for (const g of groups) for (const m of g.members) map.set(m.id, g);
    return map;
  }, [groups]);

  /**
   * `g.name` is the admin-authored group label and `g.members`/`combinedCapacity` are wire data
   * (persisted `ResourceGroup` fields) — only the surrounding chrome ("(N capacity)",
   * "Resources … (N combined)") localizes.
   * @see [SectionBlock.test.tsx](../../../tests/components/admin/settings/SectionBlock.test.tsx)
   * — pins the section header's combinable-group count across locales.
   */
  function groupLabel(g: ResourceGroupDto): string {
    return g.name
      ? t("admin.settings.sectionBlock.namedGroupLabel", {
          name: g.name,
          capacity: g.combinedCapacity,
        })
      : t("admin.settings.sectionBlock.unnamedGroupLabel", {
          members: g.members.map((m) => m.name ?? m.id).join(" + "),
          combined: g.combinedCapacity,
        });
  }

  function groupContextFor(resourceId: number): ResourceRowGroupContext | undefined {
    const g = resourceIdToGroup.get(resourceId);
    return g ? { id: g.id, label: groupLabel(g), combinedCapacity: g.combinedCapacity } : undefined;
  }

  /**
   * Combined-capacity window the server accepts for a member set: more than the largest member
   * (else combining gains nothing) and no more than their sum (combining resources can lose a place
   * where they meet, never gain one). Mirrors ValidateCombinedCapacity.
   */
  function combinedCapacityRange(memberCapacity: number[]) {
    return { min: Math.max(...memberCapacity) + 1, max: memberCapacity.reduce((a, b) => a + b, 0) };
  }

  const startLink = (resourceId: number) => {
    setLinkingFromId(resourceId);
    setSelectedIds(new Set([resourceId]));
  };

  const cancelLink = () => {
    setLinkingFromId(null);
    setSelectedIds(new Set());
  };

  const toggleSelect = (resourceId: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(resourceId)) next.delete(resourceId);
      else next.add(resourceId);
      return next;
    });
  };

  const confirmCombine = async () => {
    const memberIds = Array.from(selectedIds);
    if (memberIds.length < 2) return;
    setCombining(true);
    const sumCapacity = memberIds.reduce(
      (sum, id) => sum + (section.resources.find((t) => t.id === id)?.capacity ?? 0),
      0
    );
    const created = await createResourceGroup(venueId, {
      members: memberIds,
      combinedCapacity: sumCapacity,
    });
    setCombining(false);
    if (created) {
      onGroupsChanged([...groups, created]);
      cancelLink();
    }
  };

  const handleUnlink = async (resourceId: number) => {
    const g = resourceIdToGroup.get(resourceId);
    if (!g) return;
    const remaining = g.members.filter((m) => m.id !== resourceId).map((m) => m.id);
    if (remaining.length < 2) {
      // Dissolve — a one-resource group is meaningless. Delete the group entirely.
      const ok = await deleteResourceGroup(venueId, g.id);
      if (ok) onGroupsChanged(groups.filter((x) => x.id !== g.id));
    } else {
      // Capacity comes off the group's own members, not this section's resources — a group rendered
      // here may still reach into another section, and a missing lookup would silently score it at
      // capacity 0.
      const { min, max } = combinedCapacityRange(
        g.members.filter((m) => remaining.includes(m.id)).map((m) => m.capacity)
      );
      const updated = await updateResourceGroup(venueId, g.id, {
        name: g.name ?? null,
        members: remaining,
        // The removed resource's capacity is gone, so the old combined figure may now exceed what the
        // remaining resources can hold — clamp it back into the accepted window.
        combinedCapacity: Math.min(Math.max(g.combinedCapacity, min), max),
      });
      if (updated) onGroupsChanged(groups.map((x) => (x.id === g.id ? updated : x)));
    }
  };

  const saveCombinedCapacity = async (groupId: number) => {
    const capacity = parseInt(draftCombinedCapacity, 10);
    const g = groups.find((x) => x.id === groupId);
    if (isNaN(capacity) || capacity < 1 || !g) return;
    const updated = await updateResourceGroup(venueId, groupId, {
      name: g.name ?? null,
      members: g.members.map((m) => m.id),
      combinedCapacity: capacity,
    });
    if (updated) onGroupsChanged(groups.map((x) => (x.id === groupId ? updated : x)));
    setEditingGroupId(null);
  };

  const startSectionDelete = async () => {
    setDeleteStep("confirm");
    setImpact(null);
    setImpactLoading(true);
    const result = await fetchSectionDeleteImpact(venueId, section.id);
    setImpact(result?.bookings ?? null);
    setImpactLoading(false);
  };

  const cancelSectionDelete = () => {
    setDeleteStep("idle");
    setImpact(null);
    setImpactLoading(false);
  };

  const confirmSectionDelete = async () => {
    setDeleting(true);
    const success = await deleteSection(venueId, section.id);
    setDeleting(false);
    if (success) onSectionDeleted();
  };

  const inSelectionMode = linkingFromId !== null;

  return (
    <View
      style={[settingsStyles.sectionBlock, styles.card, { borderColor, backgroundColor: surface2 }]}
    >
      {/* Section header — surface background with border-bottom. Name + count subtitle on the
          left, a tidy cluster of icon actions on the right (consistent with the resource tiles and
          the rest of the settings cards). Editing state swaps to a name Input + text Save/Cancel. */}
      <View style={[styles.header, { backgroundColor: cardBg, borderBottomColor: borderColor }]}>
        {/* Left: name / edit input + count subtitle */}
        <View style={styles.headerCopy}>
          {editing ? (
            <Input
              value={draft}
              onChangeText={setDraft}
              placeholder={t("admin.settings.sectionBlock.namePlaceholder")}
              autoFocus
            />
          ) : (
            <>
              <ThemedText style={settingsStyles.editableValue}>{section.name}</ThemedText>
              <ThemedText style={[styles.headerSub, { color: mutedColor }]}>
                {sectionGroups.length > 0
                  ? t("admin.settings.sectionBlock.headerSubtitleWithGroups", {
                      resources: t("admin.settings.locationCard.resourcesCount", {
                        count: section.resources.length,
                      }),
                      capacity: t("admin.settings.resourceRow.capacityCount", {
                        count: totalCapacity,
                      }),
                      groups: t("admin.settings.sectionBlock.groupsCount", {
                        count: sectionGroups.length,
                      }),
                    })
                  : t("admin.settings.sectionBlock.headerSubtitle", {
                      resources: t("admin.settings.locationCard.resourcesCount", {
                        count: section.resources.length,
                      }),
                      capacity: t("admin.settings.resourceRow.capacityCount", {
                        count: totalCapacity,
                      }),
                    })}
              </ThemedText>
            </>
          )}
        </View>

        {/* Right: edit/save/delete actions — gap 8 to match the resource-row trailing cluster. */}
        <View style={styles.headerActions}>
          {editing ? (
            <>
              <Button
                variant="secondary"
                tone="neutral"
                size="md"
                onPress={() => setEditing(false)}
                accessibilityLabel={t("admin.settings.sectionBlock.cancelRenameLabel", {
                  name: section.name,
                })}
              >
                {t("common.actions.cancel")}
              </Button>
              <Button
                size="md"
                disabled={saving}
                loading={saving}
                accessibilityLabel={t("admin.settings.sectionBlock.saveNameLabel", {
                  name: section.name,
                })}
                onPress={async () => {
                  if (!draft.trim()) return;
                  setSaving(true);
                  const result = await updateSection(venueId, section.id, draft.trim());
                  if (result) onSectionRenamed(result.name);
                  setSaving(false);
                  setEditing(false);
                }}
              >
                {t("admin.settings.sectionBlock.save")}
              </Button>
            </>
          ) : (
            <>
              <RowIconButton
                testID="section-move-up-btn"
                name="arrow-up-outline"
                color={isFirst ? mutedColor : primaryColor}
                disabled={isFirst || moveDisabled}
                onPress={() => {
                  if (!isFirst) onMoveUp();
                }}
                accessibilityLabel={t("admin.settings.sectionBlock.moveUpLabel", {
                  name: section.name,
                })}
                accessibilityHint={t("admin.settings.sectionBlock.moveUpHint")}
              />
              <RowIconButton
                testID="section-move-down-btn"
                name="arrow-down-outline"
                color={isLast ? mutedColor : primaryColor}
                disabled={isLast || moveDisabled}
                onPress={() => {
                  if (!isLast) onMoveDown();
                }}
                accessibilityLabel={t("admin.settings.sectionBlock.moveDownLabel", {
                  name: section.name,
                })}
                accessibilityHint={t("admin.settings.sectionBlock.moveDownHint")}
              />
              <RowTextButton
                testID="section-edit-btn"
                label={t("admin.settings.sectionBlock.edit")}
                icon="pencil-outline"
                color={primaryColor}
                onPress={() => {
                  setDraft(section.name);
                  setEditing(true);
                }}
                accessibilityLabel={t("admin.settings.sectionBlock.renameLabel", {
                  name: section.name,
                })}
              />
              <RowTextButton
                testID="section-delete-btn"
                label={t("admin.settings.sectionBlock.delete")}
                icon="trash-outline"
                color={theme.colors.error}
                disabled={deleteStep === "confirm"}
                onPress={startSectionDelete}
                accessibilityLabel={t("admin.settings.sectionBlock.deleteSectionLabel", {
                  name: section.name,
                })}
              />
            </>
          )}
        </View>
      </View>

      {/* Inline two-step delete confirmation — sits between the header and the resource list so
          the consequence is visible in context. Cancel returns to the header without destroying. */}
      {deleteStep === "confirm" && (
        <View
          style={[
            styles.confirmBanner,
            {
              borderBottomColor: borderColor,
              backgroundColor: isDark
                ? hexToRgba(theme.colors.error, 0.08)
                : hexToRgba(theme.colors.error, 0.04),
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
                {t("admin.settings.sectionBlock.deleteConfirmTitle", { name: section.name })}
              </ThemedText>{" "}
              {impactLoading
                ? ""
                : impact && impact > 0
                  ? t("admin.settings.sectionBlock.impactCount", { count: impact })
                  : t("admin.settings.sectionBlock.impactUnknown")}{" "}
              {t("admin.settings.resourceRow.cannotBeUndone")}
            </ThemedText>
          </View>
          <ButtonRow>
            <Button
              testID="section-delete-cancel-btn"
              variant="secondary"
              tone="neutral"
              size="md"
              onPress={cancelSectionDelete}
              disabled={deleting}
            >
              {t("common.actions.cancel")}
            </Button>
            <Button
              testID="section-delete-confirm-btn"
              tone="danger"
              size="md"
              disabled={deleting}
              loading={deleting}
              onPress={confirmSectionDelete}
            >
              {deleting
                ? t("admin.settings.resourceRow.deleting")
                : t("admin.settings.resourceRow.yesDelete")}
            </Button>
          </ButtonRow>
        </View>
      )}

      {/* Combine-selection header — shown while the admin is building a new group. Lists the
          source resource and offers Cancel / Combine. The resource rows below become
          checkboxes. */}
      {inSelectionMode && (
        <View
          style={[
            styles.selectionBar,
            {
              borderBottomColor: borderColor,
              backgroundColor: isDark
                ? hexToRgba(primaryColor, 0.1)
                : hexToRgba(primaryColor, 0.06),
            },
          ]}
        >
          <ThemedText style={[styles.selectionLabel, { color: primaryColor }]}>
            {t("admin.settings.sectionBlock.selectToCombine", {
              source:
                section.resources.find((t) => t.id === linkingFromId)?.name ??
                t("admin.settings.resourceRow.resourceFallbackShort", { id: linkingFromId }),
              count: selectedIds.size,
            })}
          </ThemedText>
          <Button
            variant="secondary"
            tone="neutral"
            size="md"
            onPress={cancelLink}
            disabled={combining}
            accessibilityLabel={t("admin.settings.sectionBlock.cancelCombineLabel")}
          >
            {t("common.actions.cancel")}
          </Button>
          <Button
            testID="section-combine-btn"
            size="md"
            disabled={selectedIds.size < 2 || combining}
            loading={combining}
            onPress={confirmCombine}
          >
            {t("admin.settings.sectionBlock.combine")}
          </Button>
        </View>
      )}

      {/* Group combined-capacity edit affordance */}
      {editingGroupId !== null &&
        (() => {
          const g = groups.find((x) => x.id === editingGroupId);
          if (!g) return null;
          return (
            <View
              style={[
                styles.groupEditBar,
                {
                  borderBottomColor: borderColor,
                  backgroundColor: isDark
                    ? hexToRgba(primaryColor, 0.1)
                    : hexToRgba(primaryColor, 0.06),
                },
              ]}
            >
              <ThemedText style={[styles.groupEditLabel, { color: primaryColor }]}>
                {t("admin.settings.sectionBlock.editCombinedCapacityHeading", {
                  label: groupLabel(g),
                })}
              </ThemedText>
              <View style={styles.groupEditRow}>
                <View style={styles.groupEditSelect}>
                  <Select
                    selectedValue={parseInt(draftCombinedCapacity, 10) || undefined}
                    onSelect={(v) => setDraftCombinedCapacity(String(v))}
                    options={(() => {
                      const { min, max } = combinedCapacityRange(g.members.map((m) => m.capacity));
                      return buildPartySizeOptions(min, max);
                    })()}
                    placeholder={String(g.combinedCapacity)}
                  />
                </View>
                <Button
                  variant="secondary"
                  tone="neutral"
                  size="md"
                  onPress={() => setEditingGroupId(null)}
                  accessibilityLabel={t("admin.settings.sectionBlock.cancelEditCombinedLabel")}
                >
                  {t("common.actions.cancel")}
                </Button>
                <Button
                  testID="group-save-combined-btn"
                  size="md"
                  onPress={() => saveCombinedCapacity(g.id)}
                >
                  {t("admin.settings.sectionBlock.save")}
                </Button>
              </View>
            </View>
          );
        })()}

      {/* Resource list — tiles with breathing room (rows are now rounded surface tiles, not
          divider-separated). */}
      <View style={styles.resourceList}>
        {section.resources.map((t) => (
          <ResourceRow
            key={t.id}
            resource={t}
            venueId={venueId}
            sectionId={section.id}
            isDark={isDark}
            borderColor={borderColor}
            onUpdated={onResourceUpdated}
            onDeleted={() => onResourceDeleted(t.id)}
            group={groupContextFor(t.id)}
            onLink={() => startLink(t.id)}
            onUnlink={() => handleUnlink(t.id)}
            selectionMode={inSelectionMode}
            selected={selectedIds.has(t.id)}
            onToggleSelect={() => toggleSelect(t.id)}
            disabledInSelection={!!resourceIdToGroup.get(t.id)}
          />
        ))}
        {section.resources.length === 0 && (
          <View style={[settingsStyles.emptyState, { borderColor }]}>
            <Icon name="grid-outline" size={22} color={mutedColor} />
            <ThemedText style={[settingsStyles.emptyStateText, { color: mutedColor }]}>
              {t("admin.settings.sectionBlock.noResourcesYet")}
            </ThemedText>
          </View>
        )}
      </View>

      {/* Per-group combined-capacity edit trigger — a compact bordered chip under each group. */}
      {sectionGroups.map((g) => (
        <Pressable
          key={`group-edit-${g.id}`}
          testID={`group-edit-btn-${g.id}`}
          accessibilityRole="button"
          accessibilityLabel={t("admin.settings.sectionBlock.editGroupCapacityLabel", {
            label: groupLabel(g),
          })}
          style={[
            styles.groupEditTrigger,
            {
              borderColor: hexToRgba(primaryColor, 0.4),
              backgroundColor: hexToRgba(primaryColor, 0.06),
            },
          ]}
          onPress={() => {
            setEditingGroupId(g.id);
            setDraftCombinedCapacity(String(g.combinedCapacity));
          }}
        >
          <Icon name="create-outline" size={13} color={primaryColor} />
          <ThemedText style={[styles.groupEditTriggerText, { color: primaryColor }]}>
            {t("admin.settings.sectionBlock.editGroupCapacityTrigger", { label: groupLabel(g) })}
          </ThemedText>
        </Pressable>
      ))}

      {/* Add resource */}
      <View style={styles.addResourceRow}>
        <AddRow
          label={t("admin.settings.sectionBlock.addResource")}
          placeholder={t("admin.settings.sectionBlock.addResourcePlaceholder")}
          extraPlaceholder={t("admin.settings.sectionBlock.capacityPlaceholder")}
          extraOptions={buildPartySizeOptions()}
          onAdd={async (name, extra) => {
            const capacity = parseInt(extra ?? "2", 10);
            const result = await addResource(venueId, section.id, {
              name,
              capacity: isNaN(capacity) ? 2 : capacity,
            });
            if (result) onResourceAdded(result);
          }}
        />
      </View>
    </View>
  );
}
