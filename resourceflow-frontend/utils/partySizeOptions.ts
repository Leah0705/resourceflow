/**
 * Capacity dropdown options shared by every party-size/capacity picker (admin resource editor, add-resource
 * form, combinable-group combined-capacity editor). Matches the backend BookingLimits bounds so the
 * frontend can never offer a value the server will reject — see ResourceFlowApi BookingLimits.cs.
 */
export const MIN_PARTY_SIZE = 1;
export const MAX_PARTY_SIZE = 50;

export interface PartySizeOption {
  label: string;
  value: number;
}

/**
 * Build capacity options from `min` (inclusive) to `max` (inclusive). Defaults to the global
 * [MIN_PARTY_SIZE, MAX_PARTY_SIZE] range; callers can narrow it (e.g. a group's combined-capacity editor may
 * want a floor above the sum of member capacities).
 */
export function buildPartySizeOptions(
  min: number = MIN_PARTY_SIZE,
  max: number = MAX_PARTY_SIZE
): PartySizeOption[] {
  const options: PartySizeOption[] = [];
  for (let i = min; i <= max; i++) {
    options.push({ label: `${i} place${i === 1 ? "" : "s"}`, value: i });
  }
  return options;
}
