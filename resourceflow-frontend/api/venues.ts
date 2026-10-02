import { get, post, put, del, buildUrl } from "./client";

export interface ResourceDto {
  id: number;
  name?: string | null;
  capacity: number;
  /** Kept for walk-ins: never offered online, still assignable by staff. */
  walkInOnly?: boolean;
}

export interface SectionDto {
  id: number;
  name: string;
  sortOrder?: number;
  resources: ResourceDto[];
}

/**
 * A combinable-resource group: physical resources an admin flagged as combinable, bookable
 * as one unit for larger parties. `combinedCapacity` is the stored capacity; `members` are the
 * physical resources combined.
 */
export interface ResourceGroupDto {
  id: number;
  name?: string | null;
  combinedCapacity: number;
  members: ResourceDto[];
}

/**
 * Best-effort preview of what a resource/section delete would orphan. `bookings` is the count of
 * non-cancelled *future* bookings that would lose their resource/section reference (the FK-null the
 * delete already performs). Null/undefined when the impact read failed or is unavailable — callers
 * fall back to generic copy rather than blocking the delete.
 */
export interface DeleteImpactDto {
  bookings: number;
}

/** Why an upcoming booking no longer fits its location's current schedule. */
/**
 * Every reason means the guest arrives to a closed venue. A walk-in-only location or day
 * is deliberately not one: it stops new online bookings, it does not close the location, so a
 * slot already on the books stands.
 */
export type ScheduleConflictReason = "closedDay" | "outsideHours";

/**
 * An upcoming booking the location's current schedule would no longer accept. Editing hours,
 * open days or the walk-in policy leaves the bookings already taken exactly where they are, so
 * this read is the only thing that surfaces the guests the edit stranded.
 */
export interface ScheduleConflictDto {
  bookingId: number;
  bookingRef: string;
  customerName?: string | null;
  /** Slot start, UTC. */
  date: string;
  partySize: number;
  reason: ScheduleConflictReason;
}

/** A party of `minPartySize` or more holds its resource for `minutes`, up to the next rule. */
export interface DurationRuleDto {
  minPartySize: number;
  minutes: number;
}

export interface DayHoursDto {
  /** ISO 8601 day number: 1=Monday … 7=Sunday. */
  day: number;
  open: string;
  close: string;
}

/**
 * Shape of the booking reference handed to customers. Mirrors the backend
 * `BookingRefFormat` enum, which serialises as its member name.
 */
export type BookingRefFormat = "AlphaNumeric" | "Numeric";

export interface VenueDto {
  id: number;
  name: string;
  address?: string | null;
  openTime: string;
  closeTime: string;
  /** Resolved hours for every day of the week (7 entries) when provided by the API. */
  openHours?: DayHoursDto[];
  openDays: string;
  timezone: string;
  tags?: string[];
  imageUrl?: string | null;
  /** Optional blurb shown on the location detail page (supports [label](url) links). */
  description?: string | null;
  /** Optional link to this location's guide (PDF, page, etc.). */
  guideUrl?: string | null;
  /** Optional contact phone for this location; falls back to the brand phone when absent. */
  phoneNumber?: string | null;
  /** Optional contact email for this location; falls back to the brand email when absent. */
  emailAddress?: string | null;
  isArchived?: boolean;
  /** When true the whole location is walk-in only — the booking flow is disabled. */
  walkInOnly?: boolean;
  /** Comma-separated ISO days (1=Monday … 7=Sunday) that are walk-in only ("" when none). */
  walkInDays?: string;
  defaultBookingDurationMinutes?: number;
  /** Slot lengths by party size, smallest party first. Empty when every party gets the default. */
  durationRules?: DurationRuleDto[];
  /** Step (minutes) between selectable booking start times (15/30/60). */
  bookingSlotIntervalMinutes?: number;
  /** Max allowed spare capacity over party size, or null for unrestricted (off). */
  maxSpareCapacity?: number | null;
  /** Most guests whose online bookings may start in one slot, or null for no cap. */
  maxGuestsPerSlot?: number | null;
  /** Format of references minted for new bookings; existing bookings keep theirs. */
  bookingRefFormat?: BookingRefFormat;
  sections: SectionDto[];
  /** Combinable-resource groups defined for this venue. Empty/undefined when none. */
  groups?: ResourceGroupDto[];
}

export async function createVenue(name: string): Promise<VenueDto | null> {
  try {
    const res = await post("/venues", {
      name,
      openTime: "09:00",
      closeTime: "22:00",
      openDays: "1,2,3,4,5,6,7",
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    });
    if (!res.ok) throw new Error("Failed to create location");
    return await res.json();
  } catch (err) {
    console.error("createVenue error:", err);
    return null;
  }
}

export async function fetchVenues(): Promise<VenueDto[]> {
  try {
    const res = await get("/venues");
    if (!res.ok) throw new Error("Failed to fetch locations");
    return await res.json();
  } catch (err) {
    console.error("fetchVenues error:", err);
    return [];
  }
}

export async function fetchVenueById(id: number): Promise<VenueDto | null> {
  try {
    const res = await get(`/venues/${id}`);
    if (!res.ok) throw new Error("Failed to fetch location");
    return await res.json();
  } catch (err) {
    console.error("fetchVenueById error:", err);
    return null;
  }
}

export interface HighlightDto {
  id: number;
  title: string;
  body: string;
  iconKey: string;
  sortOrder: number;
  link?: string | null;
}

export async function fetchHighlights(): Promise<HighlightDto[]> {
  try {
    const res = await get("/highlights");
    if (!res.ok) throw new Error("Failed to fetch highlights");
    return await res.json();
  } catch (err) {
    console.error("fetchHighlights error:", err);
    return [];
  }
}

export interface SocialLinkDto {
  id: number;
  label: string;
  url: string;
  iconKey: string;
  sortOrder: number;
}

export async function fetchSocialLinks(): Promise<SocialLinkDto[]> {
  try {
    const res = await get("/social-links");
    if (!res.ok) throw new Error("Failed to fetch social links");
    return await res.json();
  } catch (err) {
    console.error("fetchSocialLinks error:", err);
    return [];
  }
}

export async function updateVenue(
  id: number,
  data: {
    name: string;
    address?: string | null;
    openTime?: string;
    closeTime?: string;
    openHours?: DayHoursDto[];
    openDays?: string;
    timezone?: string;
    tags?: string | null;
    defaultBookingDurationMinutes?: number;
    durationRules?: DurationRuleDto[];
    bookingSlotIntervalMinutes?: number;
    maxSpareCapacity?: number | null;
    maxGuestsPerSlot?: number | null;
    bookingRefFormat?: BookingRefFormat;
    walkInOnly?: boolean;
    walkInDays?: string;
    description?: string | null;
    guideUrl?: string | null;
    phoneNumber?: string | null;
    emailAddress?: string | null;
  }
): Promise<VenueDto | null> {
  try {
    const res = await put(`/venues/${id}`, data);
    if (!res.ok) throw new Error("Failed to update location");
    return await res.json();
  } catch (err) {
    console.error("updateVenue error:", err);
    return null;
  }
}

export async function addSection(venueId: number, name: string): Promise<SectionDto | null> {
  try {
    const res = await post(`/venues/${venueId}/sections`, { name });
    if (!res.ok) throw new Error("Failed to add section");
    return await res.json();
  } catch (err) {
    console.error("addSection error:", err);
    return null;
  }
}

export async function updateSection(
  venueId: number,
  sectionId: number,
  name: string
): Promise<SectionDto | null> {
  try {
    const res = await put(`/venues/${venueId}/sections/${sectionId}`, { name });
    if (!res.ok) throw new Error("Failed to update section");
    return await res.json();
  } catch (err) {
    console.error("updateSection error:", err);
    return null;
  }
}

export async function deleteSection(venueId: number, sectionId: number): Promise<boolean> {
  try {
    const res = await del(`/venues/${venueId}/sections/${sectionId}`);
    return res.ok;
  } catch (err) {
    console.error("deleteSection error:", err);
    return false;
  }
}

export async function addResource(
  venueId: number,
  sectionId: number,
  data: { name?: string; capacity: number; walkInOnly?: boolean }
): Promise<ResourceDto | null> {
  try {
    const res = await post(`/venues/${venueId}/sections/${sectionId}/resources`, data);
    if (!res.ok) throw new Error("Failed to add resource");
    return await res.json();
  } catch (err) {
    console.error("addResource error:", err);
    return null;
  }
}

export async function updateResource(
  venueId: number,
  sectionId: number,
  resourceId: number,
  data: { name?: string; capacity: number; walkInOnly?: boolean }
): Promise<ResourceDto | null> {
  try {
    const res = await put(`/venues/${venueId}/sections/${sectionId}/resources/${resourceId}`, data);
    if (!res.ok) throw new Error("Failed to update resource");
    return await res.json();
  } catch (err) {
    console.error("updateResource error:", err);
    return null;
  }
}

export async function deleteResource(
  venueId: number,
  sectionId: number,
  resourceId: number
): Promise<boolean> {
  try {
    const res = await del(`/venues/${venueId}/sections/${sectionId}/resources/${resourceId}`);
    return res.ok;
  } catch (err) {
    console.error("deleteResource error:", err);
    return false;
  }
}

// ── Combinable resource groups ────────────────────────────────────────────────

export async function createResourceGroup(
  venueId: number,
  data: { name?: string | null; members: number[]; combinedCapacity: number }
): Promise<ResourceGroupDto | null> {
  try {
    const res = await post(`/venues/${venueId}/groups`, data);
    if (!res.ok) throw new Error("Failed to create resource group");
    return await res.json();
  } catch (err) {
    console.error("createResourceGroup error:", err);
    return null;
  }
}

export async function updateResourceGroup(
  venueId: number,
  groupId: number,
  data: { name?: string | null; members: number[]; combinedCapacity: number }
): Promise<ResourceGroupDto | null> {
  try {
    const res = await put(`/venues/${venueId}/groups/${groupId}`, data);
    if (!res.ok) throw new Error("Failed to update resource group");
    return await res.json();
  } catch (err) {
    console.error("updateResourceGroup error:", err);
    return null;
  }
}

export async function deleteResourceGroup(venueId: number, groupId: number): Promise<boolean> {
  try {
    const res = await del(`/venues/${venueId}/groups/${groupId}`);
    return res.ok;
  } catch (err) {
    console.error("deleteResourceGroup error:", err);
    return false;
  }
}

/**
 * Non-cancelled future bookings that would lose their resource reference if this resource were deleted.
 * Returns null on any failure (network, 404, non-OK) so the two-step delete UI can degrade to
 * generic copy instead of blocking the destructive action — the count is best-effort friction, not
 * a gate.
 */
export async function fetchResourceDeleteImpact(
  venueId: number,
  sectionId: number,
  resourceId: number
): Promise<DeleteImpactDto | null> {
  try {
    const res = await get(
      `/venues/${venueId}/sections/${sectionId}/resources/${resourceId}/impact`
    );
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("fetchResourceDeleteImpact error:", err);
    return null;
  }
}

/**
 * Upcoming bookings the location's current opening hours, open days or walk-in policy would no
 * longer accept. Null (not an empty list) when the read failed, so the caller can tell "nothing
 * stranded" from "could not check" and stay silent for the latter rather than claiming all-clear.
 */
export async function fetchScheduleConflicts(
  venueId: number
): Promise<ScheduleConflictDto[] | null> {
  try {
    const res = await get(`/venues/${venueId}/schedule-conflicts`);
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("fetchScheduleConflicts error:", err);
    return null;
  }
}

/**
 * Non-cancelled future bookings that would lose their section reference if this section (and all its
 * resources) were deleted. Same best-effort/null-on-failure contract as {@link fetchResourceDeleteImpact}.
 */
export async function fetchSectionDeleteImpact(
  venueId: number,
  sectionId: number
): Promise<DeleteImpactDto | null> {
  try {
    const res = await get(`/venues/${venueId}/sections/${sectionId}/impact`);
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("fetchSectionDeleteImpact error:", err);
    return null;
  }
}

export async function uploadLocationImage(venueId: number, file: File): Promise<string | null> {
  try {
    const form = new FormData();
    form.append("file", file);
    const res = await fetch(buildUrl(`/media/location/${venueId}`), {
      method: "POST",
      credentials: "include",
      body: form,
    });
    if (!res.ok) return null;
    const data = await res.json();
    return data.url ?? null;
  } catch {
    return null;
  }
}

export async function deleteLocationImage(venueId: number): Promise<boolean> {
  try {
    const res = await fetch(buildUrl(`/media/location/${venueId}`), {
      method: "DELETE",
      credentials: "include",
    });
    return res.ok;
  } catch {
    return false;
  }
}

export async function uploadGuideFile(venueId: number, file: File): Promise<string | null> {
  try {
    const form = new FormData();
    form.append("file", file);
    const res = await fetch(buildUrl(`/media/guide/${venueId}`), {
      method: "POST",
      credentials: "include",
      body: form,
    });
    if (!res.ok) return null;
    const data = await res.json();
    return data.url ?? null;
  } catch {
    return null;
  }
}

export async function deleteGuideFile(venueId: number): Promise<boolean> {
  try {
    const res = await fetch(buildUrl(`/media/guide/${venueId}`), {
      method: "DELETE",
      credentials: "include",
    });
    return res.ok;
  } catch {
    return false;
  }
}
