import { BookingDetailDto, BookingStatusFilter } from "@/api/admin";
import { statusRankFor } from "@/components/admin/bookings/StatusBadge";

/**
 * User-controlled sorting for the admin bookings list.
 *
 * The list previously had a single hardcoded rule: sort by date, ascending for
 * the "past" tab and descending otherwise. These helpers expose the sort as
 * user state (column + direction) while preserving that rule as the per-filter
 * default, so the keyboard-navigation index math in `index.tsx` (which depends
 * on the rendered order of `sorted`) keeps working unchanged.
 */

/** Columns a booking list can be sorted by. */
export type SortKey = "date" | "guest" | "partySize" | "resource" | "status";

export type SortDir = "asc" | "desc";

export interface SortState {
  key: SortKey;
  dir: SortDir;
}

/**
 * Contextual default for a status filter. Matches the pre-sort behavior:
 * soonest-first (asc) for everything except "past", which is most-recent-first
 * (desc). `cancelled` follows the "past" convention — both are historical views
 * where the most recent event is the interesting one.
 */
export function defaultSortFor(statusFilter: BookingStatusFilter): SortState {
  const historical =
    statusFilter === "past" || statusFilter === "cancelled" || statusFilter === "noshow";
  return { key: "date", dir: historical ? "desc" : "asc" };
}

/**
 * Click-toggle rule for a column header:
 * - same column → flip direction
 * - different column → switch to it, using its natural default direction
 *   (date/partySize/status descending = newest/largest/most-urgent first;
 *   guest/resource ascending = A→Z)
 */
export function nextSort(current: SortState, key: SortKey): SortState {
  if (current.key === key) {
    return { key, dir: current.dir === "asc" ? "desc" : "asc" };
  }
  const defaultDir: SortDir =
    key === "date" || key === "partySize" || key === "status" ? "desc" : "asc";
  return { key, dir: defaultDir };
}

function guestLabel(b: BookingDetailDto): string {
  return (b.customerName ?? b.customerEmail ?? "").toLowerCase();
}

/** Raw comparator for two bookings by key/direction. */
export function compareBookings(
  a: BookingDetailDto,
  b: BookingDetailDto,
  state: SortState
): number {
  let diff: number;
  switch (state.key) {
    case "date":
      diff = new Date(a.date).getTime() - new Date(b.date).getTime();
      break;
    case "guest":
      diff = guestLabel(a).localeCompare(guestLabel(b));
      break;
    case "partySize":
      diff = a.partySize - b.partySize;
      break;
    case "resource":
      diff = (a.resourceName ?? "").localeCompare(b.resourceName ?? "");
      break;
    case "status":
      diff = statusRankFor(a) - statusRankFor(b);
      break;
  }
  return state.dir === "asc" ? diff : -diff;
}

/** Returns a new, sorted copy of the list (does not mutate the input). */
export function sortBookings(bookings: BookingDetailDto[], state: SortState): BookingDetailDto[] {
  return [...bookings].sort((a, b) => compareBookings(a, b, state));
}
