import { buildTimeline, buildUnitRows, clockMinutesAt } from "@/utils/bookingTimeline";
import {
  buildServiceFloor,
  splitDuration,
  summarise,
  TURNAROUND_MINUTES,
  type ServiceSection,
} from "@/utils/serviceView";
import type { BookingDetailDto, SectionWithResources } from "@/api/admin";
import type { ResourceGroupDto } from "@/api/venues";

const DAY = "2026-08-23";

function booking(
  id: number,
  hhmm: string,
  minutes: number,
  unit: { resourceId?: number | null; resourceGroupId?: number | null } = { resourceId: 1 },
  partySize = 2
): BookingDetailDto {
  const date = `${DAY}T${hhmm}:00.000Z`;
  return {
    id,
    venueId: 1,
    venueName: "Test",
    timezone: "UTC",
    sectionId: 1,
    sectionName: "Main",
    resourceId: unit.resourceId ?? null,
    resourceGroupId: unit.resourceGroupId ?? null,
    resourceName: "T1",
    date,
    endTime: new Date(new Date(date).getTime() + minutes * 60000).toISOString(),
    customerEmail: `guest${id}@test.com`,
    customerName: `Participant ${id}`,
    partySize,
  };
}

const sections: SectionWithResources[] = [
  {
    id: 1,
    name: "Main",
    resources: [
      { id: 1, name: "T1", capacity: 4 },
      { id: 2, name: "T2", capacity: 2 },
    ],
  },
];

const OPEN = "17:00";

/** T1 + T2 combined: the two member resources of the room's one combinable group. */
const LONG_RESOURCE: ResourceGroupDto = {
  id: 5,
  name: "Long resource",
  combinedCapacity: 6,
  members: [
    { id: 1, name: "T1", capacity: 4 },
    { id: 2, name: "T2", capacity: 2 },
  ],
};

/** The floor as it stands `hhmm` into the day, built the way the screen builds it. */
function floorAt(
  hhmm: string,
  bookings: BookingDetailDto[],
  {
    groups = [],
    includeUnassigned = false,
  }: { groups?: ResourceGroupDto[]; includeUnassigned?: boolean } = {}
): ServiceSection[] {
  const timeline = buildTimeline({
    openTime: OPEN,
    closeTime: "23:00",
    timezone: "UTC",
    bookings,
    defaultDurationMinutes: 90,
  });
  const [h, m] = hhmm.split(":").map(Number);
  // Offsets are measured from opening, so the observed clock time converts back the same way.
  const at = h * 60 + m - clockMinutesAt(OPEN, 0);
  return buildServiceFloor({
    rows: buildUnitRows(sections, groups, { includeUnassigned }),
    placements: timeline.placements,
    at,
  });
}

const unit = (floor: ServiceSection[], key: string) =>
  floor.flatMap((s) => s.units).find((u) => u.unit.key === key)!;

describe("buildServiceFloor", () => {
  it("reads a resource as in use at the minute its slot starts", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90)]);
    expect(unit(floor, "resource:1").status).toBe("inUse");
  });

  it("reads a resource as free at the minute its slot ends, not still in use", () => {
    const floor = floorAt("19:30", [booking(1, "18:00", 90)]);
    expect(unit(floor, "resource:1").status).toBe("free");
  });

  it("counts down what is left of the slot", () => {
    const floor = floorAt("18:20", [booking(1, "18:00", 90)]);
    expect(unit(floor, "resource:1").minutesRemaining).toBe(70);
  });

  it("names the participant slot there", () => {
    const floor = floorAt("18:20", [booking(1, "18:00", 90)]);
    expect(unit(floor, "resource:1").current?.booking.customerName).toBe("Participant 1");
  });

  it("reads a free resource with a slot due within the turnaround as ending", () => {
    const floor = floorAt("18:00", [
      booking(1, `18:${String(TURNAROUND_MINUTES).padStart(2, "0")}`, 90),
    ]);
    expect(unit(floor, "resource:1").status).toBe("ending");
  });

  it("reads a free resource whose next slot is past the turnaround as free", () => {
    const floor = floorAt("18:00", [
      booking(1, `18:${String(TURNAROUND_MINUTES + 1).padStart(2, "0")}`, 90),
    ]);
    expect(unit(floor, "resource:1").status).toBe("free");
  });

  it("reads a resource with nothing booked as free", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90)]);
    expect(unit(floor, "resource:2").status).toBe("free");
    expect(unit(floor, "resource:2").next).toBeNull();
  });

  it("counts down to the next slot while the resource is still occupied", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 60), booking(2, "19:30", 60)]);
    const t1 = unit(floor, "resource:1");
    expect(t1.status).toBe("inUse");
    expect(t1.minutesUntilNext).toBe(90);
  });

  it("places a group booking on its group unit, which carries no resource id to match on", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90, { resourceGroupId: 5 })], {
      groups: [LONG_RESOURCE],
    });
    expect(unit(floor, "group:5").status).toBe("inUse");
  });

  // Combining T1 and T2 does not conjure a third resource. A party on the group is using
  // both of them, so drawing either as free is the same room read two different ways.
  it("occupies the member resources of a group a party is assigned to", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90, { resourceGroupId: 5 })], {
      groups: [LONG_RESOURCE],
    });
    expect(unit(floor, "resource:1").status).toBe("inUse");
    expect(unit(floor, "resource:2").current?.booking.id).toBe(1);
  });

  it("occupies the group when a party is assigned to one of its member resources", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90, { resourceId: 1 })], {
      groups: [LONG_RESOURCE],
    });
    expect(unit(floor, "group:5").status).toBe("inUse");
  });

  it("leaves a group and its members free when the slot is on neither", () => {
    const floor = floorAt("18:00", [], { groups: [LONG_RESOURCE] });
    expect(unit(floor, "group:5").status).toBe("free");
    expect(unit(floor, "resource:1").status).toBe("free");
  });

  it("turns a member resource over when its group's next slot is inside the turnaround", () => {
    const floor = floorAt("18:00", [booking(1, "18:30", 90, { resourceGroupId: 5 })], {
      groups: [LONG_RESOURCE],
    });
    expect(unit(floor, "resource:1").status).toBe("ending");
  });

  it("keeps sections in the order the rows came in", () => {
    const floor = floorAt("18:00", []);
    expect(floor.map((s) => s.key)).toEqual(["section:1"]);
  });

  it("picks the slot covering the moment, not merely the first of the day", () => {
    const floor = floorAt("20:00", [booking(1, "18:00", 60), booking(2, "20:00", 60)]);
    expect(unit(floor, "resource:1").current?.booking.id).toBe(2);
  });
});

describe("summarise", () => {
  it("counts guests from the participants actually in session, not the day's bookings", () => {
    const floor = floorAt("18:00", [
      booking(1, "18:00", 90, { resourceId: 1 }, 4),
      booking(2, "21:00", 90, { resourceId: 2 }, 6),
    ]);
    expect(summarise(floor).guests).toBe(4);
  });

  it("partitions every unit across the three statuses", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90)]);
    const summary = summarise(floor);
    expect(summary.inUse + summary.ending + summary.free).toBe(2);
  });

  it("counts an empty floor as all free", () => {
    expect(summarise(floorAt("18:00", []))).toEqual({
      inUse: 0,
      ending: 0,
      free: 2,
      guests: 0,
    });
  });

  // The room has two resources however many ways they can be combined; a group counted as a
  // unit of its own is what let a two-resource room report three free.
  it("counts a combinable group as its member resources rather than as a unit beside them", () => {
    const summary = summarise(floorAt("18:00", [], { groups: [LONG_RESOURCE] }));
    expect(summary.free).toBe(2);
    expect(summary.inUse + summary.ending + summary.free).toBe(2);
  });

  it("counts a party assigned to a group against its member resources, once", () => {
    const summary = summarise(
      floorAt("18:00", [booking(1, "18:00", 90, { resourceGroupId: 5 }, 6)], {
        groups: [LONG_RESOURCE],
      })
    );
    expect(summary).toEqual({ inUse: 2, ending: 0, free: 0, guests: 6 });
  });

  // The unassigned row stands for a booking whose resource was deleted, not for a resource.
  it("counts the unassigned row as no resource on any of the three statuses", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90, { resourceId: null }, 3)], {
      includeUnassigned: true,
    });
    const summary = summarise(floor);
    expect(summary.inUse + summary.ending + summary.free).toBe(2);
    expect(summary.inUse).toBe(0);
  });

  it("still counts the party on the unassigned row among the guests in the room", () => {
    const floor = floorAt("18:00", [booking(1, "18:00", 90, { resourceId: null }, 3)], {
      includeUnassigned: true,
    });
    expect(summarise(floor).guests).toBe(3);
  });
});

describe("splitDuration", () => {
  it("leaves no remainder on a whole number of hours", () => {
    expect(splitDuration(120)).toEqual({ hours: 2, minutes: 0 });
  });

  it("splits an hour and a half into both parts", () => {
    expect(splitDuration(90)).toEqual({ hours: 1, minutes: 30 });
  });

  it("reports under an hour as minutes alone", () => {
    expect(splitDuration(45)).toEqual({ hours: 0, minutes: 45 });
  });

  it("floors a span that has already elapsed at zero rather than going negative", () => {
    expect(splitDuration(-10)).toEqual({ hours: 0, minutes: 0 });
  });
});
