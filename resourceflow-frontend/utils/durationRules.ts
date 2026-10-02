import type { DurationRuleDto } from "@/api/venues";

export interface DurationRuleRange {
  minPartySize: number;
  /** The largest party the rule covers, or null when it covers every larger party. */
  maxPartySize: number | null;
  minutes: number;
}

/**
 * Which party sizes each rule covers, mirroring `BookingDuration.For` on the server: a party gets
 * the rule with the largest `minPartySize` at or below its size, so a rule runs up to one below the
 * next. For display only; the server resolves the length a booking actually gets.
 *
 * @see [durationRules.test.ts](../tests/utils/durationRules.test.ts) — pins that a rule stops one guest
 * below the next and that the largest rule is open-ended.
 */
export function durationRuleRanges(rules: DurationRuleDto[]): DurationRuleRange[] {
  const sorted = [...rules].sort((a, b) => a.minPartySize - b.minPartySize);
  return sorted.map((rule, i) => ({
    minPartySize: rule.minPartySize,
    maxPartySize: i + 1 < sorted.length ? sorted[i + 1].minPartySize - 1 : null,
    minutes: rule.minutes,
  }));
}

/** True when two rules start at the same party size, which the server rejects. */
export function hasDuplicateDurationRules(rules: DurationRuleDto[]): boolean {
  return new Set(rules.map((r) => r.minPartySize)).size !== rules.length;
}
