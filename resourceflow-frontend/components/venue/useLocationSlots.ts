import { useEffect, useMemo, useState } from "react";
import { fetchAvailability, TimeSlotDto } from "@/api/availability";
import { getVenueNow } from "@/utils/venueTime";
import type { TimeWindow } from "@/components/venue/LocationsFilterBar";

export interface UseLocationSlotsArgs {
  venueId: number;
  date: string;
  partySize: number;
  timeWindow: TimeWindow;
  /** False for closed / walk-in-only days: no request goes out and the list stays empty. */
  bookable: boolean;
  /** True when `date` is the venue's own today, which is what makes past slots unusable. */
  isToday: boolean;
  /** IANA zone the venue keeps its clock in. */
  timezone: string;
  onAvailabilityChange?: (id: number, availableSlots: number | null) => void;
}

/**
 * Availability for one location row, narrowed to slots the visitor can actually book.
 *
 * Refetches whenever the filter bar changes, which is what makes the cards comparable
 * against one another. On today, slots are cut against the *venue's* clock rather
 * than the viewer's, so a visitor in another timezone doesn't get offered a past slot.
 */
export function useLocationSlots({
  venueId,
  date,
  partySize,
  timeWindow,
  bookable,
  isToday,
  timezone,
  onAvailabilityChange,
}: UseLocationSlotsArgs) {
  const [slots, setSlots] = useState<TimeSlotDto[]>([]);
  const [slotsLoading, setSlotsLoading] = useState(true);

  useEffect(() => {
    if (!bookable) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSlots([]);
      setSlotsLoading(false);
      return;
    }
    let cancelled = false;
    setSlotsLoading(true);
    fetchAvailability(venueId, date, partySize)
      .then((data) => {
        if (cancelled) return;
        setSlots(data && Array.isArray(data.slots) ? data.slots.filter((s) => s.isAvailable) : []);
        setSlotsLoading(false);
      })
      .catch(() => {
        if (cancelled) return;
        setSlots([]);
        setSlotsLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [venueId, date, partySize, bookable]);

  const usableSlots = useMemo(() => {
    const inWindow = timeWindow === "All" ? slots : slots.filter((s) => s.category === timeWindow);
    if (!isToday) return inWindow;
    const { totalMins } = getVenueNow(timezone);
    return inWindow.filter((s) => {
      const [h, m] = s.time.split(":").map(Number);
      return h * 60 + (m || 0) > totalMins;
    });
  }, [slots, timeWindow, isToday, timezone]);

  useEffect(() => {
    onAvailabilityChange?.(venueId, slotsLoading ? null : usableSlots.length);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [venueId, usableSlots.length, slotsLoading]);

  return { slotsLoading, usableSlots };
}
