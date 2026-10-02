import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import type { VenueDto, ResourceDto } from "@/api/venues";
import type { TimeSlotDto } from "@/api/availability";
import { groupDropdownLabel, groupedResourceIds } from "@/utils/resourceGroups";
import { capacityAtLeast } from "@/utils/placement";
import { onlineGroups, onlineSections } from "@/utils/walkIn";

export const ANY_SECTION_ID = 0;

/**
 * Groups and resources share one dropdown, so group ids ride in as negatives — resource ids are
 * always positive and section 0 is "Any", leaving the negative range free.
 */
export const groupSelectValue = (groupId: number) => -groupId;
export const isGroupSelectValue = (value: number) => value < 0;
export const groupIdFromSelectValue = (value: number) => -value;

export interface UseBookingPlacementArgs {
  venue: VenueDto;
  partySize: number;
  /** The slot the guest has picked, if availability has loaded for it. */
  currentSlot: TimeSlotDto | undefined;
}

/**
 * Section / resource / combinable-group selection for the booking form.
 *
 * "Any section" (id 0) is the default: the form hides the resource dropdown and the server picks.
 * Otherwise the hook keeps a concrete resource selected, re-picking whenever availability, party
 * size or section changes so the form never sits on a resource the API would reject.
 *
 * Selection is all it owns. A live hold on the previous pick is invalidated by the new pick
 * flowing into `useResourceHold`, which releases or replaces it — never by this hook reaching back.
 */
export function useBookingPlacement({ venue, partySize, currentSlot }: UseBookingPlacementArgs) {
  const { t } = useTranslation();
  const [sectionId, setSectionId] = useState<number>(ANY_SECTION_ID);
  const [resourceId, setResourceId] = useState<number | undefined>();
  /** Mutually exclusive with `resourceId` in the submit payload: a booking reserves one or the other. */
  const [resourceGroupId, setResourceGroupId] = useState<number | undefined>();

  const sections = onlineSections(venue);
  const allResources = sections.flatMap((s) => s.resources);
  const allGroups = onlineGroups(venue);
  const groupedResourceIdSet = groupedResourceIds(allGroups);

  // Parties above the best single resource *or* the best combinable group can't be fit even with
  // resources combined, and have to contact the venue directly.
  const maxSingleResourceCapacity =
    allResources.length > 0 ? Math.max(...allResources.map((t) => t.capacity)) : 0;
  const maxGroupCapacity =
    allGroups.length > 0 ? Math.max(...allGroups.map((g) => g.combinedCapacity)) : 0;
  const maxResourceCapacity = Math.max(maxSingleResourceCapacity, maxGroupCapacity);
  const partyTooLarge = maxResourceCapacity > 0 && partySize > maxResourceCapacity;

  const sectionOptions = [
    { label: t("booking.placement.anySectionLabel"), value: ANY_SECTION_ID },
    ...sections.map((s) => ({ label: s.name, value: s.id })),
  ];
  const isAutoAssign = sectionId === ANY_SECTION_ID;
  const resourcesInSection = sections.find((s) => s.id === sectionId)?.resources ?? allResources;
  // A group belongs to the picked section only when *every* member sits in it: booking a group
  // books all its resources, so one member elsewhere would split the party across sections. ResourceDto
  // carries no sectionId, hence resolving membership through the section's own resource ids.
  const sectionResourceIds = new Set(resourcesInSection.map((t) => t.id));
  const groupsInSection = isAutoAssign
    ? allGroups
    : allGroups.filter(
        (g) => g.members.length > 0 && g.members.every((m) => sectionResourceIds.has(m.id))
      );

  const availableResourceIds = currentSlot?.availableResourceIds ?? [];
  const availableGroupIds = currentSlot?.availableGroupIds ?? [];

  function bestResourceFor(
    size: number,
    availableIds?: number[],
    candidateResources?: ResourceDto[]
  ) {
    const pool = candidateResources ?? allResources;
    let eligible = capacityAtLeast(pool, size, venue.maxSpareCapacity, (t) => t.capacity);
    if (availableIds && availableIds.length > 0) {
      eligible = eligible.filter((t) => availableIds.includes(t.id));
    }
    // Smallest fitting resource, and a combinable resource loses to an ungrouped one of the same size —
    // the deprioritization the server applies when auto-assigning, so the suggested default leaves
    // mergeable resources free for the parties that need them combined.
    eligible.sort((a, b) => {
      if (a.capacity !== b.capacity) return a.capacity - b.capacity;
      return Number(groupedResourceIdSet.has(a.id)) - Number(groupedResourceIdSet.has(b.id));
    });
    return eligible[0]?.id ?? pool[0]?.id;
  }

  useEffect(() => {
    if (isAutoAssign) {
      if (resourceId !== undefined) setResourceId(undefined);
      if (resourceGroupId !== undefined) setResourceGroupId(undefined);
      return;
    }
    const candidates = sections.find((s) => s.id === sectionId)?.resources ?? allResources;
    if (availableResourceIds.length > 0) {
      if (!resourceId || !availableResourceIds.includes(resourceId)) {
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setResourceId(bestResourceFor(partySize, availableResourceIds, candidates));
      }
    } else {
      setResourceId(bestResourceFor(partySize, undefined, candidates));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [availableResourceIds, partySize, isAutoAssign]);

  useEffect(() => {
    if (isAutoAssign) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setResourceId(undefined);
      setResourceGroupId(undefined);
      return;
    }
    const candidates = sections.find((s) => s.id === sectionId)?.resources ?? allResources;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setResourceId(
      bestResourceFor(
        partySize,
        availableResourceIds.length > 0 ? availableResourceIds : undefined,
        candidates
      )
    );
    setResourceGroupId(undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sectionId]);

  const eligibleResources = capacityAtLeast(
    resourcesInSection,
    partySize,
    venue.maxSpareCapacity,
    (t) => t.capacity
  )
    .filter((t) => (currentSlot ? availableResourceIds.includes(t.id) : true))
    .sort((a, b) => a.capacity - b.capacity);

  const eligibleGroups = capacityAtLeast(
    groupsInSection,
    partySize,
    venue.maxSpareCapacity,
    (g) => g.combinedCapacity
  )
    .filter((g) => (currentSlot ? availableGroupIds.includes(g.id) : true))
    .sort((a, b) => a.combinedCapacity - b.combinedCapacity);

  const resourceOptions = [
    ...eligibleResources.map((resource) => ({
      label: `${resource.name ?? t("booking.placement.resourceFallbackName", { id: resource.id })} ${t("booking.placement.capacitySuffix", { count: resource.capacity })}`,
      value: resource.id,
    })),
    ...eligibleGroups.map((g) => ({ label: groupDropdownLabel(g), value: groupSelectValue(g.id) })),
  ];

  /** Applies a pick from the combined resource/group dropdown, keeping the two mutually exclusive. */
  const selectPlacementUnit = (value: number) => {
    if (isGroupSelectValue(value)) {
      setResourceGroupId(groupIdFromSelectValue(value));
      setResourceId(undefined);
    } else {
      setResourceGroupId(undefined);
      setResourceId(value);
    }
  };

  return {
    allResources,
    allGroups,
    sectionId,
    setSectionId,
    resourceId,
    resourceGroupId,
    selectPlacementUnit,
    isAutoAssign,
    sectionOptions,
    resourceOptions,
    maxResourceCapacity,
    partyTooLarge,
  };
}
