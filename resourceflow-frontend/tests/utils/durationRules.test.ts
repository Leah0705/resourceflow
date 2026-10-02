import { hasDuplicateDurationRules, durationRuleRanges } from "@/utils/durationRules";

describe("durationRuleRanges", () => {
  it("stops each rule one participant below the next", () => {
    expect(
      durationRuleRanges([
        { minPartySize: 3, minutes: 90 },
        { minPartySize: 1, minutes: 60 },
        { minPartySize: 5, minutes: 120 },
      ])
    ).toEqual([
      { minPartySize: 1, maxPartySize: 2, minutes: 60 },
      { minPartySize: 3, maxPartySize: 4, minutes: 90 },
      { minPartySize: 5, maxPartySize: null, minutes: 120 },
    ]);
  });

  it("leaves the largest rule open-ended", () => {
    expect(durationRuleRanges([{ minPartySize: 6, minutes: 150 }])).toEqual([
      { minPartySize: 6, maxPartySize: null, minutes: 150 },
    ]);
  });
});

describe("hasDuplicateDurationRules", () => {
  it("is true when two rules start at the same participant count", () => {
    expect(
      hasDuplicateDurationRules([
        { minPartySize: 3, minutes: 90 },
        { minPartySize: 3, minutes: 120 },
      ])
    ).toBe(true);
  });

  it("is false when every rule starts at its own participant count", () => {
    expect(
      hasDuplicateDurationRules([
        { minPartySize: 3, minutes: 90 },
        { minPartySize: 4, minutes: 120 },
      ])
    ).toBe(false);
  });
});
