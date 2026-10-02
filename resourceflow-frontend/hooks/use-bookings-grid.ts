import { useCallback, useEffect, useState } from "react";
import {
  adminGetResources,
  getAdminBookings,
  type BookingDetailDto,
  type SectionWithResources,
} from "@/api/admin";
import { isoDate } from "@/utils/formatters";

export interface UseBookingsGridOptions {
  /** Currently selected venue; the grid (re)loads when this changes. */
  venueId: number | null;
  /** Active view mode — the grid only loads on venue change for a grid-backed mode. */
  viewMode: "timetable" | "service" | "list";
}

export interface UseBookingsGridResult {
  gridDate: Date;
  gridSections: SectionWithResources[];
  gridBookings: BookingDetailDto[];
  gridLoading: boolean;
  /** Imperative reload — used by the screen after mutations and venue switches. */
  loadGrid: (venueId: number, date: Date) => Promise<void>;
  /** Step the grid date by ±1 day and reload. */
  handleGridDateChange: (delta: number) => void;
  /** Reset the grid date to today and reload (if a venue is selected). */
  resetToToday: () => void;
}

/**
 * Timetable grid state + fetch for the admin bookings screen.
 *
 * Owns: the selected grid day, the fetched sections/bookings for that day, and
 * the loading flag. Exposes imperative loaders so the screen can reconcile the
 * grid after mutations (cancel/create/edit) and venue switches.
 *
 * The screen retains `viewMode` and `selectedVenueId` (orchestration state)
 * and passes them in; this hook does not decide *when* to show the grid, only
 * how its data is fetched and navigated.
 */
export function useBookingsGrid({
  venueId,
  viewMode,
}: UseBookingsGridOptions): UseBookingsGridResult {
  const [gridDate, setGridDate] = useState(new Date());
  const [gridSections, setGridSections] = useState<SectionWithResources[]>([]);
  const [gridBookings, setGridBookings] = useState<BookingDetailDto[]>([]);
  const [gridLoading, setGridLoading] = useState(false);

  const loadGrid = useCallback(async (rid: number, date: Date) => {
    setGridLoading(true);
    const [sections, bookingsForDate] = await Promise.all([
      adminGetResources(rid),
      getAdminBookings(rid, isoDate(date)),
    ]);
    setGridSections(sections);
    setGridBookings(bookingsForDate);
    setGridLoading(false);
  }, []);

  const handleGridDateChange = useCallback(
    (delta: number) => {
      setGridDate((prev) => {
        const next = new Date(prev);
        next.setDate(next.getDate() + delta);
        if (venueId) loadGrid(venueId, next);
        return next;
      });
    },
    [venueId, loadGrid]
  );

  const resetToToday = useCallback(() => {
    const today = new Date();
    setGridDate(today);
    if (venueId) loadGrid(venueId, today);
  }, [venueId, loadGrid]);

  // Load the grid on mount / when the selected venue changes (grid-backed views only).
  useEffect(() => {
    if (venueId && viewMode !== "list") {
      loadGrid(venueId, gridDate);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [venueId]);

  return {
    gridDate,
    gridSections,
    gridBookings,
    gridLoading,
    loadGrid,
    handleGridDateChange,
    resetToToday,
  };
}
