import { canFit, capacityAtLeast } from "@/utils/placement";

describe("canFit", () => {
  // The pair is the point: the cap is a boundary, and only testing one side of it lets the
  // client drift off the server's rule without anything failing.
  it("accepts a unit exactly at the oversize cap", () => {
    expect(canFit(6, 4, 2)).toBe(true);
  });

  it("rejects a unit one place over the cap", () => {
    expect(canFit(7, 4, 2)).toBe(false);
  });

  it("rejects a unit smaller than the party", () => {
    expect(canFit(2, 4, null)).toBe(false);
  });

  it("accepts a unit of any size when no cap is set", () => {
    expect(canFit(10, 2, null)).toBe(true);
    expect(canFit(10, 2, undefined)).toBe(true);
  });

  it("accepts an exact fit under a cap of zero", () => {
    expect(canFit(4, 4, 0)).toBe(true);
    expect(canFit(5, 4, 0)).toBe(false);
  });
});

describe("capacityAtLeast", () => {
  const resources = [
    { id: 1, capacity: 2 },
    { id: 2, capacity: 4 },
    { id: 3, capacity: 6 },
    { id: 4, capacity: 8 },
  ];

  it("keeps only the units the party fits into, within the cap", () => {
    const eligible = capacityAtLeast(resources, 4, 2, (t) => t.capacity);
    expect(eligible.map((t) => t.id)).toEqual([2, 3]);
  });

  it("keeps every large-enough unit when no cap is set", () => {
    const eligible = capacityAtLeast(resources, 4, null, (t) => t.capacity);
    expect(eligible.map((t) => t.id)).toEqual([2, 3, 4]);
  });
});
