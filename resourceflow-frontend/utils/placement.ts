/**
 * Which bookable units can take a party — the client's copy of the server's `Venue.CanFit`.
 * Every dropdown, suggested pick and eligibility list runs through this, because a unit offered
 * here that the API then rejects reads to the guest as a broken booking form rather than as a
 * capacity rule.
 *
 * @see [placement.test.ts](../tests/utils/placement.test.ts) — pins the boundary on both sides of the
 * oversize cap, and that no cap means no upper bound.
 */

/** A location's optional cap on spare capacity over the party size. Null means unrestricted. */
export type OversizeCap = number | null | undefined;

export function canFit(unitCapacity: number, partySize: number, cap: OversizeCap): boolean {
  if (unitCapacity < partySize) return false;
  return cap == null || unitCapacity - partySize <= cap;
}

export function capacityAtLeast<T>(
  units: T[],
  partySize: number,
  cap: OversizeCap,
  capacityOf: (unit: T) => number
): T[] {
  return units.filter((unit) => canFit(capacityOf(unit), partySize, cap));
}
