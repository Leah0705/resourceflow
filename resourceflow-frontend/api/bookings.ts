import { get, post, del } from "./client";
import { apiErrorMessage } from "@/api/errors";

export interface BookingDto {
  id: number;
  resourceId: number | null;
  sectionId: number | null;
  /**
   * Combinable-resource group id when this booking reserves a merged group of resources; null for
   * a single-resource booking. Mutually exclusive with resourceId. When set, resourceName carries the group
   * label (e.g. "Resources 8 + 9") and resourceCapacity carries the group's combined capacity.
   */
  resourceGroupId?: number | null;
  venueId: number;
  date: string;
  endTime?: string;
  customerEmail: string;
  customerName?: string;
  partySize: number;
  isHeld: boolean;
  specialRequests?: string;
  bookingRef?: string;
  resourceName?: string;
  sectionName?: string;
  resourceCapacity?: number;
  isCancelled?: boolean;
}

export interface BookingCreationDto {
  venueId: number;
  /** Omit (or null) for "Any section" auto-assign — the server picks the best resource. */
  resourceId: number | null;
  /** Omit (or null) for "Any section" auto-assign. */
  sectionId: number | null;
  /**
   * Combinable-resource group id when booking a combined group; null otherwise. Mutually
   * exclusive with resourceId.
   */
  resourceGroupId?: number | null;
  customerEmail: string;
  customerName: string;
  partySize: number;
  date: string;
  holdId?: string | null;
  specialRequests?: string | null;
}

/** Normalize PascalCase API responses to camelCase BookingDto */
function normalizeBooking(raw: Record<string, unknown>): BookingDto {
  return {
    id: (raw.id ?? raw.Id) as number,
    resourceId: (raw.resourceId ?? raw.ResourceId ?? null) as number | null,
    sectionId: (raw.sectionId ?? raw.SectionId ?? null) as number | null,
    resourceGroupId: (raw.resourceGroupId ?? raw.ResourceGroupId ?? null) as number | null,
    venueId: (raw.venueId ?? raw.VenueId) as number,
    date: (raw.date ?? raw.Date) as string,
    endTime: (raw.endTime ?? raw.EndTime) as string | undefined,
    customerEmail: (raw.customerEmail ?? raw.CustomerEmail) as string,
    customerName: (raw.customerName ?? raw.CustomerName) as string | undefined,
    partySize: (raw.partySize ?? raw.PartySize) as number,
    isHeld: (raw.isHeld ?? raw.IsHeld ?? false) as boolean,
    specialRequests: (raw.specialRequests ?? raw.SpecialRequests) as string | undefined,
    bookingRef: (raw.bookingRef ?? raw.BookingRef) as string | undefined,
    resourceName: (raw.resourceName ?? raw.ResourceName) as string | undefined,
    sectionName: (raw.sectionName ?? raw.SectionName) as string | undefined,
    resourceCapacity: (raw.resourceCapacity ?? raw.ResourceCapacity) as number | undefined,
    isCancelled: (raw.isCancelled ?? raw.IsCancelled) as boolean | undefined,
  };
}

export async function createBooking(booking: BookingCreationDto): Promise<BookingDto | null> {
  const res = await post("/bookings", booking);

  if (res.status === 409) {
    const body = await res.json().catch(() => ({}));
    throw new Error(apiErrorMessage(body, "This resource is no longer available."));
  }

  if (!res.ok) throw new Error("Failed to create booking");
  return normalizeBooking(await res.json());
}

export async function getBookingById(id: number): Promise<BookingDto | null> {
  try {
    const res = await get(`/bookings/${id}`);
    if (!res.ok) throw new Error("Failed to fetch booking");
    return normalizeBooking(await res.json());
  } catch (err) {
    console.error("getBookingById error:", err);
    return null;
  }
}

/**
 * Looks up a booking by reference and email. Returns null only for a genuine
 * "no booking matches" (the backend 404s for both an unknown ref and a
 * ref/email mismatch, deliberately not distinguishing the two). Anything
 * else — a 5xx, a timeout, a network failure — throws instead of also
 * collapsing to null, so callers can tell "this booking doesn't exist" apart
 * from "we couldn't check" rather than showing the same not-found copy for both.
 */
export async function getBookingByRef(
  bookingRef: string,
  email: string
): Promise<BookingDto | null> {
  const res = await get(`/bookings/ref/${bookingRef}?email=${encodeURIComponent(email)}`);
  if (res.status === 404) return null;
  if (!res.ok) throw new Error("Failed to fetch booking");
  return normalizeBooking(await res.json());
}

export async function getBookingsByVenue(venueId: number): Promise<BookingDto[]> {
  try {
    const res = await get(`/venues/${venueId}/bookings`);
    if (!res.ok) throw new Error("Failed to fetch bookings");
    const data: Record<string, unknown>[] = await res.json();
    return data.map(normalizeBooking);
  } catch (err) {
    console.error("getBookingsByVenue error:", err);
    return [];
  }
}

export async function deleteBooking(id: number): Promise<boolean> {
  try {
    const res = await del(`/bookings/${id}`);
    return res.ok;
  } catch (err) {
    console.error("deleteBooking error:", err);
    return false;
  }
}

export async function cancelBookingByRef(bookingRef: string, email: string): Promise<true> {
  try {
    const res = await post(`/bookings/ref/${bookingRef}/cancel`, { email });
    if (!res.ok) {
      const body = await res.json().catch(() => ({}));
      throw new Error(apiErrorMessage(body, "Failed to cancel booking."));
    }
    return true;
  } catch (err) {
    console.error("cancelBookingByRef error:", err);
    throw err;
  }
}
