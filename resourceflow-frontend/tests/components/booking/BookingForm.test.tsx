/**
 * @jest-environment jsdom
 */
import React from "react";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react-native";
import { StyleSheet, Text } from "react-native";
import BookingForm from "@/components/booking/BookingForm";
import { BookingDockProvider, useBookingDock } from "@/components/booking/BookingDockContext";
import { getNowInTimezone } from "@/utils/date";
import { getVenueDate } from "@/utils/venueTime";
import { getIsoDayFromDateString } from "@/utils/openingHours";

// WalkInNotice links to the waitlist; the real router can't load under Jest.
jest.mock("expo-router", () => ({ useRouter: () => ({ push: jest.fn() }) }));

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

// This whole file renders under Platform.OS === "web" at a desktop width, so the
// two-column layout branches (fieldRow/fieldHalf/holdPush styles) get exercised.
// The native (non-web) branches are already covered by
// tests/components/BookingForm.test.tsx, which uses the jest-expo default
// (Platform.OS === "ios").
//
// A phone browser is also Platform.OS === "web", so width — not platform — is
// what decides the column count. useWindowDimensions reads Dimensions.get, so
// patching that is how the responsive-layout tests below shrink the viewport.
let mockViewportWidth = 1024;
jest.mock("react-native", () => {
  const rn = jest.requireActual("react-native");
  rn.Platform.OS = "web";
  const actualGet = rn.Dimensions.get.bind(rn.Dimensions);
  rn.Dimensions.get = (dim: string) =>
    dim === "window" ? { ...actualGet("window"), width: mockViewportWidth } : actualGet(dim);
  return rn;
});

/**
 * The form publishes its submit from an effect, so the footer showing it is always one commit
 * behind the render that decided to dock. Withholding the publish is how a test reaches that
 * in-between state, which on a device is a frame.
 */
let mockPublishReachesTheDock = true;
jest.mock("@/components/booking/BookingDockContext", () => {
  const actual = jest.requireActual("@/components/booking/BookingDockContext");
  return {
    ...actual,
    usePublishBookingDock: (dock: unknown) =>
      actual.usePublishBookingDock(mockPublishReachesTheDock ? dock : null),
  };
});

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" }),
}));

// Controllable hold status
const mockSetHoldStatus = jest.fn();
let mockHoldStatus = "idle";

jest.mock("@/components/booking/useResourceHold", () => ({
  useResourceHold: () => ({
    hold: null,
    holdStatus: mockHoldStatus,
    secondsLeft: 0,
    holdId: null,
    resolvedResourceId: null,
    resolvedSectionId: null,
    setHoldStatus: mockSetHoldStatus,
  }),
}));

const mockFetchAvailability = jest.fn();
jest.mock("@/api/availability", () => ({
  fetchAvailability: (...args: unknown[]) => mockFetchAvailability(...args),
}));

jest.mock("@/components/booking/HoldStatusBanner", () => ({
  __esModule: true,
  default: () => {
    const { View } = require("react-native");
    return <View testID="hold-banner" />;
  },
}));

jest.mock("@/components/booking/LargePartyNoticeModal", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("@/components/booking/PopularTimesPicker", () => ({
  __esModule: true,
  default: () => {
    const { Text } = require("react-native");
    return <Text>PopularTimesPicker</Text>;
  },
}));

jest.mock("@/components/common/Input", () => ({
  __esModule: true,
  default: ({
    placeholder,
    onChangeText,
  }: {
    placeholder?: string;
    onChangeText?: (v: string) => void;
  }) => {
    const { TextInput } = require("react-native");
    return <TextInput placeholder={placeholder} onChangeText={onChangeText} />;
  },
}));

jest.mock("@/components/common/Button", () => ({
  __esModule: true,
  default: ({ children, onPress, disabled }: any) => {
    const { Pressable, Text } = require("react-native");
    return (
      <Pressable onPress={onPress} disabled={disabled} testID="submit-btn">
        <Text>{children}</Text>
      </Pressable>
    );
  },
}));

// TimePicker mock also surfaces minTime/maxTime so tests can assert the
// after-midnight-closing fallback (maxPickerTime -> "23:45").
jest.mock("@/components/common/TimePicker", () => ({
  __esModule: true,
  default: ({
    selectedTime,
    minTime,
    maxTime,
  }: {
    selectedTime: string;
    minTime?: string;
    maxTime?: string;
  }) => {
    const { Text } = require("react-native");
    return (
      <Text testID="time-picker">
        {selectedTime}|{minTime}|{maxTime}
      </Text>
    );
  },
}));

// DatePicker mock that lets tests trigger a date selection: a closed Saturday,
// a Sunday (to exercise the jsDay===0 -> isoDay 7 mapping), and clearing the
// date entirely (to exercise the "no date selected" fallback branches).
jest.mock("@/components/common/DatePicker", () => ({
  __esModule: true,
  default: ({
    onSelect,
    openDays,
    unavailableDays,
  }: {
    onSelect: (date: string) => void;
    openDays?: number[];
    unavailableDays?: number[];
  }) => {
    const { Pressable, Text, View } = require("react-native");
    return (
      <View>
        <Text testID="date-picker-days">{(openDays ?? []).join(",")}</Text>
        <Text testID="date-picker-unavailable-days">{(unavailableDays ?? []).join(",")}</Text>
        <Pressable testID="date-picker-sat" onPress={() => onSelect("2026-06-20")}>
          <Text>Pick Saturday</Text>
        </Pressable>
        <Pressable testID="date-picker-sun" onPress={() => onSelect("2026-06-21")}>
          <Text>Pick Sunday</Text>
        </Pressable>
        <Pressable testID="date-picker-clear" onPress={() => onSelect("")}>
          <Text>Clear date</Text>
        </Pressable>
      </View>
    );
  },
}));

// Select mock: exposes section selector via testID
jest.mock("@/utils/date", () => ({
  getNowInTimezone: jest.fn(() => ({ dateStr: "2026-06-23", hours: 10, minutes: 0 })),
  formatCurrentTimeInTimezone: jest.fn(() => "10:00 AM"),
  isViewerInTimezone: jest.fn(() => false),
}));

jest.mock("@/components/common/Select", () => ({
  __esModule: true,
  default: ({ onSelect, placeholder, selectedValue, options, accessibilityLabel }: any) => {
    const { Pressable, Text } = require("react-native");
    if (accessibilityLabel === "Number of participants") {
      return (
        <Pressable testID="guests-select" onPress={() => onSelect(selectedValue + 1)}>
          <Text>GuestsSelect:{selectedValue}</Text>
        </Pressable>
      );
    }
    if (placeholder === "Select a section") {
      return (
        <Pressable testID="section-select" onPress={() => onSelect(20)}>
          <Text>SectionSelect:{selectedValue}</Text>
        </Pressable>
      );
    }
    if (placeholder === "Select a resource") {
      // Pick whichever option isn't already selected, so pressing always
      // fires a real onSelect(<different id>) call.
      const alt =
        options?.find((o: { value: number }) => o.value !== selectedValue) ?? options?.[0];
      return (
        <Pressable testID="resource-select" onPress={() => onSelect(alt?.value)}>
          <Text>ResourceSelect:{selectedValue}</Text>
        </Pressable>
      );
    }
    return <Text testID="select-other">{String(selectedValue ?? placeholder ?? "Select")}</Text>;
  },
}));

const mockVenueWeekdays = {
  id: 1,
  name: "Central Workspace",
  address: "1 Main St",
  openTime: "11:00",
  closeTime: "22:00",
  openDays: "1,2,3,4,5", // Mon–Fri only
  timezone: "UTC",
  sections: [
    {
      id: 10,
      name: "Main",
      venueId: 1,
      resources: [{ id: 100, name: "T1", capacity: 4, sectionId: 10 }],
    },
    {
      id: 20,
      name: "Annex",
      venueId: 1,
      resources: [{ id: 200, name: "T2", capacity: 2, sectionId: 20 }],
    },
  ],
};

const mockVenueAllDays = {
  ...mockVenueWeekdays,
  openDays: "1,2,3,4,5,6,7",
};

// No `openDays` at all -> exercises the "default to every day" fallback.
// (openDays is required on VenueDto; cast to model a real-world/older
// payload that omits it, which is exactly what the `venue.openDays?.`
// optional chaining in the component defends against.)
const mockVenueNoOpenDays = (() => {
  const { openDays: _openDays, ...rest } = mockVenueAllDays;
  return rest as unknown as typeof mockVenueAllDays;
})();

// No sections -> exercises the sectionId/resourcesInSection/timezone fallbacks.
// timezone: "" (rather than omitted) keeps this assignable to VenueDto
// while still being falsy, which is what triggers the `|| "UTC"` fallback.
const mockVenueEmptySections = {
  id: 2,
  name: "Empty Spot",
  address: "2 Side St",
  openTime: "11:00",
  closeTime: "22:00",
  openDays: "1,2,3,4,5,6,7",
  timezone: "",
  sections: [],
};

// Closing time wraps past midnight -> exercises the "23:45" max-picker-time
// fallback (close <= open).
const mockVenueLateNight = {
  ...mockVenueAllDays,
  openTime: "18:00",
  closeTime: "02:00",
};

// Open all hours so the mocked "now" (10:00) always falls inside the open
// window -> exercises suggestTime's minute-bucket branches directly, rather
// than its (already istanbul-ignored) "outside open hours" early return.
const mockVenueWideOpen = {
  ...mockVenueAllDays,
  openTime: "00:00",
  closeTime: "23:59",
};

beforeEach(() => {
  jest.clearAllMocks();
  mockHoldStatus = "idle";
  // Both resources (T1=100 in Main, T2=200 in Annex) are available so switching sections
  // in either direction still surfaces a resource dropdown.
  mockFetchAvailability.mockResolvedValue({
    slots: [{ time: "19:00", isAvailable: true, availableResourceIds: [100, 200], category: "PM" }],
  });
  // jest.clearAllMocks() clears call history but not a mockReturnValue set by
  // an earlier test — restore the module's default "now" here so tests don't
  // leak their overrides into one another.
  (getNowInTimezone as jest.Mock).mockReturnValue({ dateStr: "2026-06-23", hours: 10, minutes: 0 });
});

describe("BookingForm", () => {
  it("renders the form with Popular Times label", () => {
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    expect(screen.getByText("Popular Times")).toBeTruthy();
  });

  it("shows 'closed on this day' when a closed day is selected", async () => {
    render(<BookingForm venue={mockVenueWeekdays} onSubmit={jest.fn()} />);
    // Wait for the initial availability fetch (today's date, which is open)
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalledTimes(1));
    // Clear so we can assert no extra call for the closed day
    mockFetchAvailability.mockClear();

    // "2026-06-20" is a Saturday — not in openDays "1,2,3,4,5"
    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-sat"));
    });
    await waitFor(() => {
      expect(
        screen.getByText("The location is closed on this day. Please select a different date.")
      ).toBeTruthy();
    });
    // fetchAvailability should NOT be called for a closed day
    expect(mockFetchAvailability).not.toHaveBeenCalled();
  });

  it("resets hold status to idle when section is changed while held", async () => {
    mockHoldStatus = "held";
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(screen.getByTestId("section-select")).toBeTruthy());
    fireEvent.press(screen.getByTestId("section-select"));
    expect(mockSetHoldStatus).toHaveBeenCalledWith("idle");
  });

  it("resets hold status to idle when section is changed while expired", async () => {
    mockHoldStatus = "expired";
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(screen.getByTestId("section-select")).toBeTruthy());
    fireEvent.press(screen.getByTestId("section-select"));
    expect(mockSetHoldStatus).toHaveBeenCalledWith("idle");
  });

  it("does not reset hold status when section is changed while idle", async () => {
    mockHoldStatus = "idle";
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(screen.getByTestId("section-select")).toBeTruthy());
    fireEvent.press(screen.getByTestId("section-select"));
    expect(mockSetHoldStatus).not.toHaveBeenCalled();
  });

  it("shows the walk-in notice and skips fetch when a walk-in-only day is selected", async () => {
    // Saturdays (ISO 6) are walk-in only; the venue is open every day.
    const venue = { ...mockVenueAllDays, walkInDays: "6" };
    render(<BookingForm venue={venue} onSubmit={jest.fn()} />);
    // Initial fetch happens for today's (bookable) date
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalledTimes(1));
    mockFetchAvailability.mockClear();

    // "2026-06-20" is a Saturday
    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-sat"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("walk-in-notice")).toBeTruthy();
    });
    expect(screen.getByText("Walk-ins only on this day")).toBeTruthy();
    expect(screen.getByText(/doesn't take online bookings on Saturdays/)).toBeTruthy();
    expect(mockFetchAvailability).not.toHaveBeenCalled();
  });

  it("offers the waitlist on a walk-in day only when that day is today", async () => {
    const today = getVenueDate(mockVenueAllDays.timezone ?? "UTC");
    const venue = {
      ...mockVenueAllDays,
      walkInDays: String(getIsoDayFromDateString(today)),
    };
    const onJoinWaitlist = jest.fn();
    const { rerender } = render(
      <BookingForm
        venue={venue}
        onSubmit={jest.fn()}
        date={today}
        onDateChange={jest.fn()}
        onJoinWaitlist={onJoinWaitlist}
      />
    );

    // Button is mocked in this file, so the waitlist action is found by its label.
    fireEvent.press(await screen.findByText("Join the waitlist"));
    expect(onJoinWaitlist).toHaveBeenCalled();

    const nextWeek = new Date(`${today}T12:00:00`);
    nextWeek.setDate(nextWeek.getDate() + 7);
    rerender(
      <BookingForm
        venue={venue}
        onSubmit={jest.fn()}
        date={nextWeek.toISOString().slice(0, 10)}
        onDateChange={jest.fn()}
        onJoinWaitlist={onJoinWaitlist}
      />
    );
    await waitFor(() => expect(screen.getByTestId("walk-in-notice")).toBeTruthy());
    expect(screen.queryByText("Join the waitlist")).toBeNull();
  });

  it("keeps walk-in-only days in the date picker, marked unpickable", () => {
    // Dropping them left the picker unable to name the day the guest was already on, and
    // left a walk-in-only location with an empty list. They stay, greyed out and labelled.
    const venue = { ...mockVenueAllDays, walkInDays: "6,7" };
    render(<BookingForm venue={venue} onSubmit={jest.fn()} />);
    expect(screen.getByTestId("date-picker-days").props.children).toBe("1,2,3,4,5,6,7");
    expect(screen.getByTestId("date-picker-unavailable-days").props.children).toBe("6,7");
  });

  it("marks every open day unpickable for a walk-in-only location", () => {
    const venue = { ...mockVenueAllDays, walkInOnly: true };
    render(<BookingForm venue={venue} onSubmit={jest.fn()} />);
    expect(screen.getByTestId("date-picker-days").props.children).toBe("1,2,3,4,5,6,7");
    expect(screen.getByTestId("date-picker-unavailable-days").props.children).toBe("1,2,3,4,5,6,7");
  });

  it("offers no way to book once a walk-in-only day is selected", async () => {
    const venue = { ...mockVenueAllDays, walkInDays: "6" };
    render(<BookingForm venue={venue} onSubmit={jest.fn()} />);
    expect(screen.getByTestId("submit-btn")).toBeTruthy();

    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-sat"));
    });

    await waitFor(() => expect(screen.getByTestId("walk-in-notice")).toBeTruthy());
    // Party size and date stay live so the guest can pick their way out of the day;
    // everything that would take a booking is gone.
    expect(screen.getByTestId("guests-select")).toBeTruthy();
    expect(screen.queryByTestId("submit-btn")).toBeNull();
    expect(screen.queryByTestId("time-picker")).toBeNull();
    expect(screen.queryByTestId("hold-banner")).toBeNull();
  });

  it("restores the full form when the guest picks their way off a walk-in day", async () => {
    const venue = { ...mockVenueAllDays, walkInDays: "6" };
    render(<BookingForm venue={venue} onSubmit={jest.fn()} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-sat"));
    });
    await waitFor(() => expect(screen.getByTestId("walk-in-notice")).toBeTruthy());
    expect(screen.queryByTestId("submit-btn")).toBeNull();

    // The date control is the way out, so it has to survive the blocked state and work.
    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-sun"));
    });

    await waitFor(() => expect(screen.getByTestId("submit-btn")).toBeTruthy());
    expect(screen.queryByTestId("walk-in-notice")).toBeNull();
    expect(screen.getByTestId("time-picker")).toBeTruthy();
  });

  it("offers no way to book once a closed day is selected", async () => {
    render(<BookingForm venue={mockVenueWeekdays} onSubmit={jest.fn()} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-sun"));
    });

    await waitFor(() =>
      expect(
        screen.getByText("The location is closed on this day. Please select a different date.")
      ).toBeTruthy()
    );
    expect(screen.queryByTestId("submit-btn")).toBeNull();
  });

  it("shows a persistent banner naming the walk-in days regardless of the selected date", () => {
    // Today ("2026-06-23", a Tuesday per the mocked clock) is bookable, but
    // Saturdays are walk-in only — the banner should still be visible.
    const venue = { ...mockVenueAllDays, walkInDays: "6" };
    render(<BookingForm venue={venue} onSubmit={jest.fn()} />);
    expect(screen.getByTestId("walk-in-days-banner")).toBeTruthy();
    expect(screen.getByText(/Walk-ins only on Saturdays/)).toBeTruthy();
  });

  it("does not show the walk-in days banner when no walk-in days are configured", () => {
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    expect(screen.queryByTestId("walk-in-days-banner")).toBeNull();
  });

  it("falls back to a WalkInNotice with no days label when walk-in-only is set globally (no walkInDays)", async () => {
    const venue = { ...mockVenueAllDays, walkInOnly: true };
    render(<BookingForm venue={venue} onSubmit={jest.fn()} />);
    await waitFor(() => {
      expect(screen.getByTestId("walk-in-notice")).toBeTruthy();
    });
  });

  it("renders the 'Any section' auto-assign hint and omits the timezone hint for a location with no sections and no timezone", () => {
    // With no sections to switch to, the form stays in "Any section" mode and shows the
    // auto-assign hint instead of the legacy "No resources available" explicit-resource message.
    render(<BookingForm venue={mockVenueEmptySections} onSubmit={jest.fn()} />);
    expect(screen.getByText(/We'll assign the best available resource/)).toBeTruthy();
    expect(screen.queryByText(/All times are in/)).toBeNull();
  });

  it("defaults to a 7-day open list when venue.openDays is not provided", () => {
    render(<BookingForm venue={mockVenueNoOpenDays} onSubmit={jest.fn()} />);
    // Today ("2026-06-23") should be treated as open, so PopularTimesPicker
    // renders instead of the closed-day notice.
    expect(screen.getByText("PopularTimesPicker")).toBeTruthy();
    expect(
      screen.queryByText("The location is closed on this day. Please select a different date.")
    ).toBeNull();
  });

  it("maps a Sunday selection to ISO day 7 and flags it closed for a weekdays-only location", async () => {
    render(<BookingForm venue={mockVenueWeekdays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalledTimes(1));
    mockFetchAvailability.mockClear();

    // "2026-06-21" is a Sunday — not in openDays "1,2,3,4,5"
    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-sun"));
    });
    await waitFor(() => {
      expect(
        screen.getByText("The location is closed on this day. Please select a different date.")
      ).toBeTruthy();
    });
    expect(mockFetchAvailability).not.toHaveBeenCalled();
  });

  it("treats a cleared date as open with no selected day", async () => {
    render(<BookingForm venue={mockVenueWeekdays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalledTimes(1));
    mockFetchAvailability.mockClear();

    await act(async () => {
      fireEvent.press(screen.getByTestId("date-picker-clear"));
    });
    // With no date selected, isClosedDay/isWalkInDay are both forced false,
    // so PopularTimesPicker renders rather than a closed/walk-in notice.
    await waitFor(() => {
      expect(screen.getByText("PopularTimesPicker")).toBeTruthy();
    });
    expect(
      screen.queryByText("The location is closed on this day. Please select a different date.")
    ).toBeNull();
  });

  it("rolls suggestDate over to the next day once the current time is past the last bookable window", async () => {
    // Venue closes at 12:00, so the latest bookable start is 10:45.
    // Mocked "now" of 23:00 is well past that, forcing the addDays fallback.
    const venue = { ...mockVenueAllDays, openTime: "08:00", closeTime: "12:00" };
    (getNowInTimezone as jest.Mock).mockReturnValue({
      dateStr: "2026-06-23",
      hours: 23,
      minutes: 0,
    });
    render(<BookingForm venue={venue} onSubmit={jest.fn()} initialTime="10:00" />);
    await waitFor(() => {
      expect(mockFetchAvailability).toHaveBeenCalledWith(venue.id, "2026-06-24", 2);
    });
  });

  it.each([
    [5, "10:15"], // minutes < 15
    [20, "10:30"], // 15 <= minutes < 30
    [35, "10:45"], // 30 <= minutes < 45
    [50, "11:00"], // minutes >= 45 -> rolls to the next hour
  ])("suggests %i minutes past the hour as %s", async (minutes, expected) => {
    (getNowInTimezone as jest.Mock).mockReturnValue({
      dateStr: "2026-06-23",
      hours: 10,
      minutes,
    });
    render(<BookingForm venue={mockVenueWideOpen} onSubmit={jest.fn()} />);
    await waitFor(() => {
      expect(screen.getByTestId("time-picker").props.children[0]).toBe(expected);
    });
  });

  it("does not auto-select a time when no slots are available at all", async () => {
    mockFetchAvailability.mockResolvedValue({
      slots: [{ time: "12:00", isAvailable: false, availableResourceIds: [], category: "AM" }],
    });
    render(<BookingForm venue={mockVenueWideOpen} onSubmit={jest.fn()} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // suggestTime(10:00) -> "10:15"; since no slot is available, time is left alone.
    expect(screen.getByTestId("time-picker").props.children[0]).toBe("10:15");
  });

  it("falls back to 23:45 as the max picker time when closing wraps past midnight", () => {
    render(<BookingForm venue={mockVenueLateNight} onSubmit={jest.fn()} />);
    const text = screen.getByTestId("time-picker").props.children.join("");
    expect(text).toContain("|23:45");
  });

  it("resets hold status to idle when the resource is changed while held", async () => {
    mockHoldStatus = "held";
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    // The form defaults to "Any section" (resource dropdown hidden); switch to a concrete
    // section to expose the resource dropdown.
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    await waitFor(() => expect(screen.getByTestId("resource-select")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-select"));
    });
    expect(mockSetHoldStatus).toHaveBeenCalledWith("idle");
  });

  it("resets hold status to idle when the resource is changed while expired", async () => {
    mockHoldStatus = "expired";
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    await waitFor(() => expect(screen.getByTestId("resource-select")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-select"));
    });
    expect(mockSetHoldStatus).toHaveBeenCalledWith("idle");
  });

  it("does not reset hold status when the resource is changed while idle", async () => {
    mockHoldStatus = "idle";
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    await waitFor(() => expect(screen.getByTestId("resource-select")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-select"));
    });
    expect(mockSetHoldStatus).not.toHaveBeenCalled();
  });

  // ── "Any section" auto-assign ──────────────────────────────────────────────

  it("defaults to 'Any section' and shows the auto-assign hint instead of the resource dropdown", () => {
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    // The auto-assign hint replaces the resource dropdown.
    expect(screen.getByText(/We'll assign the best available resource/)).toBeTruthy();
    expect(screen.queryByTestId("resource-select")).toBeNull();
  });

  it("reveals the resource dropdown when a concrete section is selected", async () => {
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    expect(screen.queryByTestId("resource-select")).toBeNull();
    // section-select mock fires onSelect(20) -> Annex section.
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    await waitFor(() => expect(screen.getByTestId("resource-select")).toBeTruthy());
    // Switching out of "Any section" hides the auto-assign hint.
    expect(screen.queryByText(/We'll assign the best available resource/)).toBeNull();
  });

  it("submits with null resourceId/sectionId when 'Any section' is active", async () => {
    mockHoldStatus = "held";
    const onSubmit = jest.fn();
    render(<BookingForm venue={mockVenueAllDays} onSubmit={onSubmit} />);
    fireEvent.changeText(screen.getByPlaceholderText("Your full name"), "Auto User");
    fireEvent.changeText(screen.getByPlaceholderText("your@email.com"), "auto@test.com");
    await act(async () => {
      fireEvent.press(screen.getByText("Confirm Booking"));
    });
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ resourceId: null, sectionId: null })
    );
  });

  // ── Max resource oversize ─────────────────────────────────────────────────────
  // The section-select mock fires onSelect(20) -> Annex, so this variant puts an
  // oversized resource in Annex to exercise the frontend oversize filter. With a party
  // of 2 (the default) and a cap of 1 spare place, a capacity-6 resource is excluded and the
  // section shows "No resources available" instead of the dropdown.
  const mockVenueOversizedAnnex = {
    ...mockVenueAllDays,
    maxSpareCapacity: 1,
    sections: [
      {
        id: 10,
        name: "Main",
        venueId: 1,
        resources: [{ id: 100, name: "T1", capacity: 2, sectionId: 10 }],
      },
      {
        id: 20,
        name: "Annex",
        venueId: 1,
        resources: [{ id: 200, name: "Big", capacity: 6, sectionId: 20 }],
      },
    ],
  };

  it("hides oversized resources from the dropdown when maxSpareCapacity is set", async () => {
    render(<BookingForm venue={mockVenueOversizedAnnex} onSubmit={jest.fn()} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select")); // -> Annex (capacity-6 resource)
    });
    // capacity 6 - 2 guests = 4 spare > cap of 1 -> excluded -> no eligible resources.
    expect(screen.getByText(/No resources available for 2 participants/)).toBeTruthy();
    expect(screen.queryByTestId("resource-select")).toBeNull();
  });

  it("still offers the oversized resource when maxSpareCapacity is unset", async () => {
    const unrestricted = { ...mockVenueOversizedAnnex, maxSpareCapacity: undefined };
    render(<BookingForm venue={unrestricted} onSubmit={jest.fn()} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select")); // -> Annex (capacity-6 resource)
    });
    // No cap -> the capacity-6 resource is eligible and the dropdown renders.
    await waitFor(() => expect(screen.getByTestId("resource-select")).toBeTruthy());
    expect(screen.queryByText(/No resources available/)).toBeNull();
  });

  // ── Combinable resource groups ───────────────────────────────────────────────
  //
  // A venue whose largest single resource fits 4 but which has a combinable group of 8.
  // A party of 6 fits only via the group — the large-party modal must NOT fire.

  const mockVenueWithGroup = {
    ...mockVenueAllDays,
    sections: [
      {
        id: 10,
        name: "Main",
        venueId: 1,
        resources: [
          { id: 100, name: "T1", capacity: 4, sectionId: 10 },
          { id: 101, name: "T2", capacity: 4, sectionId: 10 },
        ],
      },
    ],
    groups: [
      {
        id: 1,
        name: null,
        combinedCapacity: 8,
        members: [
          { id: 100, name: "T1", capacity: 4 },
          { id: 101, name: "T2", capacity: 4 },
        ],
      },
    ],
  };

  it("does not show the large-party notice when a combinable group can fit the party", async () => {
    // Party of 6 exceeds the largest single resource (4) but fits the group (8).
    render(<BookingForm venue={mockVenueWithGroup} onSubmit={jest.fn()} initialPartySize={6} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // No large-party notice/modal since the group can fit the party.
    expect(screen.queryByText("Large group")).toBeNull();
  });

  it("still shows the large-party notice when even the group cannot fit the party", async () => {
    // Party of 10 exceeds both the largest single resource (4) and the group (8).
    render(<BookingForm venue={mockVenueWithGroup} onSubmit={jest.fn()} initialPartySize={10} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // The large-party notice (and auto-opened modal) both render — assert at least one.
    expect(screen.getAllByText("Large group").length).toBeGreaterThan(0);
  });

  it("offers the group as a dropdown option when availableGroupIds includes it", async () => {
    mockFetchAvailability.mockResolvedValue({
      slots: [
        {
          time: "19:00",
          isAvailable: true,
          availableResourceIds: [],
          availableGroupIds: [1],
          category: "PM",
        },
      ],
    });
    render(<BookingForm venue={mockVenueWithGroup} onSubmit={jest.fn()} initialPartySize={6} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // Switch out of "Any section" so the explicit resource dropdown is visible.
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    // The dropdown renders (a group is selectable) even though no single resource is available.
    await waitFor(() => expect(screen.getByTestId("resource-select")).toBeTruthy());
    expect(screen.queryByText(/No resources available/)).toBeNull();
  });

  it("does not offer the group when availableGroupIds excludes it", async () => {
    mockFetchAvailability.mockResolvedValue({
      slots: [
        {
          time: "19:00",
          isAvailable: true,
          availableResourceIds: [],
          availableGroupIds: [], // group not bookable for this slot
          category: "PM",
        },
      ],
    });
    render(<BookingForm venue={mockVenueWithGroup} onSubmit={jest.fn()} initialPartySize={6} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // Switch out of "Any section" so the explicit resource dropdown would be visible if eligible.
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    // No standalone resources (empty availableResourceIds) and the group excluded → no dropdown.
    expect(screen.getByText(/No resources available for 6 participants/)).toBeTruthy();
  });

  // ── Groups are scoped to the picked section ───────────────────────────────
  //
  // The Select mock always resolves the section field to id 20 ("Annex"), so these two fixtures
  // differ only in which section owns the group's member resources.

  const groupOfEights = [
    {
      id: 1,
      name: null,
      combinedCapacity: 8,
      members: [
        { id: 100, name: "T1", capacity: 4 },
        { id: 101, name: "T2", capacity: 4 },
      ],
    },
  ];

  const mockVenueGroupInMain = {
    ...mockVenueAllDays,
    sections: [
      {
        id: 10,
        name: "Main",
        venueId: 1,
        resources: [
          { id: 100, name: "T1", capacity: 4, sectionId: 10 },
          { id: 101, name: "T2", capacity: 4, sectionId: 10 },
        ],
      },
      {
        id: 20,
        name: "Annex",
        venueId: 1,
        resources: [{ id: 200, name: "P1", capacity: 2, sectionId: 20 }],
      },
    ],
    groups: groupOfEights,
  };

  const mockVenueGroupInAnnex = {
    ...mockVenueAllDays,
    sections: [
      {
        id: 10,
        name: "Main",
        venueId: 1,
        resources: [{ id: 200, name: "P1", capacity: 2, sectionId: 10 }],
      },
      {
        id: 20,
        name: "Annex",
        venueId: 1,
        resources: [
          { id: 100, name: "T1", capacity: 4, sectionId: 20 },
          { id: 101, name: "T2", capacity: 4, sectionId: 20 },
        ],
      },
    ],
    groups: groupOfEights,
  };

  const groupOnlySlot = {
    slots: [
      {
        time: "19:00",
        isAvailable: true,
        availableResourceIds: [],
        availableGroupIds: [1],
        category: "PM",
      },
    ],
  };

  it("does not offer a group whose resources live in another section", async () => {
    mockFetchAvailability.mockResolvedValue(groupOnlySlot);
    render(<BookingForm venue={mockVenueGroupInMain} onSubmit={jest.fn()} initialPartySize={6} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // Select "Annex" — the group's resources are both in "Main".
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    expect(screen.getByText(/No resources available for 6 participants/)).toBeTruthy();
  });

  it("offers a group whose resources all live in the picked section", async () => {
    mockFetchAvailability.mockResolvedValue(groupOnlySlot);
    render(<BookingForm venue={mockVenueGroupInAnnex} onSubmit={jest.fn()} initialPartySize={6} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // Select "Annex" — this time it owns both of the group's resources.
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-select"));
    });
    await waitFor(() => expect(screen.getByTestId("resource-select")).toBeTruthy());
    expect(screen.queryByText(/No resources available/)).toBeNull();
  });

  it("still offers every group under 'Any section'", async () => {
    mockFetchAvailability.mockResolvedValue(groupOnlySlot);
    render(<BookingForm venue={mockVenueGroupInMain} onSubmit={jest.fn()} initialPartySize={6} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    // Auto-assign is the default; the group still counts toward capacity, so no large-party notice.
    expect(screen.queryByText("Large group")).toBeNull();
    expect(screen.getByText(/best available resource/)).toBeTruthy();
  });
});

// Platform.OS === "web" covers a phone browser too, so these assert that the
// side-by-side field pairs collapse on width rather than on platform.
describe("BookingForm responsive layout", () => {
  const DESKTOP_WIDTH = 1024;
  const PHONE_WIDTH = 390;

  afterEach(() => {
    mockViewportWidth = DESKTOP_WIDTH;
  });

  function rowFlexDirections() {
    return screen
      .getAllByTestId("booking-field-row")
      .map((row) => StyleSheet.flatten(row.props.style)?.flexDirection);
  }

  it("lays the field pairs out side by side at desktop width", async () => {
    mockViewportWidth = DESKTOP_WIDTH;
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

    const directions = rowFlexDirections();
    expect(directions).toHaveLength(4);
    expect(directions.every((d) => d === "row")).toBe(true);
  });

  it("stacks every field pair onto its own row at phone width", async () => {
    mockViewportWidth = PHONE_WIDTH;
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

    const directions = rowFlexDirections();
    expect(directions).toHaveLength(4);
    // No row lays out horizontally, so Email and Special Requests each get the
    // full width instead of sharing a ~180px column.
    expect(directions.every((d) => d === undefined)).toBe(true);
  });

  it("renders the hold banner exactly once in either layout", async () => {
    mockViewportWidth = PHONE_WIDTH;
    const { unmount } = render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    expect(screen.getAllByTestId("hold-banner")).toHaveLength(1);
    unmount();

    mockViewportWidth = DESKTOP_WIDTH;
    render(<BookingForm venue={mockVenueAllDays} onSubmit={jest.fn()} />);
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    expect(screen.getAllByTestId("hold-banner")).toHaveLength(1);
  });
});

// The drawer variant of the form: party size and date have already been settled by the
// page-level filter bar, so those pickers are gone and placement moves behind a disclosure.
/** Reports whether the form published a submit, which is the dock's only observable side. */
function DockReadout() {
  const dock = useBookingDock();
  return <Text testID="dock-readout">{dock ? "published" : "empty"}</Text>;
}

describe("BookingForm drawer layout", () => {
  beforeEach(() => {
    mockViewportWidth = 1024;
    mockHoldStatus = "idle";
    mockFetchAvailability.mockResolvedValue({ slots: [] });
  });

  function renderDrawer(overrides: Record<string, unknown> = {}) {
    return render(
      <BookingForm
        layout="drawer"
        venue={mockVenueAllDays}
        partySize={4}
        date="2026-06-24"
        onSubmit={jest.fn()}
        {...overrides}
      />
    );
  }

  it("keeps participant count and date adjustable, seeded from the page bar", async () => {
    renderDrawer();
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    expect(screen.getByText("Participants")).toBeTruthy();
    expect(screen.getByText("GuestsSelect:4")).toBeTruthy();
    expect(screen.getByText("Date")).toBeTruthy();
    // The drawer lays fields out in its own single column, never the inline pair rows.
    expect(screen.queryAllByTestId("booking-field-row")).toHaveLength(0);
  });

  it("reports a party-size change back to the caller instead of diverging from it", async () => {
    const onPartySizeChange = jest.fn();
    renderDrawer({ onPartySizeChange });
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

    fireEvent.press(screen.getByTestId("guests-select"));

    expect(onPartySizeChange).toHaveBeenCalledWith(5);
    // Still showing the caller's number: the page owns it, so the form waits to be told.
    expect(screen.getByText("GuestsSelect:4")).toBeTruthy();
  });

  it("reports a date change back to the caller instead of diverging from it", async () => {
    const onDateChange = jest.fn();
    renderDrawer({ onDateChange });
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

    fireEvent.press(screen.getByTestId("date-picker-sun"));

    expect(onDateChange).toHaveBeenCalledWith("2026-06-21");
    // The page owns the date, so the form keeps showing the one it was given.
    await waitFor(() =>
      expect(mockFetchAvailability).toHaveBeenLastCalledWith(expect.anything(), "2026-06-24", 4)
    );
  });

  it("fetches availability for the caller's participant count and date", async () => {
    renderDrawer();
    await waitFor(() =>
      expect(mockFetchAvailability).toHaveBeenCalledWith(expect.anything(), "2026-06-24", 4)
    );
  });

  it("asks only for name, email and requests up front", async () => {
    renderDrawer();
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    expect(screen.getByPlaceholderText("Your full name")).toBeTruthy();
    expect(screen.getByPlaceholderText("your@email.com")).toBeTruthy();
    expect(screen.getByText("Requirements")).toBeTruthy();
    // Section and resource stay behind the disclosure until asked for.
    expect(screen.queryByText("Section")).toBeNull();
    expect(screen.queryByText("Resource")).toBeNull();
  });

  it("reveals the section and resource controls from the placement disclosure", async () => {
    renderDrawer();
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

    // The exact-time picker is about time, not placement, so it stays out in the form.
    expect(screen.getByTestId("time-picker")).toBeTruthy();

    fireEvent.press(screen.getByTestId("placement-disclosure-toggle"));
    expect(screen.getByText("Section")).toBeTruthy();
    expect(screen.getByText("Resource")).toBeTruthy();

    fireEvent.press(screen.getByTestId("placement-disclosure-toggle"));
    expect(screen.queryByText("Section")).toBeNull();
    // The toggle keeps one stable label and flips its chevron, rather than renaming
    // itself — the row is a section boundary, not a link that changes meaning.
    expect(screen.getByText("Choose a section or resource")).toBeTruthy();
  });

  it("groups the drawer into labelled sections and does not label two things 'Time'", async () => {
    renderDrawer();
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

    expect(screen.getByText("Group & date")).toBeTruthy();
    expect(screen.getByText("Your details")).toBeTruthy();

    // The exact-time picker sits under the chips that are already the times on offer,
    // so it must not be labelled "Time" as well.
    expect(screen.getByText("Exact time")).toBeTruthy();
    expect(screen.queryAllByText("Time")).toHaveLength(0);
  });

  it("shows the same privacy note as the inline form, in full and without a toggle", async () => {
    renderDrawer();
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    expect(screen.getByText(/essential cookie/)).toBeTruthy();
    expect(screen.queryByTestId("privacy-note-toggle")).toBeNull();
  });

  it("puts the timezone note under the times it qualifies, not down in the footer", async () => {
    renderDrawer();
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

    // Tree order: the note has to land inside the Time section, which ends where the
    // "Your details" heading begins.
    const tree = JSON.stringify(screen.toJSON());
    const note = tree.indexOf("All times are in");
    const details = tree.indexOf("Your details");
    expect(note).toBeGreaterThan(-1);
    expect(details).toBeGreaterThan(-1);
    expect(note).toBeLessThan(details);
  });

  it("still shows the hold banner and confirm button exactly once", async () => {
    renderDrawer();
    await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
    expect(screen.getAllByTestId("hold-banner")).toHaveLength(1);
    expect(screen.getAllByTestId("submit-btn")).toHaveLength(1);
    expect(screen.getByText("Confirm Booking")).toBeTruthy();
  });

  /**
   * Inside the platform sheet the confirm is docked to the bottom edge, so the form gives its
   * own up rather than leaving a second one in the scroll behind it. The pair is the rule: no
   * dock hosted, or nothing typed yet, and the form keeps it.
   */
  describe("with a dock hosted", () => {
    beforeEach(() => {
      mockPublishReachesTheDock = true;
    });

    const renderDocked = (overrides: Record<string, unknown> = {}) =>
      render(
        <BookingDockProvider>
          <BookingForm
            layout="drawer"
            venue={mockVenueAllDays}
            partySize={4}
            date="2026-06-24"
            onSubmit={jest.fn()}
            {...overrides}
          />
          <DockReadout />
        </BookingDockProvider>
      );

    const fill = () => {
      fireEvent.changeText(screen.getByPlaceholderText("Your full name"), "Frank Reynolds");
      fireEvent.changeText(
        screen.getByPlaceholderText("your@email.com"),
        "frank@paddyspub.example"
      );
    };

    it("keeps its own confirm until a name and email are entered", async () => {
      renderDocked();
      await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

      expect(screen.getAllByTestId("submit-btn")).toHaveLength(1);
      expect(screen.getByTestId("dock-readout").props.children).toBe("empty");
    });

    it("hands the confirm to the dock once both are in", async () => {
      renderDocked();
      await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

      fill();

      expect(screen.queryByTestId("submit-btn")).toBeNull();
      expect(screen.getByTestId("dock-readout").props.children).toBe("published");
    });

    // The countdown goes with it, or the sheet would show a hold banner and no way to act on it.
    it("takes the hold banner with it", async () => {
      renderDocked();
      await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

      fill();

      expect(screen.queryByTestId("hold-banner")).toBeNull();
    });

    /**
     * The dock arrives a commit after the render that decided to dock it, so a form that gave
     * its confirm up on that earlier render leaves a frame with neither — which on a phone is
     * the scroll jumping as the guest finishes typing their email, and jumping back.
     */
    it("keeps its own confirm for as long as the dock is showing none", async () => {
      mockPublishReachesTheDock = false;
      renderDocked();
      await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());

      fill();

      expect(screen.getAllByTestId("submit-btn")).toHaveLength(1);
      expect(screen.getByTestId("dock-readout").props.children).toBe("empty");
    });

    it("takes it back if the email is cleared again", async () => {
      renderDocked();
      await waitFor(() => expect(mockFetchAvailability).toHaveBeenCalled());
      fill();

      fireEvent.changeText(screen.getByPlaceholderText("your@email.com"), "");

      expect(screen.getAllByTestId("submit-btn")).toHaveLength(1);
      expect(screen.getByTestId("dock-readout").props.children).toBe("empty");
    });
  });

  it("tracks a participant count changed on the page bar without remounting", async () => {
    const { rerender } = renderDrawer();
    await waitFor(() =>
      expect(mockFetchAvailability).toHaveBeenCalledWith(expect.anything(), "2026-06-24", 4)
    );
    rerender(
      <BookingForm
        layout="drawer"
        venue={mockVenueAllDays}
        partySize={7}
        date="2026-06-24"
        onSubmit={jest.fn()}
      />
    );
    await waitFor(() =>
      expect(mockFetchAvailability).toHaveBeenCalledWith(expect.anything(), "2026-06-24", 7)
    );
  });
});
