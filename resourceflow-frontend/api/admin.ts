import { get, post, patch, del, put, buildUrl } from "./client";
import { apiErrorMessage } from "@/api/errors";

export interface AdminOverviewDto {
  totalVenues: number;
  totalBookings: number;
  todayBookings: number;
  totalCapacity: number;
  activeHoldsCount?: number;
  pausedVenuesCount?: number;
  /** Bookings marked as no-shows today, each location's day in its own timezone. */
  todayNoShowsCount?: number;
  scheduleConflictsCount?: number;
  scheduleConflictLocationIds?: number[];
  occupancyData?: number[];
  occupancyDates?: string[];
  occupancyCounts?: number[];
  todayBookingsList?: BookingDetailDto[];
  todayPacing?: LocationPacingDto[];
}

/** Today's guests per slot at a location with a guest cap. Slots with no arrivals are left out. */
export interface LocationPacingDto {
  venueId: number;
  venueName: string;
  maxGuestsPerSlot: number;
  /** `time` is the slot start as local "HH:mm". */
  slots: { time: string; guests: number }[];
}

export interface BookingSummaryDto {
  id: number;
  date: string;
  endTime?: string;
  customerEmail: string;
  customerName?: string;
  partySize: number;
  venueName: string;
  bookingRef: string;
  isCancelled?: boolean;
  status?: BookingStatus;
}

export interface AdminDashboardStats {
  todayCount: number;
  activeHoldsCount: number;
  pausedCount: number;
  noShowCount: number;
  scheduleConflictsCount: number;
  scheduleConflictLocationIds: number[];
  totalGuests: number;
  occupancyData: number[];
  occupancyDates: string[];
  occupancyCounts: number[];
  recentBookings: BookingSummaryDto[];
  pacing: LocationPacingDto[];
}

export async function getAdminDashboardStats(): Promise<AdminDashboardStats | null> {
  try {
    const overview = await getAdminOverview();
    if (!overview) return null;

    const now = new Date();
    return {
      todayCount: overview.todayBookings,
      activeHoldsCount: overview.activeHoldsCount ?? 0,
      pausedCount: overview.pausedVenuesCount ?? 0,
      noShowCount: overview.todayNoShowsCount ?? 0,
      scheduleConflictsCount: overview.scheduleConflictsCount ?? 0,
      scheduleConflictLocationIds: overview.scheduleConflictLocationIds ?? [],
      totalGuests: overview.totalCapacity,
      occupancyData: overview.occupancyData ?? [],
      occupancyDates: overview.occupancyDates ?? [],
      occupancyCounts: overview.occupancyCounts ?? [],
      recentBookings: (overview.todayBookingsList ?? [])
        .map((b) => ({
          id: b.id,
          date: b.date,
          endTime: b.endTime,
          customerEmail: b.customerEmail,
          customerName: b.customerName,
          partySize: b.partySize,
          venueName: b.venueName,
          bookingRef: b.bookingRef ?? "",
          isCancelled: b.isCancelled,
          status: b.status,
        }))
        .filter((b) => {
          const end = b.endTime
            ? new Date(b.endTime)
            : new Date(new Date(b.date).getTime() + 60 * 60 * 1000);
          return end >= now;
        })
        .slice(0, 5),
      pacing: overview.todayPacing ?? [],
    };
  } catch (err) {
    console.error("getAdminDashboardStats error:", err);
    return null;
  }
}

export interface BookingDetailDto {
  id: number;
  venueId: number;
  venueName: string;
  /**
   * The location's IANA timezone. `date` is UTC and names no wall-clock time on its own, so
   * anything shown to or sent to a guest resolves through this, not the admin's browser zone.
   */
  timezone?: string;
  sectionId: number | null;
  sectionName: string;
  resourceId: number | null;
  /**
   * Set when the booking reserves a combinable resource group rather than a single resource, in which
   * case `resourceId` is null. Placing a booking on the floor has to branch on both or every group
   * booking is silently dropped.
   */
  resourceGroupId?: number | null;
  resourceName: string;
  date: string;
  endTime?: string;
  customerEmail: string;
  customerName?: string;
  partySize: number;
  specialRequests?: string;
  bookingRef?: string;
  isCancelled?: boolean;
  cancelledAt?: string;
  /** What has happened at the slot. Independent of `isCancelled`. */
  status?: BookingStatus;
  /** The statuses staff can move the booking to now, as the server decides them. */
  nextStatuses?: BookingStatus[];
  /** The status the last change can still be undone to, while the undo window is open. */
  undoStatus?: BookingStatus | null;
  /** Other bookings under the guest's email marked as no-shows; null where not counted. */
  previousNoShows?: number | null;
}

export type BookingStatus = "Booked" | "Arrived" | "InUse" | "Finished" | "NoShow";

export interface AdminCreateBookingRequest {
  venueId: number;
  sectionId: number;
  resourceId: number;
  date: string;
  customerEmail: string;
  customerName?: string;
  partySize: number;
}

export interface CreateVenueRequest {
  name: string;
  address?: string;
}

export async function getAdminOverview(): Promise<AdminOverviewDto | null> {
  try {
    const res = await get("/admin/overview");
    if (!res.ok) throw new Error("Failed to fetch overview");
    return await res.json();
  } catch (err) {
    console.error("getAdminOverview error:", err);
    return null;
  }
}

export type BookingStatusFilter = "active" | "past" | "cancelled" | "noshow" | "all";

export async function getAdminBookings(
  venueId?: number,
  date?: string,
  status: BookingStatusFilter = "active",
  search?: string
): Promise<BookingDetailDto[]> {
  try {
    const params = new URLSearchParams();
    if (venueId != null) params.set("venueId", String(venueId));
    if (date) params.set("date", date);
    if (status !== "active") params.set("status", status);
    if (search) params.set("query", search);
    const query = params.toString() ? `?${params}` : "";
    const res = await get(`/admin/bookings${query}`);
    if (!res.ok) throw new Error("Failed to fetch admin bookings");
    return await res.json();
  } catch (err) {
    console.error("getAdminBookings error:", err);
    return [];
  }
}

/**
 * Free-text booking search across every location. One `query` param, matched server-side as a
 * substring of the customer name, the customer email and the booking reference at once — the
 * caller does not have to guess which field the admin typed.
 *
 * @see [admin.test.ts](../tests/api/admin.test.ts) — pins that a partial name, a partial
 * email and a partial reference all go out as the same `query` param.
 */
export async function adminLookupBookings(query: string): Promise<BookingDetailDto[]> {
  return getAdminBookings(undefined, undefined, "all", query);
}

export async function getAdminBooking(id: number): Promise<BookingDetailDto | null> {
  try {
    const res = await get(`/admin/bookings/${id}`);
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("getAdminBooking error:", err);
    return null;
  }
}

export async function adminCreateBooking(
  req: AdminCreateBookingRequest
): Promise<BookingDetailDto | null> {
  const res = await post("/admin/bookings", req);

  if (res.status === 409) {
    const body = await res.json().catch(() => ({}));
    throw new Error(apiErrorMessage(body, "This resource is already booked on that date."));
  }

  if (!res.ok) throw new Error("Failed to create booking");
  return await res.json();
}

export async function adminExtendBooking(
  id: number,
  minutes: number
): Promise<{ endTime: string } | null> {
  try {
    const res = await post(`/admin/bookings/${id}/extend`, { minutes });
    if (!res.ok) throw new Error("Failed to extend booking");
    return await res.json();
  } catch (err) {
    console.error("adminExtendBooking error:", err);
    return null;
  }
}

/**
 * Moves a booking along the floor, or back to `undoStatus` to take the last move back. Throws
 * with the server's reason when the move is refused, the same way cancelling does.
 */
export async function adminSetBookingStatus(
  id: number,
  status: BookingStatus
): Promise<BookingDetailDto> {
  const res = await post(`/admin/bookings/${id}/status`, { status });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(apiErrorMessage(body, "Failed to update the booking's status."));
  }
  return await res.json();
}

export async function adminDeleteBooking(id: number): Promise<true> {
  try {
    const res = await post(`/admin/bookings/${id}/cancel`);
    if (!res.ok) {
      const body = await res.json().catch(() => ({}));
      throw new Error(apiErrorMessage(body, "Failed to cancel the booking."));
    }
    return true;
  } catch (err) {
    console.error("adminDeleteBooking error:", err);
    throw err;
  }
}

export async function adminPurgeBooking(id: number): Promise<boolean> {
  try {
    const res = await del(`/admin/bookings/${id}`);
    return res.ok;
  } catch (err) {
    console.error("adminPurgeBooking error:", err);
    return false;
  }
}

export async function sendBookingEmail(
  bookingId: number,
  subject: string,
  body: string
): Promise<{ ok: boolean; message: string }> {
  try {
    const res = await post(`/admin/bookings/${bookingId}/email`, { subject, body });
    const data = await res.json();
    return { ok: res.ok, message: data.message };
  } catch {
    return { ok: false, message: "Network error." };
  }
}

export async function adminRestoreBooking(id: number): Promise<boolean> {
  try {
    const res = await post(`/admin/bookings/${id}/restore`);
    if (!res.ok) {
      const body = await res.json().catch(() => ({}));
      throw new Error(apiErrorMessage(body, "Failed to restore booking"));
    }
    return true;
  } catch (err) {
    console.error("adminRestoreBooking error:", err);
    throw err;
  }
}

export interface AdminUpdateBookingRequest {
  venueId?: number;
  sectionId?: number;
  resourceId?: number;
  date?: string;
  partySize?: number;
  customerEmail?: string;
  customerName?: string;
  specialRequests?: string;
}

export async function adminUpdateBookingFull(
  id: number,
  req: AdminUpdateBookingRequest
): Promise<BookingDetailDto | null> {
  try {
    const res = await put(`/admin/bookings/${id}`, req);
    if (!res.ok) {
      const body = await res.json().catch(() => ({}));
      throw new Error(apiErrorMessage(body, "Failed to update booking"));
    }
    return await res.json();
  } catch (err) {
    console.error("adminUpdateBookingFull error:", err);
    throw err;
  }
}

export async function adminCreateVenue(
  req: CreateVenueRequest
): Promise<{ id: number; name: string; address?: string } | null> {
  try {
    const res = await post("/admin/venues", req);
    if (!res.ok) throw new Error("Failed to create location");
    return await res.json();
  } catch (err) {
    console.error("adminCreateVenue error:", err);
    return null;
  }
}

export async function adminDeleteVenue(id: number): Promise<boolean> {
  try {
    const res = await del(`/admin/venues/${id}`);
    return res.ok;
  } catch (err) {
    console.error("adminDeleteVenue error:", err);
    return false;
  }
}

export async function pauseVenueBookings(id: number, minutes: number): Promise<boolean> {
  try {
    const res = await post(`/admin/venues/${id}/pause`, { minutes });
    return res.ok;
  } catch (err) {
    console.error("pauseVenueBookings error:", err);
    return false;
  }
}

export async function unpauseVenueBookings(id: number): Promise<boolean> {
  try {
    const res = await post(`/admin/venues/${id}/unpause`, {});
    return res.ok;
  } catch (err) {
    console.error("unpauseVenueBookings error:", err);
    return false;
  }
}

export async function extendVenueBookings(
  id: number,
  minutes: number
): Promise<{ ok: boolean; extendedBookings: BookingDetailDto[] }> {
  try {
    const res = await post(`/admin/venues/${id}/extend`, { minutes });
    if (!res.ok) return { ok: false, extendedBookings: [] };
    const data = await res.json();
    return { ok: true, extendedBookings: data.extendedBookings || [] };
  } catch (err) {
    console.error("extendVenueBookings error:", err);
    return { ok: false, extendedBookings: [] };
  }
}

export interface SectionWithResources {
  id: number;
  name: string;
  resources: { id: number; name: string; capacity: number }[];
}

export async function adminGetResources(venueId: number): Promise<SectionWithResources[]> {
  try {
    const res = await get(`/admin/venues/${venueId}/resources`);
    if (!res.ok) return [];
    return await res.json();
  } catch (err) {
    console.error("adminGetResources error:", err);
    return [];
  }
}

/** A location as the admin lists it — archived rows included, unlike `fetchVenues`. */
export interface AdminVenueSummary {
  id: number;
  name: string;
  bookingsPausedUntil?: string;
  activeBookingsCount?: number;
  upcomingBookingsCount?: number;
  isArchived?: boolean;
}

export async function adminGetVenues(): Promise<AdminVenueSummary[]> {
  try {
    const res = await get("/admin/venues");
    if (!res.ok) return [];
    return await res.json();
  } catch (err) {
    console.error("adminGetVenues error:", err);
    return [];
  }
}

/** What deleting a location would destroy. Owner-only, like the delete itself. */
export interface VenueDeletePreview {
  id: number;
  name: string;
  isArchived: boolean;
  sectionCount: number;
  resourceCount: number;
  resourceGroupCount: number;
  bookingCount: number;
  upcomingBookingCount: number;
}

export async function adminGetVenueDeletePreview(id: number): Promise<VenueDeletePreview | null> {
  try {
    const res = await get(`/admin/venues/${id}/delete-preview`);
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("adminGetVenueDeletePreview error:", err);
    return null;
  }
}

export async function adminSetVenueArchived(id: number, archived: boolean): Promise<boolean> {
  try {
    const res = await patch(`/admin/venues/${id}`, { isArchived: archived });
    return res.ok;
  } catch (err) {
    console.error("adminSetVenueArchived error:", err);
    return false;
  }
}

export async function adminGetSections(venueId: number): Promise<{ id: number; name: string }[]> {
  try {
    const res = await get(`/admin/venues/${venueId}/sections`);
    if (!res.ok) return [];
    return await res.json();
  } catch (err) {
    console.error("adminGetSections error:", err);
    return [];
  }
}

/**
 * Persists a new display order for a venue's sections. Pass the full list of
 * section IDs in the desired order (used by the up/down move buttons — the caller
 * computes the swapped order locally and resends the whole list).
 */
export async function reorderSections(venueId: number, sectionIds: number[]): Promise<boolean> {
  try {
    const res = await patch(`/admin/venues/${venueId}/sections/reorder`, {
      sectionIds,
    });
    return res.ok;
  } catch (err) {
    console.error("reorderSections error:", err);
    return false;
  }
}

export interface EmailFailureDto {
  id: number;
  bookingRef: string | null;
  recipientEmail: string;
  errorMessage: string;
  attemptedAt: string;
}

export async function getEmailFailures(): Promise<EmailFailureDto[]> {
  try {
    const res = await get("/admin/email-settings/failures");
    if (!res.ok) return [];
    return await res.json();
  } catch {
    return [];
  }
}

/** The confirmation email as a guest would receive it, rendered server-side for the admin. */
export interface EmailPreviewDto {
  venueId: number | null;
  venueName: string;
  recipientEmail: string;
  subject: string;
  html: string;
}

/**
 * Renders the booking confirmation for a stand-in booking. The HTML comes from the same template
 * the send path uses, so the panel never has to keep a copy of the markup in step with it.
 */
export async function getEmailPreview(venueId?: number): Promise<EmailPreviewDto | null> {
  try {
    const query = venueId === undefined ? "" : `?venueId=${venueId}`;
    const res = await get(`/admin/email-settings/preview${query}`);
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("getEmailPreview error:", err);
    return null;
  }
}

export interface EmailSettingsDto {
  host: string;
  port: number;
  username: string;
  password: string;
  enableSsl: boolean;
  fromName?: string;
  fromEmail?: string;
  isConfigured: boolean;
  sendBookingConfirmations: boolean;
}

export async function getEmailSettings(): Promise<EmailSettingsDto> {
  try {
    const res = await get("/admin/email-settings");
    if (!res.ok) throw new Error();
    return await res.json();
  } catch {
    return {
      host: "",
      port: 587,
      username: "",
      password: "",
      enableSsl: true,
      isConfigured: false,
      sendBookingConfirmations: false,
    };
  }
}

export async function saveEmailSettings(
  data: Omit<EmailSettingsDto, "isConfigured">
): Promise<{ message: string } | null> {
  try {
    const res = await patch("/admin/email-settings", data);
    return await res.json();
  } catch {
    return null;
  }
}

export async function testEmailConnection(): Promise<{ ok: boolean; message: string }> {
  try {
    const res = await post("/admin/email-settings/test");
    const data = await res.json();
    return { ok: res.ok, message: data.message };
  } catch {
    return { ok: false, message: "Network error." };
  }
}

export interface BrandSettingsDto {
  appName?: string;
  primaryColor?: string;
  accentColor?: string;
  faviconIcon?: string;
  websiteUrl?: string;
  phoneNumber?: string;
  emailAddress?: string;
  copyrightText?: string;
  subtitle?: string;
  highlightsHeading?: string;
  highlightsSubheading?: string;
  headerImageFit?: string;
  /** `""` clears; `undefined` leaves the stored value alone (the same contract as every field above). */
  privacyPolicyUrl?: string;
  /** `major.minor.patch`, or `""` to lift the floor. */
  minimumAppVersion?: string;
}

/**
 * Result-typed rather than a bare `{ message }`: success and failure both come back as a
 * message, and callers used to tell them apart by looking for "fail" in the text — which
 * quietly reported every validation error ("Subtitle cannot exceed 160 characters.") as a
 * successful save. Null still means the request never landed.
 */
export async function saveBrandSettings(
  data: BrandSettingsDto
): Promise<AdminMutationResult<{ message: string }> | null> {
  try {
    const res = await patch("/brand", data);
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      return { ok: false, message: apiErrorMessage(err, "Failed to save.") };
    }
    return { ok: true, data: await res.json() };
  } catch {
    return null;
  }
}

export async function uploadHeroImage(file: File): Promise<string | null> {
  try {
    const form = new FormData();
    form.append("file", file);
    const res = await fetch(buildUrl("/media/hero"), {
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

export async function deleteHeroImage(): Promise<boolean> {
  try {
    const res = await fetch(buildUrl("/media/hero"), {
      method: "DELETE",
      credentials: "include",
    });
    return res.ok;
  } catch {
    return false;
  }
}

export interface AdminHighlightDto {
  id: number;
  title: string;
  body: string;
  iconKey: string;
  sortOrder: number;
  link?: string | null;
}

export interface CreateHighlightRequest {
  title: string;
  body: string;
  iconKey: string;
  sortOrder: number;
  link?: string | null;
}

export interface UpdateHighlightRequest {
  title: string;
  body: string;
  iconKey: string;
  sortOrder: number;
  link?: string | null;
}

export async function adminGetHighlights(): Promise<AdminHighlightDto[]> {
  try {
    const res = await get("/highlights");
    if (!res.ok) return [];
    return await res.json();
  } catch (err) {
    console.error("adminGetHighlights error:", err);
    return [];
  }
}

/**
 * Result of a create/update admin mutation. Success carries the saved DTO; failure carries the
 * server's message (or a generic fallback). Network failures (no response) return `null`, which
 * callers treat as "couldn't reach the server" — the same convention as {@link saveBrandSettings}.
 * The error body shape mirrors the backend `MessageResponse { message }` (never RFC 7807).
 */
export type AdminMutationResult<T> = { ok: true; data: T } | { ok: false; message: string };

export async function adminCreateHighlight(
  req: CreateHighlightRequest
): Promise<AdminMutationResult<AdminHighlightDto> | null> {
  try {
    const res = await post("/highlights", req);
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      return { ok: false, message: apiErrorMessage(err, "Failed to create highlight.") };
    }
    return { ok: true, data: await res.json() };
  } catch (err) {
    console.error("adminCreateHighlight error:", err);
    return null;
  }
}

export async function adminUpdateHighlight(
  id: number,
  req: UpdateHighlightRequest
): Promise<AdminMutationResult<AdminHighlightDto> | null> {
  try {
    const res = await put(`/highlights/${id}`, req);
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      return { ok: false, message: apiErrorMessage(err, "Failed to update highlight.") };
    }
    return { ok: true, data: await res.json() };
  } catch (err) {
    console.error("adminUpdateHighlight error:", err);
    return null;
  }
}

export async function adminDeleteHighlight(id: number): Promise<boolean> {
  try {
    const res = await del(`/highlights/${id}`);
    return res.ok;
  } catch (err) {
    console.error("adminDeleteHighlight error:", err);
    return false;
  }
}

export interface AdminSocialLinkDto {
  id: number;
  label: string;
  url: string;
  iconKey: string;
  sortOrder: number;
}

export interface CreateSocialLinkRequest {
  label: string;
  url: string;
  iconKey: string;
  sortOrder: number;
}

export interface UpdateSocialLinkRequest {
  label: string;
  url: string;
  iconKey: string;
  sortOrder: number;
}

export async function adminGetSocialLinks(): Promise<AdminSocialLinkDto[]> {
  try {
    const res = await get("/social-links");
    if (!res.ok) return [];
    return await res.json();
  } catch (err) {
    console.error("adminGetSocialLinks error:", err);
    return [];
  }
}

export async function adminCreateSocialLink(
  req: CreateSocialLinkRequest
): Promise<AdminMutationResult<AdminSocialLinkDto> | null> {
  try {
    const res = await post("/social-links", req);
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      return { ok: false, message: apiErrorMessage(err, "Failed to create social link.") };
    }
    return { ok: true, data: await res.json() };
  } catch (err) {
    console.error("adminCreateSocialLink error:", err);
    return null;
  }
}

export async function adminUpdateSocialLink(
  id: number,
  req: UpdateSocialLinkRequest
): Promise<AdminMutationResult<AdminSocialLinkDto> | null> {
  try {
    const res = await put(`/social-links/${id}`, req);
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      return { ok: false, message: apiErrorMessage(err, "Failed to update social link.") };
    }
    return { ok: true, data: await res.json() };
  } catch (err) {
    console.error("adminUpdateSocialLink error:", err);
    return null;
  }
}

export async function adminDeleteSocialLink(id: number): Promise<boolean> {
  try {
    const res = await del(`/social-links/${id}`);
    return res.ok;
  } catch (err) {
    console.error("adminDeleteSocialLink error:", err);
    return false;
  }
}
