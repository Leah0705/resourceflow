import React from "react";
import { Modal } from "react-native";
import { act, render, screen, fireEvent, within } from "@testing-library/react-native";
import { ServiceView } from "@/components/admin/bookings/ServiceView";
import i18n from "@/i18n";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" }),
}));

jest.mock("@/utils/colors", () => ({
  hexToRgba: (_h: string, _a: number) => "rgba(0,0,0,0.1)",
}));

const DAY = "2026-08-23";

function booking(
  id: number,
  hhmm: string,
  minutes: number,
  overrides: Partial<Record<string, unknown>> = {}
) {
  const date = `${DAY}T${hhmm}:00.000Z`;
  return {
    id,
    resourceId: 101,
    resourceGroupId: null,
    date,
    endTime: new Date(new Date(date).getTime() + minutes * 60000).toISOString(),
    partySize: 2,
    customerEmail: "booked@test.com",
    ...overrides,
  };
}

const sections = [
  {
    id: 1,
    name: "Main",
    resources: [
      { id: 101, name: "Resource 1", capacity: 4 },
      { id: 102, name: "Resource 2", capacity: 2 },
    ],
  },
];

const props = {
  sections: sections as never,
  bookings: [booking(10, "18:00", 90)] as never,
  isDark: false,
  onBookingPress: jest.fn(),
  openTime: "17:00",
  closeTime: "23:00",
  timezone: "UTC",
  // Never the location's today, so the floor opens on the first slot and the clock cannot
  // walk these assertions off their times mid-run.
  gridDateIso: DAY,
};

/** Resource 1 and Resource 2 flagged as combinable, so both are members of one bookable group. */
const longResource = [
  {
    id: 5,
    name: "Long resource",
    combinedCapacity: 6,
    members: [{ id: 101, name: "Resource 1", capacity: 4 }],
  },
] as never;

const onTheLongResource = [
  booking(10, "18:00", 90, { resourceId: null, resourceGroupId: 5 }),
] as never;

/**
 * A 6pm slot on Resource 1 plus one on Resource 2 at `hhmm`. The floor opens on the earliest slot,
 * so the anchor fixes the observed moment at 6pm and lets Resource 2 be read before its party arrives.
 */
function anchoredAt(hhmm: string) {
  return [booking(10, "18:00", 90), booking(11, hhmm, 90, { resourceId: 102 })] as never;
}

describe("ServiceView", () => {
  beforeEach(() => jest.clearAllMocks());

  it("says so plainly when the location has no resources to lay out", () => {
    render(<ServiceView {...props} sections={[] as never} />);
    expect(screen.getByText(/No resources found/)).toBeTruthy();
  });

  it("lists every resource in the section, booked or not", () => {
    render(<ServiceView {...props} />);
    expect(screen.getByText("MAIN")).toBeTruthy();
    expect(screen.getByText("Resource 1")).toBeTruthy();
    expect(screen.getByText("Resource 2")).toBeTruthy();
  });

  it("opens on the first slot of a day that is not today, so the floor is not blank", () => {
    render(<ServiceView {...props} />);
    expect(screen.getByTestId("service-scrub-clock")).toHaveTextContent("6:00p");
  });

  it("names the participant, their party size and what is left of their slot", () => {
    render(<ServiceView {...props} />);

    expect(screen.getByText("booked")).toBeTruthy();
    expect(screen.getByText("6:00p – 7:30p")).toBeTruthy();
    expect(screen.getByText("1h 30m left")).toBeTruthy();
  });

  it("prefers the participant's name over their email when there is one", () => {
    render(
      <ServiceView
        {...props}
        bookings={[booking(10, "18:00", 90, { customerName: "Patel" })] as never}
      />
    );
    expect(screen.getByText("Patel")).toBeTruthy();
  });

  it("marks an occupied resource in use and an unbooked one free", () => {
    render(<ServiceView {...props} />);

    expect(
      within(screen.getByTestId("service-unit-resource:101")).getByText(/In use/)
    ).toBeTruthy();
    expect(within(screen.getByTestId("service-unit-resource:102")).getByText(/Free/)).toBeTruthy();
  });

  it("counts the guests actually in session at the shown moment", () => {
    render(
      <ServiceView
        {...props}
        bookings={
          [
            booking(10, "18:00", 90, { partySize: 4 }),
            booking(11, "21:00", 90, { partySize: 6, resourceId: 102 }),
          ] as never
        }
      />
    );
    expect(within(screen.getByTestId("service-guests")).getByText("4 participants")).toBeTruthy();
  });

  it("opens the booking when its resource is pressed", () => {
    render(<ServiceView {...props} />);

    fireEvent.press(screen.getByTestId("service-unit-resource:101"));

    expect(props.onBookingPress).toHaveBeenCalledWith(expect.objectContaining({ id: 10 }));
  });

  it("opens the slot still to come when a free resource is pressed", () => {
    render(<ServiceView {...props} bookings={anchoredAt("20:00")} />);

    fireEvent.press(screen.getByTestId("service-unit-resource:102"));

    expect(props.onBookingPress).toHaveBeenCalledWith(expect.objectContaining({ id: 11 }));
  });

  it("says nothing else is booked on a resource with no slots left", () => {
    render(<ServiceView {...props} />);
    expect(
      within(screen.getByTestId("service-unit-resource:102")).getByText(/Nothing else today/)
    ).toBeTruthy();
  });

  it("announces the next slot and how long the resource stays free", () => {
    render(<ServiceView {...props} bookings={anchoredAt("20:00")} />);
    expect(
      within(screen.getByTestId("service-unit-resource:102")).getByText(/Next 8:00p/)
    ).toBeTruthy();
  });

  // A resource with a party due imminently cannot be offered to a walk-in, so it is not "free".
  it("marks a resource ending when its next slot is inside the turnaround", () => {
    render(<ServiceView {...props} bookings={anchoredAt("18:30")} />);
    expect(
      within(screen.getByTestId("service-unit-resource:102")).getByText(/Ending/)
    ).toBeTruthy();
  });

  it("marks the same resource free when that slot is beyond the turnaround", () => {
    render(<ServiceView {...props} bookings={anchoredAt("18:45")} />);
    expect(within(screen.getByTestId("service-unit-resource:102")).getByText(/Free/)).toBeTruthy();
  });

  it("steps the shown time forward and back in quarter hours", () => {
    render(<ServiceView {...props} />);

    fireEvent.press(screen.getByTestId("service-scrub-forward"));
    expect(screen.getByTestId("service-scrub-clock")).toHaveTextContent("6:15p");

    fireEvent.press(screen.getByTestId("service-scrub-back"));
    expect(screen.getByTestId("service-scrub-clock")).toHaveTextContent("6:00p");
  });

  it("re-reads the floor at the scrubbed time, not the time it opened on", () => {
    render(<ServiceView {...props} bookings={[booking(10, "18:00", 30)] as never} />);

    expect(
      within(screen.getByTestId("service-unit-resource:101")).getByText(/In use/)
    ).toBeTruthy();

    fireEvent.press(screen.getByTestId("service-scrub-forward"));
    fireEvent.press(screen.getByTestId("service-scrub-forward"));
    fireEvent.press(screen.getByTestId("service-scrub-forward"));

    expect(within(screen.getByTestId("service-unit-resource:101")).getByText(/Free/)).toBeTruthy();
  });

  it("holds the scrubbed time at the edge of the day rather than running off it", () => {
    render(<ServiceView {...props} />);

    for (let i = 0; i < 40; i++) fireEvent.press(screen.getByTestId("service-scrub-back"));

    expect(screen.getByTestId("service-scrub-clock")).toHaveTextContent("5:00p");
  });

  it("offers no jump back to now on a day that has no now on it", () => {
    render(<ServiceView {...props} />);
    expect(screen.queryByTestId("service-scrub-now")).toBeNull();
  });

  it("gives a combinable group its own place on the floor beside its member resources", () => {
    render(<ServiceView {...props} groups={longResource} bookings={onTheLongResource} />);

    expect(within(screen.getByTestId("service-unit-group:5")).getByText(/In use/)).toBeTruthy();
    expect(screen.getByTestId("service-unit-resource:101")).toBeTruthy();
  });

  // Resource 1 is half of the long resource. A party assigned to the group is using it, and a card
  // reading "Free" over an occupied resource is the floor lying about the one thing it is for.
  it("draws the member resources of an occupied group as in use, not as free", () => {
    render(<ServiceView {...props} groups={longResource} bookings={onTheLongResource} />);

    expect(
      within(screen.getByTestId("service-unit-resource:101")).getByText(/In use/)
    ).toBeTruthy();
    expect(within(screen.getByTestId("service-unit-resource:102")).getByText(/Free/)).toBeTruthy();
  });

  it("counts the room's two resources once between the group and its members", () => {
    render(<ServiceView {...props} groups={longResource} bookings={[] as never} />);

    expect(screen.getByText("2 free")).toBeTruthy();
  });

  it("counts a party on the group once over, however many units carry it", () => {
    render(<ServiceView {...props} groups={longResource} bookings={onTheLongResource} />);

    expect(within(screen.getByTestId("service-guests")).getByText("2 participants")).toBeTruthy();
  });

  it("says so on a day with nothing booked at all", () => {
    render(<ServiceView {...props} bookings={[] as never} dateLabel="Sun 23 Aug" />);
    expect(screen.getByText("No bookings on Sun 23 Aug")).toBeTruthy();
  });

  it("falls back to a generic name for a participant with neither name nor email", () => {
    render(
      <ServiceView
        {...props}
        bookings={[booking(10, "18:00", 90, { customerName: null, customerEmail: null })] as never}
      />
    );
    expect(
      within(screen.getByTestId("service-unit-resource:101")).getByText("Participant")
    ).toBeTruthy();
  });

  it("fills the screen with the floor when maximised, and gives the page back on collapse", () => {
    render(<ServiceView {...props} />);

    expect(screen.queryByTestId("service-expanded")).toBeNull();

    fireEvent.press(screen.getByTestId("service-expand"));
    expect(screen.getByTestId("service-expanded")).toBeTruthy();

    fireEvent.press(screen.getByTestId("service-expand"));
    expect(screen.queryByTestId("service-expanded")).toBeNull();
  });

  it("gives the page back when the platform asks the sheet to close", () => {
    render(<ServiceView {...props} />);
    fireEvent.press(screen.getByTestId("service-expand"));

    fireEvent(screen.UNSAFE_getByType(Modal), "requestClose");

    expect(screen.queryByTestId("service-expanded")).toBeNull();
  });

  it("keeps the scrubbed time across maximising, so the floor does not jump", () => {
    render(<ServiceView {...props} />);

    fireEvent.press(screen.getByTestId("service-scrub-forward"));
    const scrubbed = screen.getByTestId("service-scrub-clock").props.children;

    fireEvent.press(screen.getByTestId("service-expand"));

    expect(screen.getByTestId("service-scrub-clock").props.children).toBe(scrubbed);
  });

  it("still lays the room out while maximised", () => {
    render(<ServiceView {...props} />);

    fireEvent.press(screen.getByTestId("service-expand"));

    expect(screen.getByText("Resource 1")).toBeTruthy();
    expect(
      within(screen.getByTestId("service-unit-resource:101")).getByText(/In use/)
    ).toBeTruthy();
  });

  it("names no day in the empty state when it was not given one", () => {
    render(<ServiceView {...props} bookings={[] as never} />);
    expect(screen.getByText("No bookings on this day")).toBeTruthy();
  });

  it("falls back to its own service hours when the location supplies none", () => {
    render(
      <ServiceView
        sections={props.sections}
        bookings={[] as never}
        isDark={false}
        onBookingPress={jest.fn()}
        gridDateIso={DAY}
      />
    );
    expect(screen.getByText("Resource 1")).toBeTruthy();
  });
});

describe("ServiceView on the location's today", () => {
  /** Today in the location's zone, which is the only day the floor has a "now" to follow. */
  const today = new Date().toISOString().slice(0, 10);

  function bookingToday(id: number, minutesFromNow: number, minutes: number, resourceId = 101) {
    const start = new Date(Date.now() + minutesFromNow * 60000);
    return {
      id,
      resourceId,
      resourceGroupId: null,
      date: start.toISOString(),
      endTime: new Date(start.getTime() + minutes * 60000).toISOString(),
      partySize: 2,
      customerEmail: "booked@test.com",
    };
  }

  const liveProps = {
    ...props,
    gridDateIso: today,
    openTime: "00:00",
    closeTime: "23:59",
    bookings: [bookingToday(20, -15, 90)] as never,
  };

  beforeEach(() => jest.clearAllMocks());

  it("follows the clock, so a party that started a quarter hour ago reads as in use", () => {
    render(<ServiceView {...liveProps} />);
    expect(
      within(screen.getByTestId("service-unit-resource:101")).getByText(/In use/)
    ).toBeTruthy();
  });

  it("offers a way back to now once the floor has been scrubbed off it", () => {
    render(<ServiceView {...liveProps} />);
    const clock = screen.getByTestId("service-scrub-clock").props.children;

    fireEvent.press(screen.getByTestId("service-scrub-forward"));
    expect(screen.getByTestId("service-scrub-clock").props.children).not.toBe(clock);

    fireEvent.press(screen.getByTestId("service-scrub-now"));
    expect(screen.getByTestId("service-scrub-clock").props.children).toBe(clock);
  });

  it("maps a position on the track to a time, so the far ends are different moments", () => {
    render(<ServiceView {...liveProps} />);
    const track = screen.getByTestId("service-scrub-track");
    const clock = () => screen.getByTestId("service-scrub-clock").props.children;

    fireEvent(track, "layout", { nativeEvent: { layout: { width: 240 } } });

    fireEvent(track, "responderGrant", { nativeEvent: { locationX: 0 } });
    const atStart = clock();

    fireEvent(track, "responderMove", { nativeEvent: { locationX: 240 } });
    expect(clock()).not.toBe(atStart);

    fireEvent(track, "responderMove", { nativeEvent: { locationX: 0 } });
    expect(clock()).toBe(atStart);
  });

  it("clamps a drag past either end of the track to the day it draws", () => {
    render(<ServiceView {...liveProps} />);
    const track = screen.getByTestId("service-scrub-track");
    const clock = () => screen.getByTestId("service-scrub-clock").props.children;

    fireEvent(track, "layout", { nativeEvent: { layout: { width: 240 } } });

    fireEvent(track, "responderGrant", { nativeEvent: { locationX: 0 } });
    const atStart = clock();
    fireEvent(track, "responderMove", { nativeEvent: { locationX: -400 } });

    expect(clock()).toBe(atStart);
  });

  it("ignores a drag before the track has been measured", () => {
    render(<ServiceView {...liveProps} />);
    const before = screen.getByTestId("service-scrub-clock").props.children;

    fireEvent(screen.getByTestId("service-scrub-track"), "responderGrant", {
      nativeEvent: { locationX: 100 },
    });

    expect(screen.getByTestId("service-scrub-clock").props.children).toBe(before);
  });
});

// The combined block is the floor's own wording rather than the admin's, so it follows the UI
// language like every other string on the screen.
describe("ServiceView in the reader's language", () => {
  afterEach(async () => {
    // The mounted tree re-renders on the language change, so the restore is a React update too.
    await act(() => i18n.changeLanguage("en"));
  });

  it("heads the combined block in French rather than in English", async () => {
    await act(() => i18n.changeLanguage("fr"));

    render(<ServiceView {...props} groups={longResource} bookings={onTheLongResource} />);

    expect(screen.getByText("RESSOURCES COMBINÉES")).toBeTruthy();
    expect(screen.queryByText("COMBINED RESOURCES")).toBeNull();
  });

  it("leaves the admin's own section name alone", async () => {
    await act(() => i18n.changeLanguage("fr"));

    render(<ServiceView {...props} />);

    expect(screen.getByText("MAIN")).toBeTruthy();
  });
});
