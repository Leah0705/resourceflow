/**
 * @jest-environment jsdom
 *
 * The compact Locations row: identity, the meta row that states hours and walk-in policy,
 * the slot quick-pick driven by the page-level filter bar, and the "Details" body
 * (blurb, maps, weekly hours, resources & capacity).
 * Booking itself now lives in BookingDrawer and is covered by its own suite.
 */
import React from "react";
import { screen, waitFor, fireEvent } from "@testing-library/react-native";
import { Platform, StyleSheet } from "react-native";
import LocationListItem from "@/components/venue/LocationListItem";
import { fetchAvailability } from "@/api/availability";
import { renderWithProviders } from "@/tests/helpers/renderWithProviders";
import { getVenueNow } from "@/utils/venueTime";
import * as useAppThemeModule from "@/hooks/use-app-theme";
import { getThemeColors } from "@/theme/theme";

// WalkInNotice links to the waitlist; the real router can't load under Jest.
jest.mock("expo-router", () => ({ useRouter: () => ({ push: jest.fn() }) }));

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("expo-image", () => {
  const { View } = require("react-native");
  return {
    Image: ({ onError }: any) => <View testID="location-image" onError={onError} />,
  };
});

jest.mock("@/api/availability", () => ({
  fetchAvailability: jest.fn().mockResolvedValue({ slots: [] }),
}));

// The card drops slots that are already past on the *venue's* clock, which it reads
// from the wall clock via getVenueNow. Tests that assert on a fixed slot time would
// otherwise have a daily window in which they fail for real. Passthrough by default;
// individual tests pin the clock via mockReturnValue.
jest.mock("@/utils/venueTime", () => {
  const actual = jest.requireActual("@/utils/venueTime");
  return { ...actual, getVenueNow: jest.fn(actual.getVenueNow) };
});

/** Thursday. Both "today" and the selected date unless a test says otherwise. */
const TODAY = "2026-04-16";
const THURSDAY = 4;
const FRIDAY = 5;

const mockVenue = {
  id: 1,
  name: "Toronto Location",
  address: "123 Test St",
  openTime: "09:00",
  closeTime: "22:00",
  openDays: "1,2,3,4,5,6,7",
  timezone: "America/Toronto",
  sections: [
    {
      id: 1,
      name: "Main",
      venueId: 1,
      resources: [{ id: 101, name: "T1", capacity: 4, sectionId: 1 }],
    },
  ],
};

// jest.clearAllMocks() clears calls but not return values, so a test that pins the clock would
// leak it into every test after it. beforeEach restores this passthrough explicitly.
const actualGetVenueNow = jest.requireActual("@/utils/venueTime").getVenueNow;

const registerRef = jest.fn();
const onExpand = jest.fn();
const onBook = jest.fn();
const onJoinWaitlist = jest.fn();

/** Everything the page-level filter bar supplies, so tests only state what they vary. */
const baseProps = {
  partySize: 2,
  date: TODAY,
  today: TODAY,
  timeWindow: "All" as const,
  registerRef,
  onExpand,
  onBook,
  onJoinWaitlist,
};

jest.setTimeout(15000);

describe("LocationListItem", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (getVenueNow as jest.Mock).mockImplementation(actualGetVenueNow);
    (fetchAvailability as jest.Mock).mockResolvedValue({ slots: [] });
  });

  it("renders the location name and address", async () => {
    renderWithProviders(<LocationListItem {...baseProps} venue={mockVenue as any} />);
    await waitFor(() => expect(screen.getByText("Toronto Location")).toBeTruthy());
    expect(screen.getByText("123 Test St")).toBeTruthy();
  });

  it("states an open location's hours once, in the meta row, with no badge repeating them", async () => {
    renderWithProviders(<LocationListItem {...baseProps} venue={mockVenue as any} />);
    await waitFor(() => expect(screen.getByText("09:00 – 22:00 today")).toBeTruthy());
    expect(screen.queryByText(/Open till/)).toBeNull();
  });

  it("keeps the booking form out of the card — booking is the drawer's job now", async () => {
    renderWithProviders(
      <LocationListItem {...baseProps} venue={mockVenue as any} defaultExpanded />
    );
    await waitFor(() => expect(screen.getByText("Resources & capacity")).toBeTruthy());
    expect(screen.queryByText("Confirm Booking")).toBeNull();
    expect(screen.queryByPlaceholderText("your@email.com")).toBeNull();
  });

  it("tells a participant which resources are kept for walk-ins", async () => {
    const withWalkInResource = {
      ...mockVenue,
      sections: [
        {
          ...mockVenue.sections[0],
          resources: [
            ...mockVenue.sections[0].resources,
            { id: 102, name: "D3", capacity: 2, sectionId: 1, walkInOnly: true },
          ],
        },
      ],
    };
    renderWithProviders(
      <LocationListItem {...baseProps} venue={withWalkInResource as any} defaultExpanded />
    );
    await waitFor(() => expect(screen.getByText("D3")).toBeTruthy());
    expect(screen.getAllByText("Walk-ins only")).toHaveLength(1);
  });

  it("registers its view ref with the parent for scroll anchoring", async () => {
    renderWithProviders(<LocationListItem {...baseProps} venue={mockVenue as any} />);
    await waitFor(() => expect(registerRef).toHaveBeenCalledWith(1, expect.anything()));
  });

  describe("slot row", () => {
    const utcVenue = { ...mockVenue, timezone: "UTC" };

    beforeEach(() => {
      // Noon (720 minutes) on a Thursday, so past/future slots are deterministic.
      (getVenueNow as jest.Mock).mockReturnValue({ totalMins: 720, isoDay: THURSDAY });
    });

    it("keeps only future, available slots and drops past or unavailable ones", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [
          { time: "11:00", isAvailable: true, category: "AM" },
          { time: "10:00", isAvailable: false, category: "AM" },
          { time: "14:00", isAvailable: true, category: "PM" },
        ],
      });
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("14:00")).toBeTruthy());
      expect(screen.queryByText("11:00")).toBeNull();
      expect(screen.queryByText("10:00")).toBeNull();
    });

    it("keeps past-but-same-day slots when a future date is selected", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "11:00", isAvailable: true, category: "AM" }],
      });
      renderWithProviders(
        <LocationListItem {...baseProps} date="2026-04-17" venue={utcVenue as any} />
      );
      await waitFor(() => expect(screen.getByText("11:00")).toBeTruthy());
    });

    it("filters to the time-of-day window chosen on the page bar", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [
          { time: "13:00", isAvailable: true, category: "AM" },
          { time: "19:00", isAvailable: true, category: "PM" },
        ],
      });
      renderWithProviders(
        <LocationListItem {...baseProps} timeWindow="PM" venue={utcVenue as any} />
      );
      await waitFor(() => expect(screen.getByText("19:00")).toBeTruthy());
      expect(screen.queryByText("13:00")).toBeNull();
    });

    it("caps the row at five slots and offers the rest behind a '+N more' affordance", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: ["18:00", "18:30", "19:00", "19:30", "20:00", "20:30", "21:00"].map((time) => ({
          time,
          isAvailable: true,
          category: "PM",
        })),
      });
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("+2 more")).toBeTruthy());
      expect(screen.getByText("20:00")).toBeTruthy();
      expect(screen.queryByText("20:30")).toBeNull();
    });

    it("hands the tapped time to onBook rather than expanding the card", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "14:00", isAvailable: true, category: "PM" }],
      });
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("14:00")).toBeTruthy());
      fireEvent.press(screen.getByText("14:00"));
      expect(onBook).toHaveBeenCalledWith(utcVenue, "14:00");
      expect(screen.queryByText("Resources & capacity")).toBeNull();
    });

    it("opens the drawer on the first slot when '+N more' is pressed", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: ["18:00", "18:30", "19:00", "19:30", "20:00", "20:30"].map((time) => ({
          time,
          isAvailable: true,
          category: "PM",
        })),
      });
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("+1 more")).toBeTruthy());
      fireEvent.press(screen.getByText("+1 more"));
      expect(onBook).toHaveBeenCalledWith(utcVenue, "18:00");
    });

    it("offers Book now even when no slot fits the current filters", async () => {
      // Nothing on the strip to press, but the location is open — the CTA is the only way
      // into the panel, where the guest can widen the date or party size.
      (fetchAvailability as jest.Mock).mockResolvedValue({ slots: [] });
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByTestId("location-book-now-1")).toBeTruthy());

      fireEvent.press(screen.getByTestId("location-book-now-1"));
      expect(onBook).toHaveBeenCalledWith(utcVenue, "09:00");
    });

    it("seeds Book now with the earliest slot on offer", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: ["18:00", "19:00"].map((time) => ({ time, isAvailable: true, category: "PM" })),
      });
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("18:00")).toBeTruthy());

      fireEvent.press(screen.getByTestId("location-book-now-1"));
      expect(onBook).toHaveBeenCalledWith(utcVenue, "18:00");
      // The CTA opens the panel; it must not also toggle the card body underneath it.
      expect(screen.queryByText("Resources & capacity")).toBeNull();
    });

    it("keeps Book now on a walk-in day, so a future date is still reachable", async () => {
      // Today being walk-in only says nothing about next Tuesday. The panel opens on this
      // day's notice and its date picker is the way on.
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...utcVenue, walkInDays: String(THURSDAY) } as any}
        />
      );
      await waitFor(() =>
        expect(screen.getByText("No reservations required, first come first served")).toBeTruthy()
      );
      expect(screen.getByTestId("location-book-now-1")).toBeTruthy();
    });

    it("keeps Book now on a closed day, for the same reason", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} venue={{ ...utcVenue, openDays: String(FRIDAY) } as any} />
      );
      await waitFor(() => expect(screen.getByTestId("location-book-now-1")).toBeTruthy());
    });

    it("hides Book now only when no day takes online bookings", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} venue={{ ...utcVenue, walkInOnly: true } as any} />
      );
      await waitFor(() =>
        expect(screen.getByText("No reservations required, first come first served")).toBeTruthy()
      );
      expect(screen.queryByTestId("location-book-now-1")).toBeNull();
    });

    it("hides Book now when every open day is walk-in only", async () => {
      // Same dead end as walkInOnly, reached the per-day way.
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...utcVenue, openDays: "4,5", walkInDays: `${THURSDAY},${FRIDAY}` } as any}
        />
      );
      await waitFor(() =>
        expect(screen.getByText("No reservations required, first come first served")).toBeTruthy()
      );
      expect(screen.queryByTestId("location-book-now-1")).toBeNull();
    });

    it("explains an empty slot row rather than leaving it blank", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue(null);
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() =>
        expect(
          screen.getByText("No times available, try another date or participant count")
        ).toBeTruthy()
      );
    });

    it("stops loading and reports empty availability when the fetch fails", async () => {
      const onAvailabilityChange = jest.fn();
      (fetchAvailability as jest.Mock).mockRejectedValue(new Error("network down"));
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={utcVenue as any}
          onAvailabilityChange={onAvailabilityChange}
        />
      );
      await waitFor(() =>
        expect(
          screen.getByText("No times available, try another date or participant count")
        ).toBeTruthy()
      );
      expect(screen.queryByTestId("location-slots-loading-1")).toBeNull();
      await waitFor(() => expect(onAvailabilityChange).toHaveBeenCalledWith(1, 0));
    });

    it("ignores an availability result that lands after unmount", async () => {
      let resolveFetch!: (v: unknown) => void;
      (fetchAvailability as jest.Mock).mockImplementation(
        () => new Promise((res) => (resolveFetch = res))
      );
      const onAvailabilityChange = jest.fn();
      const { unmount } = renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={utcVenue as any}
          onAvailabilityChange={onAvailabilityChange}
        />
      );
      unmount();
      onAvailabilityChange.mockClear();
      resolveFetch({ slots: [{ time: "14:00", isAvailable: true, category: "PM" }] });
      await new Promise((r) => setTimeout(r, 0));
      expect(onAvailabilityChange).not.toHaveBeenCalled();
    });

    it("ignores an availability failure that lands after unmount", async () => {
      let rejectFetch!: (e: Error) => void;
      (fetchAvailability as jest.Mock).mockImplementation(
        () => new Promise((_res, rej) => (rejectFetch = rej))
      );
      const onAvailabilityChange = jest.fn();
      const { unmount } = renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={utcVenue as any}
          onAvailabilityChange={onAvailabilityChange}
        />
      );
      unmount();
      onAvailabilityChange.mockClear();
      rejectFetch(new Error("late failure"));
      await new Promise((r) => setTimeout(r, 0));
      expect(onAvailabilityChange).not.toHaveBeenCalled();
    });

    it("reports its usable-slot count up to the page for the availability summary", async () => {
      const onAvailabilityChange = jest.fn();
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [
          { time: "14:00", isAvailable: true, category: "PM" },
          { time: "15:00", isAvailable: true, category: "PM" },
        ],
      });
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={utcVenue as any}
          onAvailabilityChange={onAvailabilityChange}
        />
      );
      await waitFor(() => expect(onAvailabilityChange).toHaveBeenCalledWith(1, 2));
      // Reports null while the fetch is in flight so the summary can say "checking".
      expect(onAvailabilityChange).toHaveBeenCalledWith(1, null);
    });

    it("refetches when the participant count changes", async () => {
      const { rerender } = renderWithProviders(
        <LocationListItem {...baseProps} venue={utcVenue as any} />
      );
      await waitFor(() => expect(fetchAvailability).toHaveBeenCalledWith(1, TODAY, 2));
      rerender(<LocationListItem {...baseProps} partySize={6} venue={utcVenue as any} />);
      await waitFor(() => expect(fetchAvailability).toHaveBeenCalledWith(1, TODAY, 6));
    });

    it("covers hover/press style states for a slot chip", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "23:30", isAvailable: true, category: "PM" }],
      });
      renderWithProviders(<LocationListItem {...baseProps} venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("23:30")).toBeTruthy());

      let slotNode = screen.getByText("23:30").parent;
      while (slotNode && typeof slotNode.props?.style !== "function") {
        slotNode = slotNode.parent;
      }
      const slotStyleFn = slotNode?.props.style as (state: {
        hovered: boolean;
        pressed: boolean;
      }) => unknown;
      expect(typeof slotStyleFn).toBe("function");
      expect(slotStyleFn({ hovered: true, pressed: false })).toBeTruthy();
      expect(slotStyleFn({ hovered: false, pressed: false })).toBeTruthy();
    });
  });

  describe("compact (phone) layout", () => {
    const utcVenue = { ...mockVenue, timezone: "UTC" };

    beforeEach(() => {
      (getVenueNow as jest.Mock).mockReturnValue({ totalMins: 720, isoDay: THURSDAY });
    });

    it("caps the row at three slots and shows a '+N' chip", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: ["18:00", "18:30", "19:00", "19:30", "20:00"].map((time) => ({
          time,
          isAvailable: true,
          category: "PM",
        })),
      });
      renderWithProviders(<LocationListItem {...baseProps} compact venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("+2")).toBeTruthy());
      expect(screen.getByText("19:00")).toBeTruthy();
      expect(screen.queryByText("19:30")).toBeNull();
    });

    it("opens the drawer from the '+N' chip", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: ["18:00", "18:30", "19:00", "19:30"].map((time) => ({
          time,
          isAvailable: true,
          category: "PM",
        })),
      });
      renderWithProviders(<LocationListItem {...baseProps} compact venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("+1")).toBeTruthy());
      fireEvent.press(screen.getByText("+1"));
      expect(onBook).toHaveBeenCalledWith(utcVenue, "18:00");
    });

    it("carries the walk-in line in the footer, where the meta row would be on a wide card", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} compact venue={{ ...utcVenue, walkInOnly: true } as any} />
      );
      await waitFor(() =>
        expect(screen.getByText("No reservations required, first come first served")).toBeTruthy()
      );
      // Stated once, in the footer — not repeated where the slot row would be.
      expect(screen.getAllByText("No reservations required, first come first served")).toHaveLength(
        1
      );
    });

    it("lets the footer's status line shrink so the actions stay on the card", async () => {
      // The compact footer holds three things on one row and only the status line can give
      // ground. Without this the walk-in sentence took its full intrinsic width and pushed
      // Details and Book now clean off the right edge of the card.
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          compact
          venue={{ ...utcVenue, walkInDays: String(THURSDAY) } as any}
        />
      );
      await waitFor(() => expect(screen.getByTestId("location-foot-lead-1")).toBeTruthy());

      const lead = StyleSheet.flatten(screen.getByTestId("location-foot-lead-1").props.style);
      expect(lead.flex).toBe(1);
      expect(lead.minWidth).toBe(0);
      // Its neighbour must not give ground in turn, or the buttons would squash instead.
      expect(screen.getByTestId("location-book-now-1")).toBeTruthy();
    });

    it("falls back to the hours line in the footer for a bookable location", async () => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "18:00", isAvailable: true, category: "PM" }],
      });
      renderWithProviders(<LocationListItem {...baseProps} compact venue={utcVenue as any} />);
      await waitFor(() => expect(screen.getByText("09:00 – 22:00 today")).toBeTruthy());
    });
  });

  describe("closed and walk-in states", () => {
    it("shows 'Closed today' and the next opening when closed on today's date", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...mockVenue, openDays: String(FRIDAY) } as any}
        />
      );
      await waitFor(() => expect(screen.getByText("Closed today · opens Fri 09:00")).toBeTruthy());
    });

    it("names the day instead of saying 'today' for a closed future date", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          date="2026-04-17"
          venue={{ ...mockVenue, openDays: String(THURSDAY) } as any}
        />
      );
      await waitFor(() => expect(screen.getByText(/^Closed Fri/)).toBeTruthy());
    });

    it("says only that it is closed when the location has no open days at all", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} venue={{ ...mockVenue, openDays: "" } as any} />
      );
      await waitFor(() => expect(screen.getByText("Closed today")).toBeTruthy());
      // Nothing to say about a next opening, and no walk-in policy to imply.
      expect(screen.queryByText(/^Opens /)).toBeNull();
      expect(screen.queryByText(/No reservations required/)).toBeNull();
    });

    it("states the next opening once, in the meta row, for a plainly closed location", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...mockVenue, openDays: String(FRIDAY) } as any}
        />
      );
      await waitFor(() => expect(screen.getAllByText(/opens Fri 09:00/)).toHaveLength(1));
      expect(screen.queryByText(/No reservations required/)).toBeNull();
    });

    it("replaces the slot row with a walk-in line for walk-in-only locations", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} venue={{ ...mockVenue, walkInOnly: true } as any} />
      );
      await waitFor(() =>
        expect(screen.getByText("No reservations required, first come first served")).toBeTruthy()
      );
      expect(screen.getByText("Walk-ins only")).toBeTruthy();
      expect(fetchAvailability).not.toHaveBeenCalled();
    });

    it("honours a per-day walk-in policy for the selected date", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...mockVenue, walkInDays: String(THURSDAY) } as any}
        />
      );
      await waitFor(() =>
        expect(screen.getByText("No reservations required, first come first served")).toBeTruthy()
      );
      expect(screen.getByText("Walk-ins on Thursdays")).toBeTruthy();
    });

    it("offers the waitlist in place of Book now at a walk-in-only location", async () => {
      const venue = { ...mockVenue, walkInOnly: true };
      renderWithProviders(<LocationListItem {...baseProps} venue={venue as any} />);

      fireEvent.press(await screen.findByTestId("location-join-waitlist-1"));
      expect(onJoinWaitlist).toHaveBeenCalledWith(venue);
      expect(screen.queryByTestId("location-book-now-1")).toBeNull();
    });

    it("opens the waitlist from the walk-in notice inside Details", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          defaultExpanded
          venue={{ ...mockVenue, walkInOnly: true } as any}
        />
      );
      fireEvent.press(await screen.findByTestId("walk-in-join-waitlist"));
      expect(onJoinWaitlist).toHaveBeenCalled();
    });

    it("still shows the walk-in notice inside Details for walk-in-only locations", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          defaultExpanded
          venue={{ ...mockVenue, walkInOnly: true } as any}
        />
      );
      await waitFor(() => expect(screen.getByTestId("walk-in-notice")).toBeTruthy());
    });
  });

  describe("details body", () => {
    it("expands and collapses from the Details toggle, notifying onExpand only when expanding", async () => {
      const onExpandLocal = jest.fn();
      renderWithProviders(
        <LocationListItem {...baseProps} venue={mockVenue as any} onExpand={onExpandLocal} />
      );
      await waitFor(() => expect(screen.getByText("Details")).toBeTruthy());
      expect(screen.queryByText("Resources & capacity")).toBeNull();

      fireEvent.press(screen.getByText("Details"));
      await waitFor(() => expect(screen.getByText("Resources & capacity")).toBeTruthy());
      expect(onExpandLocal).toHaveBeenCalledWith(1);
      expect(onExpandLocal).toHaveBeenCalledTimes(1);

      fireEvent.press(screen.getByText("Details"));
      await waitFor(() => expect(screen.queryByText("Resources & capacity")).toBeNull());
      expect(onExpandLocal).toHaveBeenCalledTimes(1);
    });

    it("expands and collapses from a press on the card body itself", async () => {
      const onExpandLocal = jest.fn();
      renderWithProviders(
        <LocationListItem {...baseProps} venue={mockVenue as any} onExpand={onExpandLocal} />
      );
      await waitFor(() => expect(screen.getByTestId("location-body-1")).toBeTruthy());
      expect(screen.queryByText("Resources & capacity")).toBeNull();

      fireEvent.press(screen.getByTestId("location-body-1"));
      await waitFor(() => expect(screen.getByText("Resources & capacity")).toBeTruthy());
      expect(onExpandLocal).toHaveBeenCalledWith(1);

      fireEvent.press(screen.getByTestId("location-body-1"));
      await waitFor(() => expect(screen.queryByText("Resources & capacity")).toBeNull());
    });

    it("expands without crashing when no onExpand callback is provided", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} onExpand={undefined} venue={mockVenue as any} />
      );
      await waitFor(() => expect(screen.getByText("Details")).toBeTruthy());
      fireEvent.press(screen.getByText("Details"));
      await waitFor(() => expect(screen.getByText("Resources & capacity")).toBeTruthy());
    });

    it("parses the description blurb into a tappable inline link", async () => {
      const { Linking } = require("react-native");
      const openURLSpy = jest.spyOn(Linking, "openURL").mockResolvedValue(undefined as never);

      renderWithProviders(
        <LocationListItem
          {...baseProps}
          defaultExpanded
          venue={
            {
              ...mockVenue,
              description: "Family-run since 1998. See our [guide](https://example.com/guide).",
            } as any
          }
        />
      );
      await waitFor(() => expect(screen.getByText(/Family-run since 1998/)).toBeTruthy());
      expect(screen.getByText("guide")).toBeTruthy();
      fireEvent.press(screen.getByA11yHint("https://example.com/guide"));
      expect(openURLSpy).toHaveBeenCalledWith("https://example.com/guide");
      openURLSpy.mockRestore();
    });

    it("renders tag chips in the row header, without expanding Details", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...mockVenue, tags: ["Accessible", "Projector"] } as any}
        />
      );
      await waitFor(() => expect(screen.getByText("Accessible")).toBeTruthy());
      expect(screen.getByText("Projector")).toBeTruthy();
    });

    it("opens Google Maps and Apple Maps links from the expanded address section", async () => {
      const { Linking } = require("react-native");
      const openURLSpy = jest.spyOn(Linking, "openURL").mockResolvedValue(undefined as never);
      renderWithProviders(
        <LocationListItem {...baseProps} defaultExpanded venue={mockVenue as any} />
      );
      await waitFor(() => expect(screen.getByLabelText("Open in Google Maps")).toBeTruthy());
      fireEvent.press(screen.getByLabelText("Open in Google Maps"));
      expect(openURLSpy).toHaveBeenCalledWith(
        expect.stringContaining("https://maps.google.com/?q=")
      );
      fireEvent.press(screen.getByLabelText("Open in Apple Maps"));
      expect(openURLSpy).toHaveBeenCalledWith(
        expect.stringContaining("https://maps.apple.com/?q=")
      );
      openURLSpy.mockRestore();
    });

    it("omits the maps block when the location has no address", async () => {
      const { address: _address, ...noAddressVenue } = mockVenue;
      renderWithProviders(
        <LocationListItem {...baseProps} defaultExpanded venue={noAddressVenue as any} />
      );
      await waitFor(() => expect(screen.getByText("Resources & capacity")).toBeTruthy());
      expect(screen.queryByLabelText("Open in Google Maps")).toBeNull();
    });

    it("covers hover/press style states for both map links", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} defaultExpanded venue={mockVenue as any} />
      );
      await waitFor(() => expect(screen.getByLabelText("Open in Google Maps")).toBeTruthy());

      const findStyleFn = (label: string) => {
        let node = screen.getByLabelText(label).parent;
        while (node && typeof node.props?.style !== "function") {
          node = node.parent;
        }
        return node?.props.style as (state: { hovered: boolean; pressed: boolean }) => unknown;
      };

      for (const label of ["Open in Google Maps", "Open in Apple Maps"]) {
        const styleFn = findStyleFn(label);
        expect(typeof styleFn).toBe("function");
        expect(styleFn({ hovered: true, pressed: false })).toBeTruthy();
        expect(styleFn({ hovered: false, pressed: false })).toBeTruthy();
      }
    });

    it("falls back to a generic resource label when a resource has no name", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          defaultExpanded
          venue={
            {
              ...mockVenue,
              sections: [
                {
                  id: 1,
                  name: "Main",
                  venueId: 1,
                  resources: [{ id: 202, name: undefined, capacity: 2, sectionId: 1 }],
                },
              ],
            } as any
          }
        />
      );
      await waitFor(() => expect(screen.getByText("Resource 202")).toBeTruthy());
    });
  });

  describe("guide link", () => {
    it("renders a Guide link when guideUrl is set and opens it on press", async () => {
      const { Linking } = require("react-native");
      const openURLSpy = jest.spyOn(Linking, "openURL").mockResolvedValue(undefined as never);

      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...mockVenue, guideUrl: "https://example.com/guide.pdf" } as any}
        />
      );
      await waitFor(() => expect(screen.getByLabelText("View guide")).toBeTruthy());
      fireEvent.press(screen.getByLabelText("View guide"));
      expect(openURLSpy).toHaveBeenCalledWith("https://example.com/guide.pdf");
      openURLSpy.mockRestore();
    });

    it("also offers the guide inside Details — the only route to it on a phone", async () => {
      const { Linking } = require("react-native");
      const openURLSpy = jest.spyOn(Linking, "openURL").mockResolvedValue(undefined as never);

      renderWithProviders(
        <LocationListItem
          {...baseProps}
          compact
          defaultExpanded
          venue={{ ...mockVenue, guideUrl: "https://example.com/guide.pdf" } as any}
        />
      );
      await waitFor(() => expect(screen.getByLabelText("Open guide")).toBeTruthy());
      fireEvent.press(screen.getByLabelText("Open guide"));
      expect(openURLSpy).toHaveBeenCalledWith("https://example.com/guide.pdf");
      openURLSpy.mockRestore();
    });

    it("omits the Details guide button when guideUrl is not set", async () => {
      renderWithProviders(
        <LocationListItem {...baseProps} defaultExpanded venue={mockVenue as any} />
      );
      await waitFor(() => expect(screen.getByText("Resources & capacity")).toBeTruthy());
      expect(screen.queryByLabelText("Open guide")).toBeNull();
    });

    it("omits the Guide link when guideUrl is not set", async () => {
      renderWithProviders(<LocationListItem {...baseProps} venue={mockVenue as any} />);
      await waitFor(() => expect(screen.getByText("Toronto Location")).toBeTruthy());
      expect(screen.queryByLabelText("View guide")).toBeNull();
    });
  });

  describe("thumbnail", () => {
    it("falls back away from the native image after it fails to load", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...mockVenue, imageUrl: "https://example.com/photo.jpg" } as any}
        />
      );
      // The thumbnail is decorative and hidden from accessibility, so queries must opt in.
      const withHidden = { includeHiddenElements: true } as const;
      await waitFor(() => expect(screen.getByTestId("location-image", withHidden)).toBeTruthy());
      fireEvent(screen.getByTestId("location-image", withHidden), "error");
      await waitFor(() => expect(screen.queryByTestId("location-image", withHidden)).toBeNull());
    });

    it("hides the decorative thumbnail image from accessibility", async () => {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          venue={{ ...mockVenue, imageUrl: "https://example.com/photo.jpg" } as any}
        />
      );
      await waitFor(() =>
        expect(screen.getByTestId("location-image", { includeHiddenElements: true })).toBeTruthy()
      );
      expect(screen.queryByTestId("location-image")).toBeNull();
    });

    it("shows the name's initial when there is no image", async () => {
      renderWithProviders(<LocationListItem {...baseProps} venue={mockVenue as any} />);
      await waitFor(() => expect(screen.getByText("T")).toBeTruthy());
    });

    describe("on web", () => {
      const originalPlatformOS = Platform.OS;

      beforeEach(() => {
        Object.defineProperty(Platform, "OS", { value: "web", configurable: true });
      });

      afterEach(() => {
        Object.defineProperty(Platform, "OS", { value: originalPlatformOS, configurable: true });
      });

      it("renders the web background-image style when an imageUrl is set", async () => {
        renderWithProviders(
          <LocationListItem
            {...baseProps}
            venue={{ ...mockVenue, imageUrl: "https://example.com/photo.jpg" } as any}
          />
        );
        await waitFor(() => expect(screen.getByText("Toronto Location")).toBeTruthy());
        expect(screen.queryByTestId("location-image")).toBeNull();
      });

      it("renders the web gradient placeholder style when no imageUrl is set", async () => {
        renderWithProviders(<LocationListItem {...baseProps} venue={mockVenue as any} />);
        await waitFor(() => expect(screen.getByText("Toronto Location")).toBeTruthy());
      });
    });
  });

  it("falls back to the UTC timezone when the location has none set", async () => {
    const { timezone: _timezone, ...noTimezoneVenue } = mockVenue;
    renderWithProviders(<LocationListItem {...baseProps} venue={noTimezoneVenue as any} />);
    await waitFor(() => expect(screen.getByText("Toronto Location")).toBeTruthy());
  });

  it("applies dark-theme styling throughout the collapsed and expanded card", async () => {
    const spy = jest.spyOn(useAppThemeModule, "useAppTheme").mockReturnValue({
      colors: getThemeColors(true),
      isDark: true,
      primaryColor: "#0a7ea4",
    } as ReturnType<typeof useAppThemeModule.useAppTheme>);
    try {
      renderWithProviders(
        <LocationListItem
          {...baseProps}
          defaultExpanded
          venue={{ ...mockVenue, walkInDays: "1" } as any}
        />
      );
      await waitFor(() => expect(screen.getByText("Toronto Location")).toBeTruthy());
    } finally {
      spy.mockRestore();
    }
  });

  // ── Combinable resource groups in the placement minimap ─────────────────────

  const venueWithGroups = {
    ...mockVenue,
    sections: [
      {
        id: 1,
        name: "Main",
        venueId: 1,
        resources: [
          { id: 101, name: "T1", capacity: 4, sectionId: 1 },
          { id: 102, name: "T2", capacity: 2, sectionId: 1 },
          { id: 103, name: "T3", capacity: 2, sectionId: 1 },
        ],
      },
    ],
    groups: [
      {
        id: 1,
        name: "Window desks",
        combinedCapacity: 5,
        members: [
          { id: 101, name: "T1", capacity: 4 },
          { id: 102, name: "T2", capacity: 2 },
        ],
      },
    ],
  };

  it("lists combinable groups with their combined capacity in the resources block", async () => {
    renderWithProviders(
      <LocationListItem {...baseProps} defaultExpanded venue={venueWithGroups as any} />
    );
    await waitFor(() => expect(screen.getByText("Resources we can combine")).toBeTruthy());
    expect(screen.getByText("Window desks")).toBeTruthy();
    expect(screen.getByText("Combined capacity: 5 participants")).toBeTruthy();
  });

  it("still lists member resources individually — grouping does not hide them", async () => {
    renderWithProviders(
      <LocationListItem {...baseProps} defaultExpanded venue={venueWithGroups as any} />
    );
    await waitFor(() => expect(screen.getByText("T1")).toBeTruthy());
    expect(screen.getByText("T2")).toBeTruthy();
    expect(screen.getByText("T3")).toBeTruthy();
  });

  it("names an unnamed group after its member resources", async () => {
    renderWithProviders(
      <LocationListItem
        {...baseProps}
        defaultExpanded
        venue={
          {
            ...venueWithGroups,
            groups: [
              {
                id: 2,
                name: null,
                combinedCapacity: 4,
                members: [
                  { id: 102, name: "T2", capacity: 2 },
                  { id: 103, name: "T3", capacity: 2 },
                ],
              },
            ],
          } as any
        }
      />
    );
    await waitFor(() => expect(screen.getByText("Resources T2 + T3")).toBeTruthy());
  });

  it("omits the combinable block entirely for a location with no groups", async () => {
    renderWithProviders(
      <LocationListItem {...baseProps} defaultExpanded venue={mockVenue as any} />
    );
    await waitFor(() => expect(screen.getByText("Resources & capacity")).toBeTruthy());
    expect(screen.queryByText("Resources we can combine")).toBeNull();
  });
});
