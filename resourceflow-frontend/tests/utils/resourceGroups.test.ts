import {
  groupDisplayName,
  groupDropdownLabel,
  groupedResourceIds,
  ResourceGroupLike,
} from "@/utils/resourceGroups";

const named: ResourceGroupLike = {
  name: "Window desks",
  combinedCapacity: 5,
  members: [
    { id: 1, name: "T1" },
    { id: 2, name: "T2" },
  ],
};

const unnamed: ResourceGroupLike = {
  combinedCapacity: 4,
  members: [
    { id: 4, name: "B1" },
    { id: 5, name: "B2" },
  ],
};

describe("groupDisplayName", () => {
  it("uses the admin-assigned name when present, with no capacity suffix", () => {
    expect(groupDisplayName(named)).toBe("Window desks");
  });

  it("falls back to the member resource names", () => {
    expect(groupDisplayName(unnamed)).toBe("Resources B1 + B2");
  });

  it("falls back to the resource id for an unnamed member resource", () => {
    expect(
      groupDisplayName({ combinedCapacity: 4, members: [{ id: 7 }, { id: 8, name: "B2" }] })
    ).toBe("Resources 7 + B2");
  });
});

describe("groupDropdownLabel", () => {
  it("appends the capacity to a named group", () => {
    expect(groupDropdownLabel(named)).toBe("Window desks (5 places)");
  });

  it("spells out the members for an unnamed group", () => {
    expect(groupDropdownLabel(unnamed)).toBe("Resources B1 + B2 (4 places combined)");
  });
});

describe("groupedResourceIds", () => {
  it("collects every member id across groups", () => {
    expect(groupedResourceIds([named, unnamed])).toEqual(new Set([1, 2, 4, 5]));
  });

  it("returns an empty set when there are no groups", () => {
    expect(groupedResourceIds([])).toEqual(new Set());
  });

  it("de-duplicates ids", () => {
    expect(groupedResourceIds([named, named])).toEqual(new Set([1, 2]));
  });
});
