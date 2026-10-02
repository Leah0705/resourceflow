import React from "react";
import { act, render, screen, fireEvent } from "@testing-library/react-native";
import { AvailabilityGrid } from "@/components/admin/bookings/AvailabilityGrid";
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

/** A booking on `DAY` at `hhmm` UTC, `minutes` long. */
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

describe("AvailabilityGrid", () => {
  const mockSections = [
    {
      id: 1,
      name: "Main",
      resources: [{ id: 101, name: "Resource 1", capacity: 4 }],
    },
  ];

  const props = {
    sections: mockSections as any,
    bookings: [booking(10, "18:00", 90)] as any,
    isDark: false,
    onBookingPress: jest.fn(),
    openTime: "17:00",
    closeTime: "23:00",
    timezone: "UTC",
    // A day that is never the location's today, so the now marker stays out of these assertions.
    gridDateIso: DAY,
  };

  beforeEach(() => jest.clearAllMocks());

  it("renders empty state when no sections", () => {
    render(<AvailabilityGrid {...props} sections={[]} />);
    expect(screen.getByText(/No resources found/)).toBeTruthy();
  });

  it("renders the section, its resources and the hours around the day's slots", () => {
    render(<AvailabilityGrid {...props} />);
    expect(screen.getByText("MAIN")).toBeTruthy();
    expect(screen.getByText("Resource 1")).toBeTruthy();
    expect(screen.getByText("5p")).toBeTruthy();
    expect(screen.getByText("8p")).toBeTruthy();
  });

  // Opening hours run to 11p but the only slot is 6p-7:30p, so the idle hours are trimmed away
  // rather than burying the slots off the side of the screen.
  it("does not draw the opening hours the day's slots do not reach", () => {
    render(<AvailabilityGrid {...props} />);
    expect(screen.queryByText("10p")).toBeNull();
  });

  it("says so plainly on a day with no bookings instead of drawing an empty grid", () => {
    render(<AvailabilityGrid {...props} bookings={[]} dateLabel="Sun 23 Aug" />);

    expect(screen.getByText("No bookings on Sun 23 Aug")).toBeTruthy();
    expect(screen.queryByText("5p")).toBeNull();
  });

  it("labels a slot with its participant, party size and time range", () => {
    render(<AvailabilityGrid {...props} />);
    expect(screen.getByText("booked · 2p")).toBeTruthy();
    expect(screen.getByText("6:00p–7:30p")).toBeTruthy();
  });

  it("prefers the participant's name over their email when there is one", () => {
    render(
      <AvailabilityGrid
        {...props}
        bookings={[booking(10, "18:00", 90, { customerName: "Patel" })] as any}
      />
    );
    expect(screen.getByText("Patel · 2p")).toBeTruthy();
  });

  it("opens the booking when its bar is pressed", () => {
    render(<AvailabilityGrid {...props} />);

    fireEvent.press(screen.getByTestId("slot-10"));

    expect(props.onBookingPress).toHaveBeenCalledWith(expect.objectContaining({ id: 10 }));
  });

  // The grid used to bucket by the hour and render only the first match, so a second slot in
  // the same hour on the same resource vanished entirely.
  it("renders both slots when two start in the same hour on one resource", () => {
    render(
      <AvailabilityGrid
        {...props}
        bookings={[booking(10, "18:00", 30), booking(11, "18:30", 30)] as any}
      />
    );

    expect(screen.getByTestId("slot-10")).toBeTruthy();
    expect(screen.getByTestId("slot-11")).toBeTruthy();
  });

  // A group booking stores ResourceId = null, so a grid keyed on the resource alone drops it silently.
  it("renders a group booking on its combined-resources row", () => {
    render(
      <AvailabilityGrid
        {...props}
        groups={
          [{ id: 5, name: "Window desks", combinedCapacity: 7, members: [{ id: 101 }] }] as any
        }
        bookings={[booking(12, "19:00", 90, { resourceId: null, resourceGroupId: 5 })] as any}
      />
    );

    expect(screen.getByText("COMBINED RESOURCES")).toBeTruthy();
    expect(screen.getByText("Window desks")).toBeTruthy();
    expect(screen.getByTestId("slot-12")).toBeTruthy();
  });

  // Deleting a resource nulls its bookings' FK rather than cascading, so these belong to no unit.
  it("keeps a booking whose resource was deleted visible on an unassigned row", () => {
    render(
      <AvailabilityGrid
        {...props}
        bookings={[booking(13, "19:00", 90, { resourceId: null, resourceGroupId: null })] as any}
      />
    );

    expect(screen.getByText("UNASSIGNED")).toBeTruthy();
    expect(screen.getByTestId("slot-13")).toBeTruthy();
  });

  it("does not draw the unassigned row when every booking has a unit", () => {
    render(<AvailabilityGrid {...props} />);
    expect(screen.queryByText("UNASSIGNED")).toBeNull();
  });

  // Narrowing a location's opening hours does not move the bookings already taken under the old
  // ones. The timetable is where staff would look for them, so the window has to widen.
  it("keeps a booking that now falls outside the opening hours visible", () => {
    render(<AvailabilityGrid {...props} bookings={[booking(10, "12:00", 60)] as any} />);

    expect(screen.getByText("12p")).toBeTruthy();
    expect(screen.getByTestId("slot-10")).toBeTruthy();
  });

  it("carries a slot across midnight on an overnight opening", () => {
    render(
      <AvailabilityGrid
        {...props}
        bookings={[booking(10, "23:00", 120)] as any}
        openTime="18:00"
        closeTime="02:00"
      />
    );

    expect(screen.getByText("11p")).toBeTruthy();
    expect(screen.getByText("12a")).toBeTruthy();
    expect(screen.getByText("1a")).toBeTruthy();
    expect(screen.queryByText("2a")).toBeNull();
  });

  it("renders correctly in dark mode", () => {
    render(<AvailabilityGrid {...props} isDark={true} />);
    expect(screen.getByText("Resource 1")).toBeTruthy();
  });

  describe("the now marker", () => {
    const REAL_NOW = new Date(`${DAY}T18:30:00.000Z`);

    beforeEach(() => {
      jest.useFakeTimers();
      jest.setSystemTime(REAL_NOW);
    });
    afterEach(() => jest.useRealTimers());

    it("draws on the location's today and counts down the slot in progress", () => {
      render(<AvailabilityGrid {...props} />);

      expect(screen.getByTestId("now-marker")).toBeTruthy();
      expect(screen.getByText("1h left")).toBeTruthy();
    });

    it("stays off any other day, where there is no now to mark", () => {
      render(<AvailabilityGrid {...props} gridDateIso="2026-08-30" />);

      expect(screen.queryByTestId("now-marker")).toBeNull();
      expect(screen.getByText("6:00p–7:30p")).toBeTruthy();
    });

    it("stays off when now falls outside the drawn opening-hours window", () => {
      jest.setSystemTime(new Date(`${DAY}T04:00:00.000Z`));

      render(<AvailabilityGrid {...props} bookings={[]} />);

      expect(screen.queryByTestId("now-marker")).toBeNull();
    });
  });

  // The combined and unassigned blocks are the grid's own words rather than the admin's, so they
  // are translated like every other string on the screen.
  describe("in the reader's language", () => {
    afterEach(async () => {
      // The mounted tree re-renders on the language change, so the restore is a React update too.
      await act(() => i18n.changeLanguage("en"));
    });

    it("names the combined-resources block in French rather than in English", async () => {
      await act(() => i18n.changeLanguage("fr"));

      render(
        <AvailabilityGrid
          {...props}
          groups={
            [{ id: 5, name: "Window desks", combinedCapacity: 7, members: [{ id: 101 }] }] as never
          }
          bookings={[booking(12, "19:00", 90, { resourceId: null, resourceGroupId: 5 })] as never}
        />
      );

      expect(screen.getByText("RESSOURCES COMBINÉES")).toBeTruthy();
      expect(screen.queryByText("COMBINED RESOURCES")).toBeNull();
    });

    it("names the unassigned block and its row in French rather than in English", async () => {
      await act(() => i18n.changeLanguage("fr"));

      render(
        <AvailabilityGrid
          {...props}
          bookings={
            [booking(13, "19:00", 90, { resourceId: null, resourceGroupId: null })] as never
          }
        />
      );

      expect(screen.getByText("NON ATTRIBUÉ")).toBeTruthy();
      expect(screen.getByText("Sans ressource")).toBeTruthy();
    });
  });
});
