import type { TimelinePlacement, TimelineRowGroup, TimelineUnit } from "@/utils/bookingTimeline";

/**
 * A resource with a slot arriving within this window is not offerable to a walk-in, so the floor
 * reads it as ending rather than free.
 *
 * @see [serviceView.test.ts](../tests/utils/serviceView.test.ts) — pins that a resource whose next
 * slot is exactly this far out reads as ending, and one a minute further out reads as free.
 */
export const TURNAROUND_MINUTES = 30;

export type UnitStatus = "inUse" | "ending" | "free";

export interface UnitOccupancy {
  unit: TimelineUnit;
  status: UnitStatus;
  /** The slot covering the observed moment, if any. */
  current: TimelinePlacement | null;
  /** The next slot to start after it, whether or not the unit is occupied now. */
  next: TimelinePlacement | null;
  /** Minutes until the current slot ends. Zero when nothing is booked there. */
  minutesRemaining: number;
  /** Minutes until the next slot starts; null when nothing else is booked. */
  minutesUntilNext: number | null;
}

export interface ServiceSection {
  key: string;
  /** Carried through from the row block, so the view can label the synthetic blocks in its locale. */
  kind: TimelineRowGroup["kind"];
  name: string;
  units: UnitOccupancy[];
}

export interface ServiceSummary {
  inUse: number;
  ending: number;
  free: number;
  /** Guests in session at the observed moment — the number the front desk is actually serving. */
  guests: number;
}

/**
 * A unit's occupancy at one moment, in timeline-offset minutes. A slot owns its resource from its
 * start up to but not including its end, so a resource whose slot ends at 20:00 is free at 20:00
 * rather than double-counted against the slot that starts there.
 *
 * @see [serviceView.test.ts](../tests/utils/serviceView.test.ts) — pins that a unit is in use at
 * its slot's start minute and free at its end minute.
 */
function occupancyFor(
  unit: TimelineUnit,
  placements: TimelinePlacement[],
  at: number
): UnitOccupancy {
  const ordered = [...placements].sort((a, b) => a.startOffset - b.startOffset);
  const current = ordered.find((p) => at >= p.startOffset && at < p.endOffset) ?? null;
  const next = ordered.find((p) => p.startOffset > at) ?? null;

  const minutesRemaining = current ? current.endOffset - at : 0;
  const minutesUntilNext = next ? next.startOffset - at : null;

  const status: UnitStatus = current
    ? "inUse"
    : minutesUntilNext != null && minutesUntilNext <= TURNAROUND_MINUTES
      ? "ending"
      : "free";

  return { unit, status, current, next, minutesRemaining, minutesUntilNext };
}

/**
 * The unit keys that share physical space with each other: a combinable group with each of its
 * member resources, both ways round. Combining two resources does not conjure a third resource, so a
 * slot booked on either side occupies both.
 */
function sharedPlacement(rows: TimelineRowGroup[]): Map<string, string[]> {
  const shared = new Map<string, string[]>();
  const link = (from: string, to: string) => {
    const list = shared.get(from);
    if (list) list.push(to);
    else shared.set(from, [to]);
  };

  for (const row of rows) {
    for (const unit of row.units) {
      for (const memberKey of unit.memberKeys) {
        link(unit.key, memberKey);
        link(memberKey, unit.key);
      }
    }
  }

  return shared;
}

/**
 * The floor as it stands at one moment: every bookable unit under its section, each carrying who is
 * on it, how much of their slot is left, and what arrives next.
 *
 * Rows come from the timetable's own `buildUnitRows`, so a combinable group is a unit beside its
 * member resources rather than a replacement for them, and the two views can never disagree about what
 * is bookable. Placements likewise come from `buildTimeline`, which has already resolved a missing
 * end time and unwrapped opening hours that run past midnight.
 *
 * A unit is read against the slots on its own key *and* those on the units it shares space
 * with, so a party booked on a combined group occupies its member resources too. Drawing a member as
 * free while its group is occupied is the same room read two different ways.
 *
 * @see [serviceView.test.ts](../tests/utils/serviceView.test.ts) — pins that a group booking lands
 * on its group unit and occupies its member resources, that a member's own booking occupies its
 * group, that a unit with nothing booked reads free, and that sections keep their order.
 */
export function buildServiceFloor({
  rows,
  placements,
  at,
}: {
  rows: TimelineRowGroup[];
  placements: TimelinePlacement[];
  /** Observed moment, as minutes from the timeline's left edge. */
  at: number;
}): ServiceSection[] {
  const byUnit = new Map<string, TimelinePlacement[]>();
  for (const placement of placements) {
    const list = byUnit.get(placement.unitKey);
    if (list) list.push(placement);
    else byUnit.set(placement.unitKey, [placement]);
  }

  const shared = sharedPlacement(rows);
  const slotsOn = (key: string): TimelinePlacement[] => [
    ...(byUnit.get(key) ?? []),
    ...(shared.get(key) ?? []).flatMap((other) => byUnit.get(other) ?? []),
  ];

  return rows.map((row) => ({
    key: row.key,
    kind: row.kind,
    name: row.name,
    units: row.units.map((unit) => occupancyFor(unit, slotsOn(unit.key), at)),
  }));
}

/**
 * Floor totals at the observed moment. `guests` counts guests actually in session, not the day's bookings,
 * which is the number a headcount of the room is checked against.
 *
 * The statuses count resources, because resources are what there is a finite number of: a combinable group
 * is its member resources rather than a resource beside them, and the unassigned row is no resource at all.
 * Counting either as a unit of its own is what let an eight-resource room report ten free. Guests are
 * counted per slot instead, so a party booked on a group is one party however many units carry
 * it — and a party whose resource was deleted is still in the room and still on the total.
 *
 * @see [serviceView.test.ts](../tests/utils/serviceView.test.ts) — pins that a group and its members
 * count once between them, that the unassigned unit is no resource but its party still counts as guests, and
 * that the three statuses partition the room's resources.
 */
export function summarise(sections: ServiceSection[]): ServiceSummary {
  const summary: ServiceSummary = { inUse: 0, ending: 0, free: 0, guests: 0 };
  const counted = new Set<number>();

  for (const section of sections) {
    for (const occupancy of section.units) {
      if (occupancy.unit.kind === "resource") summary[occupancy.status] += 1;

      const slot = occupancy.current;
      if (slot && !counted.has(slot.booking.id)) {
        counted.add(slot.booking.id);
        summary.guests += slot.booking.partySize;
      }
    }
  }

  return summary;
}

/**
 * Splits a span of minutes into whole hours and the remaining minutes, so a caller can pick the
 * hours-and-minutes, hours-only or minutes-only phrasing its locale needs rather than having an
 * English "1h 30m" baked in here.
 *
 * @see [serviceView.test.ts](../tests/utils/serviceView.test.ts) — pins that a whole number of
 * hours leaves no remainder and that a negative span floors at zero.
 */
export function splitDuration(minutes: number): { hours: number; minutes: number } {
  const total = Math.max(0, Math.round(minutes));
  return { hours: Math.floor(total / 60), minutes: total % 60 };
}
