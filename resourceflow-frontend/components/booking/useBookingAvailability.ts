import { useEffect, useState } from "react";
import { fetchAvailability, TimeSlotDto } from "@/api/availability";
import { isWalkInOnlyOnDate } from "@/utils/walkIn";
import type { VenueDto } from "@/api/venues";

export interface UseBookingAvailabilityArgs {
  venue: VenueDto;
  date: string;
  partySize: number;
  time: string;
  /** Called when the picked time isn't bookable and the first available one takes its place. */
  onTimeCorrected: (time: string) => void;
}

/**
 * Availability for the booking form's current (date, party size).
 *
 * Closed and walk-in-only days short-circuit to an empty list rather than a request — the
 * server would return no slots for them anyway, and the form renders its own notice instead.
 */
export function useBookingAvailability({
  venue,
  date,
  partySize,
  time,
  onTimeCorrected,
}: UseBookingAvailabilityArgs) {
  const [availabilitySlots, setAvailabilitySlots] = useState<TimeSlotDto[]>([]);
  const [loadingAvailability, setLoadingAvailability] = useState(false);

  useEffect(() => {
    const openDaysList = venue.openDays?.split(",").map(Number) ?? [1, 2, 3, 4, 5, 6, 7];
    const jsDay = date ? new Date(date + "T12:00:00").getDay() : -1;
    const isoDay = jsDay === 0 ? 7 : jsDay;
    if (date && (!openDaysList.includes(isoDay) || isWalkInOnlyOnDate(venue, date))) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setAvailabilitySlots([]);
      setLoadingAvailability(false);
      return;
    }
    async function loadAvailability() {
      setLoadingAvailability(true);
      try {
        const res = await fetchAvailability(venue.id, date, partySize);
        if (res && res.slots) {
          setAvailabilitySlots(res.slots);
          const isCurrentValid = res.slots.find((s) => s.time === time && s.isAvailable);
          if (!isCurrentValid) {
            const firstAvail = res.slots.find((s) => s.isAvailable);
            if (firstAvail) {
              onTimeCorrected(firstAvail.time);
            }
          }
        } else {
          setAvailabilitySlots([]);
        }
      } finally {
        setLoadingAvailability(false);
      }
    }
    loadAvailability();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [date, partySize, venue.id]);

  return { availabilitySlots, loadingAvailability };
}
