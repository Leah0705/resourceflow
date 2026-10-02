/**
 * Naming + membership helpers for combinable resource groups, shared by the guest
 * booking dropdown and the resource minimap so the two can't drift apart on what a group is
 * called. A group is named by the admin or, when unnamed, by its member resources.
 */

export interface ResourceGroupLike {
  name?: string | null;
  combinedCapacity: number;
  members: { id: number; name?: string | null }[];
}

/** Member resource names joined for display, e.g. "R1 + R2". Falls back to the id for unnamed resources. */
function memberNames(group: ResourceGroupLike): string {
  return group.members.map((m) => m.name ?? m.id).join(" + ");
}

/**
 * The group's display name on its own, with no capacity suffix — for surfaces that show the
 * capacity separately (the resource minimap renders it on its own line).
 */
export function groupDisplayName(group: ResourceGroupLike): string {
  return group.name ?? `Resources ${memberNames(group)}`;
}

/**
 * Single-line label for the booking form's resource dropdown, where the capacity has to ride along
 * in the same string. Named groups read "Quiet pods (5 places)"; unnamed ones spell out the
 * members so the guest knows which resources get combined.
 */
export function groupDropdownLabel(group: ResourceGroupLike): string {
  return group.name
    ? `${group.name} (${group.combinedCapacity} places)`
    : `Resources ${memberNames(group)} (${group.combinedCapacity} places combined)`;
}

/**
 * Ids of every resource that belongs to some group. Used to deprioritize combinable resources in
 * auto-assign ordering and to mark them in the resource minimap — a member resource stays
 * individually bookable, so this is a display/ordering hint, never a filter.
 */
export function groupedResourceIds(groups: ResourceGroupLike[]): Set<number> {
  return new Set(groups.flatMap((g) => g.members.map((m) => m.id)));
}
