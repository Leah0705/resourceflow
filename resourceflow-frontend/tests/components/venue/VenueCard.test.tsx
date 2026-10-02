import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react-native";
import VenueCard from "@/components/venue/VenueCard";
import { fetchAvailability } from "@/api/availability";
import { Linking, Platform, StyleSheet } from "react-native";
import { haptics } from "@/utils/haptics";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("expo-image", () => ({
  Image: ({ testID, source }: any) =>
    require("react").createElement("Image", { testID: testID ?? "expo-image", source }),
}));

jest.mock("@/api/availability", () => ({
  fetchAvailability: jest.fn(),
}));

const mockPush = jest.fn();
jest.mock("expo-router", () => ({
  useRouter: () => ({ push: mockPush }),
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "Test" }),
}));

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

jest.mock("@/utils/haptics", () => ({
  haptics: { selection: jest.fn(), press: jest.fn(), outcome: jest.fn() },
}));

jest.spyOn(Linking, "openURL").mockResolvedValue(undefined as never);

/**
 * jest-expo defaults Platform.OS to "ios". The card grew a platform split (one directions
 * pill and no new-tab control off web), so the web rules below have to say which side they
 * are about; `onPlatform` runs a body on the other.
 */
Object.defineProperty(Platform, "OS", { get: () => "web", configurable: true });

const onPlatform = async (os: string, body: () => Promise<void>) => {
  Object.defineProperty(Platform, "OS", { get: () => os, configurable: true });
  try {
    await body();
  } finally {
    Object.defineProperty(Platform, "OS", { get: () => "web", configurable: true });
  }
};

const mockVenue = {
  id: 1,
  name: "Test Venue",
  address: "123 Main St",
  openTime: "00:00",
  closeTime: "23:59",
  openDays: "1,2,3,4,5,6,7",
  timezone: "UTC",
  tags: [],
  sections: [],
};

describe("VenueCard", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (fetchAvailability as jest.Mock).mockResolvedValue({ slots: [] });
  });

  // iOS draws a shadow from the view's own box and `overflow: "hidden"` clips it away, so a
  // card carrying both rendered flat on iOS while Android kept its `elevation`. The clip the
  // rounded image needs has to sit one level inside the view that casts the shadow.
  it("casts its shadow from a view that does not clip its own children", async () => {
    await onPlatform("ios", async () => {
      render(<VenueCard venue={mockVenue as never} party={2} />);
      const card = await screen.findByLabelText("Test Venue, view details and book");
      const shadowHost = StyleSheet.flatten(card.props.style);

      expect(shadowHost.shadowRadius).toBeGreaterThan(0);
      expect(shadowHost.overflow).not.toBe("hidden");
    });
  });

  it("renders location name", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
  });

  it("shows address", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("123 Main St")).toBeTruthy());
  });

  it("shows 'No available slots today' when fetch returns empty slots", async () => {
    (fetchAvailability as jest.Mock).mockResolvedValue({ slots: [] });
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("No available slots today")).toBeTruthy());
  });

  it("shows available time slot", async () => {
    // Pin clock to noon UTC so the 23:30 slot is always in the future.
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-01T12:00:00Z"));
    try {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "23:30", isAvailable: true }],
      });
      render(<VenueCard venue={mockVenue} />);
      await waitFor(() => expect(screen.getByText("23:30")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("filters out unavailable slots", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-01T12:00:00Z"));
    try {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "23:30", isAvailable: false }],
      });
      render(<VenueCard venue={mockVenue} />);
      await waitFor(() => expect(screen.getByText("No available slots today")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("navigates to booking when 'See details' is pressed", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("See details")).toBeTruthy());

    fireEvent.press(screen.getByText("See details"));

    expect(mockPush).toHaveBeenCalledWith(expect.stringContaining("/(user)/locations/1"));
  });

  it("shows Google Maps and Apple Maps links", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("Google")).toBeTruthy());
    expect(screen.getByText("Apple")).toBeTruthy();
  });

  it("shows tags when location has tags", async () => {
    render(<VenueCard venue={{ ...mockVenue, tags: ["dog friendly"] }} />);
    await waitFor(() => expect(screen.getByText("dog friendly")).toBeTruthy());
  });

  it("shows Closed badge when location is closed for the day (past close time)", async () => {
    jest.useFakeTimers();
    // 23:59 UTC — past the 23:00-23:59 window so the day's opening hours are over
    jest.setSystemTime(new Date("2026-01-01T23:59:30Z"));
    try {
      render(<VenueCard venue={{ ...mockVenue, openTime: "23:00", closeTime: "23:59" }} />);
      await waitFor(() => expect(screen.queryByText("Closed")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("shows 'Opens in Xh' badge when opening is an exact number of hours away", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-05T10:00:00Z")); // Monday 10:00 UTC
    try {
      render(<VenueCard venue={{ ...mockVenue, openTime: "14:00", closeTime: "22:00" }} />);
      await waitFor(() => expect(screen.getByText("Opens in 4h")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("shows 'Opens in Xh Ym' badge when opening is hours and minutes away", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-05T10:00:00Z")); // Monday 10:00 UTC
    try {
      render(<VenueCard venue={{ ...mockVenue, openTime: "14:30", closeTime: "22:00" }} />);
      await waitFor(() => expect(screen.getByText("Opens in 4h 30m")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("shows 'Opens in Xm' badge when opening is less than an hour away", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-05T10:00:00Z")); // Monday 10:00 UTC
    try {
      render(<VenueCard venue={{ ...mockVenue, openTime: "10:30", closeTime: "22:00" }} />);
      await waitFor(() => expect(screen.getByText("Opens in 30m")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("shows 'Opens in' when openDays has all-unknown day names and time is before opening", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-05T10:00:00Z")); // Monday 10:00 UTC
    try {
      render(
        <VenueCard
          venue={{ ...mockVenue, openDays: "xyz", openTime: "14:00", closeTime: "22:00" }}
        />
      );
      // openDaysList is empty → day check skipped → shows "Opens in 4h"
      await waitFor(() => expect(screen.getByText("Opens in 4h")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("shows 'Open till' badge when location is open", async () => {
    render(<VenueCard venue={{ ...mockVenue, openTime: "00:00", closeTime: "23:59" }} />);
    await waitFor(() => {
      const el = screen.queryByText(/Open till/);
      expect(el).toBeTruthy();
    });
  });

  it("handles null fetchAvailability response gracefully", async () => {
    (fetchAvailability as jest.Mock).mockResolvedValue(null);
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("No available slots today")).toBeTruthy());
  });

  it("fetches availability when openDays uses day names", async () => {
    // Regression test for: openDays="Mon,Tue,Wed" should still call API
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-05T12:00:00Z")); // Monday
    try {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "23:30", isAvailable: true }],
      });
      const venueWithDayNames = {
        ...mockVenue,
        openDays: "Mon,Tue,Wed,Thu,Fri,Sat,Sun", // Day names instead of numbers
      };
      render(<VenueCard venue={venueWithDayNames} />);
      await waitFor(() => expect(screen.getByText("23:30")).toBeTruthy());
      expect(fetchAvailability).toHaveBeenCalled();
    } finally {
      jest.useRealTimers();
    }
  });

  it("renders location correctly when imageUrl is set", async () => {
    render(<VenueCard venue={{ ...mockVenue, imageUrl: "/media/photo.jpg" }} />);
    await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
  });

  it("does not render expo-image when imageUrl is absent", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.queryByTestId("expo-image")).toBeNull());
  });

  it("shows closed when openDays day names exclude today", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-05T12:00:00Z")); // Monday
    try {
      const weekendOnlyVenue = {
        ...mockVenue,
        openDays: "Sat,Sun", // Weekend only
      };
      render(<VenueCard venue={weekendOnlyVenue} />);
      await waitFor(() => expect(screen.getByText("Closed")).toBeTruthy());
      expect(fetchAvailability).not.toHaveBeenCalled();
    } finally {
      jest.useRealTimers();
    }
  });

  it("presses main card to navigate to booking page", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
    // Press the venue name — it lives inside the outer card Pressable
    fireEvent.press(screen.getByText("Test Venue"));
    expect(mockPush).toHaveBeenCalledWith("/(user)/locations/1");
  });

  it("presses Google Maps link and opens URL", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("Google")).toBeTruthy());
    // Pass a mock event to satisfy e.stopPropagation?.()
    fireEvent.press(screen.getByText("Google"), { stopPropagation: () => {} });
    expect(Linking.openURL).toHaveBeenCalledWith(expect.stringContaining("maps.google.com"));
  });

  it("answers a tap on the card itself before navigating", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());

    fireEvent.press(screen.getByLabelText("Test Venue, view details and book"));
    expect(haptics.selection).toHaveBeenCalled();
    expect(mockPush).toHaveBeenCalledWith("/(user)/locations/1");
  });

  it("answers a tap on a time the same way", async () => {
    // Pin clock to noon UTC so the 19:00 slot is always in the future.
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-01T12:00:00Z"));
    try {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "19:00", isAvailable: true, category: "PM" }],
      });
      render(<VenueCard venue={mockVenue} party={2} />);
      await waitFor(() => expect(screen.getByText("19:00")).toBeTruthy());

      fireEvent.press(screen.getByText("19:00"), { stopPropagation: () => {} });
      expect(haptics.selection).toHaveBeenCalled();
      expect(mockPush).toHaveBeenCalledWith("/(user)/locations/1?time=19%3A00&party=2");
    } finally {
      jest.useRealTimers();
    }
  });

  it("presses Apple Maps link and opens URL", async () => {
    render(<VenueCard venue={mockVenue} />);
    await waitFor(() => expect(screen.getByText("Apple")).toBeTruthy());
    fireEvent.press(screen.getByText("Apple"), { stopPropagation: () => {} });
    expect(Linking.openURL).toHaveBeenCalledWith(expect.stringContaining("maps.apple.com"));
  });

  it("opens the booking page in a new tab from the web card's corner control", async () => {
    const open = jest.fn();
    (globalThis as unknown as { window: { open: jest.Mock } }).window = { open };
    try {
      render(<VenueCard venue={mockVenue} />);
      await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
      fireEvent.press(screen.getByLabelText("Open booking page in new tab"), {
        stopPropagation: () => {},
      });
      expect(open).toHaveBeenCalledWith("/(user)/locations/1", "_blank");
    } finally {
      delete (globalThis as unknown as { window?: unknown }).window;
    }
  });

  /**
   * A new tab is a browser idea, and a choice between two maps services is a browser's
   * question: a phone has one maps app, and the card hands the address to that one.
   */
  describe("off web", () => {
    it("drops the new-tab control, which would only repeat the card's own tap", async () => {
      await onPlatform("ios", async () => {
        render(<VenueCard venue={mockVenue} />);
        await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
        expect(screen.queryByLabelText("Open booking page in new tab")).toBeNull();
      });
    });

    it("offers one Directions pill in place of the Google and Apple pair", async () => {
      await onPlatform("ios", async () => {
        render(<VenueCard venue={mockVenue} />);
        await waitFor(() => expect(screen.getByTestId("card-directions")).toBeTruthy());
        expect(screen.queryByText("Google")).toBeNull();
        expect(screen.queryByText("Apple")).toBeNull();
        expect(screen.getByText("Directions")).toBeTruthy();
      });
    });

    it("hands an iPhone to Apple Maps", async () => {
      await onPlatform("ios", async () => {
        render(<VenueCard venue={mockVenue} />);
        await waitFor(() => expect(screen.getByTestId("card-directions")).toBeTruthy());
        fireEvent.press(screen.getByTestId("card-directions"), { stopPropagation: () => {} });
        expect(Linking.openURL).toHaveBeenCalledWith("maps://?q=123%20Main%20St");
      });
    });

    it("hands an Android phone to its maps app", async () => {
      await onPlatform("android", async () => {
        render(<VenueCard venue={mockVenue} />);
        await waitFor(() => expect(screen.getByTestId("card-directions")).toBeTruthy());
        fireEvent.press(screen.getByTestId("card-directions"), { stopPropagation: () => {} });
        expect(Linking.openURL).toHaveBeenCalledWith("geo:0,0?q=123%20Main%20St");
      });
    });

    it("sends an address-less card to the maps app with an empty query", async () => {
      await onPlatform("android", async () => {
        render(<VenueCard venue={{ ...mockVenue, address: undefined }} />);
        await waitFor(() => expect(screen.getByTestId("card-directions")).toBeTruthy());
        fireEvent.press(screen.getByTestId("card-directions"), { stopPropagation: () => {} });
        expect(Linking.openURL).toHaveBeenCalledWith("geo:0,0?q=");
      });
    });
  });

  it("presses a time slot to navigate with time and party", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-01T12:00:00Z"));
    try {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        slots: [{ time: "23:30", isAvailable: true }],
      });
      render(<VenueCard venue={mockVenue} party={3} />);
      await waitFor(() => expect(screen.getByText("23:30")).toBeTruthy());
      // Use fireEvent with explicit event object to avoid stopPropagation error
      fireEvent(screen.getByText("23:30"), "press", { stopPropagation: () => {} });
      expect(mockPush).toHaveBeenCalledWith(expect.stringContaining("time=23%3A30"));
    } finally {
      jest.useRealTimers();
    }
  });

  it("handles invalid timezone by falling back to local time in catch block", async () => {
    // An invalid IANA timezone causes Intl.DateTimeFormat to throw, exercising catch branches
    render(
      <VenueCard
        venue={{
          ...mockVenue,
          timezone: "Invalid/Timezone_XYZ",
          openTime: "00:00",
          closeTime: "23:59",
        }}
      />
    );
    await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
    // Component renders without crash; open/closed status is determined via fallback
    expect(fetchAvailability).toHaveBeenCalled();
  });

  it("handles openDays with unknown day string (parseDayOfWeek returns 0)", async () => {
    // "xyz" is not a valid day name; parseDayOfWeek returns 0 and it is filtered out
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2026-01-05T12:00:00Z")); // Monday
    try {
      render(
        <VenueCard
          venue={{
            ...mockVenue,
            openDays: "xyz,zzz",
            openTime: "00:00",
            closeTime: "23:59",
          }}
        />
      );
      // openDaysList is empty after filtering zeros, so isOpenNow skips the day check
      await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
    } finally {
      jest.useRealTimers();
    }
  });

  it("shows isOpenNow=true when openTime format is invalid (returns true early)", async () => {
    // When openTime doesn't contain ':', isNaN(oh) is true → isOpenNow returns true
    render(
      <VenueCard venue={{ ...mockVenue, openTime: "notavalidtime", closeTime: "alsoinvalid" }} />
    );
    // Should render the "Open till" badge (true branch of isOpenNow)
    await waitFor(() => {
      const el = screen.queryByText(/Open till/);
      expect(el).toBeTruthy();
    });
  });

  it("renders no tags when tags is empty", async () => {
    render(<VenueCard venue={{ ...mockVenue, tags: [] }} />);
    await waitFor(() => expect(screen.getByText("Test Venue")).toBeTruthy());
    expect(screen.queryByText("Dog friendly")).toBeNull();
  });

  describe("walk-in only", () => {
    // Today's ISO day in UTC (the card's timezone in these fixtures).
    const jsDay = new Date().getUTCDay();
    const todayIso = jsDay === 0 ? 7 : jsDay;

    beforeEach(() => {
      (fetchAvailability as jest.Mock).mockResolvedValue({
        venueId: 1,
        date: "2026-05-25",
        slots: [{ time: "19:00", isAvailable: true, availableResourceIds: [1], category: "PM" }],
      });
    });

    it("shows an empty state instead of slots and skips availability", () => {
      render(<VenueCard venue={{ ...mockVenue, walkInOnly: true }} />);
      expect(screen.getByTestId("walk-in-slot-notice")).toBeTruthy();
      expect(screen.getByText("No reservations required")).toBeTruthy();
      expect(screen.queryByText("Available slots")).toBeNull();
      expect(fetchAvailability).not.toHaveBeenCalled();
    });

    it("shows the walk-in badge for walk-in only locations", () => {
      render(<VenueCard venue={{ ...mockVenue, walkInOnly: true }} />);
      expect(screen.getByTestId("walk-in-badge")).toBeTruthy();
      expect(screen.getByText("Walk-ins only")).toBeTruthy();
    });

    it("shows an empty state when today is a walk-in day", () => {
      render(<VenueCard venue={{ ...mockVenue, walkInDays: String(todayIso) }} />);
      expect(screen.getByTestId("walk-in-slot-notice")).toBeTruthy();
      expect(screen.getByText("No reservations required")).toBeTruthy();
      expect(fetchAvailability).not.toHaveBeenCalled();
    });

    it("keeps normal slots when the walk-in day is not today", async () => {
      const otherDay = todayIso === 7 ? 1 : todayIso + 1;
      render(<VenueCard venue={{ ...mockVenue, walkInDays: String(otherDay) }} />);
      await waitFor(() => expect(screen.getByText("Available slots")).toBeTruthy());
      expect(screen.queryByTestId("walk-in-slot-notice")).toBeNull();
      expect(fetchAvailability).toHaveBeenCalled();
    });

    it("always shows the walk-in badge, even on a non-walk-in day", async () => {
      const otherDay = todayIso === 7 ? 1 : todayIso + 1;
      render(<VenueCard venue={{ ...mockVenue, walkInDays: String(otherDay) }} />);
      expect(screen.getByTestId("walk-in-badge")).toBeTruthy();
      // Slots still render normally for today alongside the badge.
      await waitFor(() => expect(screen.getByText("Available slots")).toBeTruthy());
    });

    it("lists multiple walk-in days by name in the badge", () => {
      render(<VenueCard venue={{ ...mockVenue, walkInDays: "6,7" }} />);
      expect(screen.getByText("Walk-ins on Saturdays and Sundays")).toBeTruthy();
    });

    it("shows the plain 'Walk-ins only' badge for a fully walk-in location, ignoring walkInDays", () => {
      render(<VenueCard venue={{ ...mockVenue, walkInOnly: true, walkInDays: "6,7" }} />);
      expect(screen.getByText("Walk-ins only")).toBeTruthy();
      expect(screen.queryByText("Walk-ins on Saturdays and Sundays")).toBeNull();
    });

    it("does not show the walk-in badge when no walk-in policy is configured", () => {
      render(<VenueCard venue={mockVenue} />);
      expect(screen.queryByTestId("walk-in-badge")).toBeNull();
    });
  });
});
