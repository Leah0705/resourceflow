/**
 * @jest-environment jsdom
 */
import React from "react";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react-native";
import { VenueInfoForm } from "@/components/admin/settings/VenueInfoForm";
import * as venuesApi from "@/api/venues";
import { useColorScheme } from "@/hooks/use-color-scheme";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/api/venues", () => ({
  updateVenue: jest.fn(),
  uploadGuideFile: jest.fn(),
  deleteGuideFile: jest.fn(),
}));

jest.mock("@/context/BrandContext", () => {
  const brand = { primaryColor: "#0a7ea4", appName: "ResourceFlow" };
  return { useBrand: () => brand };
});

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: jest.fn(() => "light"),
}));

jest.mock("@/hooks/use-persisted-state", () => ({
  usePersistedState: (_key: string, defaultValue: unknown) => {
    const { useState } = require("react");
    return useState(defaultValue);
  },
}));

jest.mock("@/components/common/TimePicker", () => {
  const { View, Text, Pressable } = require("react-native");
  return {
    __esModule: true,
    default: ({
      selectedTime,
      onSelect,
    }: {
      selectedTime: string;
      onSelect: (t: string) => void;
    }) => (
      <View>
        <Text testID="time-picker">{selectedTime}</Text>
        <Pressable onPress={() => onSelect("10:00")}>
          <Text>Pick Time</Text>
        </Pressable>
      </View>
    ),
  };
});

const mockVenue = {
  id: 1,
  name: "Test Location",
  address: "123 Main St",
  openTime: "09:00",
  closeTime: "22:00",
  openDays: "1,2,3,4,5",
  timezone: "UTC",
  defaultBookingDurationMinutes: 90,
  tags: ["projector", "accessible"],
  sections: [],
};

/**
 * Waits out the autosave debounce and lets the save promise settle. Real timers rather than
 * `jest.advanceTimersByTime`: this file also drives promise-based UI (the guide upload/delete
 * flows), and RNTL's `waitFor` cannot make progress against a faked clock.
 */
const flushAutosave = async () => {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 900));
  });
};

describe("VenueInfoForm", () => {
  const onSaved = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders the location name", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByDisplayValue("Test Location")).toBeTruthy();
  });

  it("renders the address field", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByDisplayValue("123 Main St")).toBeTruthy();
  });

  it("renders open days as toggleable buttons", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText("Monday")).toBeTruthy();
    expect(screen.getByText("Sunday")).toBeTruthy();
  });

  it("shows tags", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText("projector")).toBeTruthy();
    expect(screen.getByText("accessible")).toBeTruthy();
  });

  it("shows the autosave status row instead of a Save button", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByTestId("location-save-status")).toBeTruthy();
    expect(screen.queryByText("Save changes")).toBeNull();
  });

  it("shows All changes saved status by default", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
  });

  it("shows Unsaved changes when form is dirty", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "New Name");
  });

  it("calls updateVenue when Save is pressed after editing", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      name: "Updated Location",
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Updated Location");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ name: "Updated Location" })
    );
  });

  it("allows name to be edited", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "New Name");
    expect(screen.getByDisplayValue("New Name")).toBeTruthy();
  });

  it("toggles a day open/closed when pressed", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    // Saturday (day 6) is not in openDays "1,2,3,4,5" — pressing it should include it
    fireEvent.press(screen.getByText("Saturday"));
    // Component should still render correctly after toggle
    expect(screen.getByText("Saturday")).toBeTruthy();
  });

  it("deselects an active day when pressed again", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    // Monday (day 1) is active in "1,2,3,4,5" — pressing it deselects it
    fireEvent.press(screen.getByText("Monday"));
    expect(screen.getByText(/4 of 7 days open/)).toBeTruthy();
  });

  it("adds a tag via onSubmitEditing", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    const tagInput = screen.getByPlaceholderText("Add tag (press Enter)");
    fireEvent.changeText(tagInput, "whiteboard");
    fireEvent(tagInput, "submitEditing");
    expect(screen.getByText("whiteboard")).toBeTruthy();
    // Input should be cleared
    expect(screen.getByPlaceholderText("Add tag (press Enter)")).toBeTruthy();
  });

  it("does not add a duplicate tag", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    const tagInput = screen.getByPlaceholderText("Add tag (press Enter)");
    fireEvent.changeText(tagInput, "projector");
    fireEvent(tagInput, "submitEditing");
    // Only one "projector" text should exist (not two)
    expect(screen.getAllByText("projector")).toHaveLength(1);
  });

  it("adds a tag via onBlur when input has value", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    const tagInput = screen.getByPlaceholderText("Add tag (press Enter)");
    fireEvent.changeText(tagInput, "parking");
    fireEvent(tagInput, "blur");
    expect(screen.getByText("parking")).toBeTruthy();
  });

  it("removes a tag when its remove button is pressed", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText("projector")).toBeTruthy();
    fireEvent.press(screen.getByTestId("remove-tag-projector"));
    expect(screen.queryByText("projector")).toBeNull();
    expect(screen.getByText("accessible")).toBeTruthy();
  });

  it("autosaves a tag committed from the tag input", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      name: "Updated Location",
      tags: ["projector", "accessible", "quiet"],
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Updated Location");
    const tagInput = screen.getByPlaceholderText("Add tag (press Enter)");
    fireEvent.changeText(tagInput, "quiet");
    fireEvent(tagInput, "submitEditing");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ tags: expect.stringContaining("quiet") })
    );
    expect(onSaved).toHaveBeenCalled();
  });

  it("does not call onSaved when updateVenue returns null", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue(null);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Updated Location");
    await flushAutosave();
    expect(onSaved).not.toHaveBeenCalled();
  });

  // ── Booking duration ───────────────────────────────────────────────────
  // The booking controls are the shared `Select`: its trigger renders the selected option's
  // label, so a selection is asserted by that label and changed by pressing the trigger and
  // then the option inside the modal.
  const chooseOption = (currentLabel: string, nextLabel: string) => {
    fireEvent.press(screen.getByText(currentLabel));
    fireEvent.press(screen.getByText(nextLabel));
  };

  it("renders the booking duration select at the location's saved value", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText("1h 30m")).toBeTruthy();
  });

  it("defaults the booking duration to 1h (60 minutes) when the location has none set", () => {
    render(
      <VenueInfoForm
        venue={{
          ...mockVenue,
          defaultBookingDurationMinutes: undefined as unknown as number,
        }}
        onSaved={onSaved}
      />
    );
    expect(screen.getByText("1h")).toBeTruthy();
  });

  it("includes the saved defaultBookingDurationMinutes in the save payload", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      name: "Updated Location",
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Updated Location");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ defaultBookingDurationMinutes: 90 })
    );
  });

  it("marks the form dirty and updates the selection when the booking duration changes", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption("1h 30m", "2h");
    expect(screen.getByText("2h")).toBeTruthy();
  });

  it("saves the newly selected booking duration", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      defaultBookingDurationMinutes: 120,
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption("1h 30m", "2h");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ defaultBookingDurationMinutes: 120 })
    );
    expect(onSaved).toHaveBeenCalledWith(
      expect.objectContaining({ defaultBookingDurationMinutes: 120 })
    );
  });

  // ── Booking start-time interval ─────────────────────────────────────────

  it("renders the slot interval select at the location's saved value", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText("30m")).toBeTruthy();
  });

  it("defaults the slot interval to 30 minutes when the location has none set", () => {
    render(
      <VenueInfoForm
        venue={{
          ...mockVenue,
          bookingSlotIntervalMinutes: undefined as unknown as number,
        }}
        onSaved={onSaved}
      />
    );
    expect(screen.getByText("30m")).toBeTruthy();
  });

  it("marks the form dirty and updates the selection when the slot interval changes", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption("30m", "15m");
    expect(screen.getByText("15m")).toBeTruthy();
  });

  it("includes the slot interval in the save payload", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      bookingSlotIntervalMinutes: 15,
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Updated Location");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ bookingSlotIntervalMinutes: 30 })
    );
  });

  it("saves the newly selected slot interval", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      bookingSlotIntervalMinutes: 60,
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption("30m", "1h");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ bookingSlotIntervalMinutes: 60 })
    );
    expect(onSaved).toHaveBeenCalledWith(
      expect.objectContaining({ bookingSlotIntervalMinutes: 60 })
    );
  });

  // ── Max resource oversize ──────────────────────────────────────────────────
  // "Off" is the sentinel the picker carries for the API's null; selecting a number sends
  // that integer.

  it("renders the oversize select at Off when the location has none set", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText("Off")).toBeTruthy();
  });

  it("renders the oversize select at the location's saved value", () => {
    render(<VenueInfoForm venue={{ ...mockVenue, maxSpareCapacity: 2 }} onSaved={onSaved} />);
    expect(screen.getByText("+2 places")).toBeTruthy();
  });

  it("marks the form dirty and updates the selection when the oversize changes", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption("Off", "+1 place");
    expect(screen.getByText("+1 place")).toBeTruthy();
  });

  it("saves the newly selected oversize cap", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      maxSpareCapacity: 1,
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption("Off", "+1 place");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ maxSpareCapacity: 1 })
    );
    expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ maxSpareCapacity: 1 }));
  });

  it("saves null (Off) when the Off option is re-selected on a capped location", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      maxSpareCapacity: null,
    });
    render(<VenueInfoForm venue={{ ...mockVenue, maxSpareCapacity: 2 }} onSaved={onSaved} />);
    chooseOption("+2 places", "Off");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ maxSpareCapacity: null })
    );
  });

  // ── Guest pacing ────────────────────────────────────────────────────────

  it("saves a typed guest cap as a number", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      maxGuestsPerSlot: 12,
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByTestId("max-guests-input"), "12");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ maxGuestsPerSlot: 12 })
    );
    expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ maxGuestsPerSlot: 12 }));
  });

  it("clears the guest cap when the field is emptied", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue(mockVenue);
    render(<VenueInfoForm venue={{ ...mockVenue, maxGuestsPerSlot: 12 }} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByTestId("max-guests-input"), "");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ maxGuestsPerSlot: null })
    );
  });

  it.each(["0", "2.5", "abc"])("holds back a guest cap of %s and says why", async (typed) => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByTestId("max-guests-input"), typed);
    await flushAutosave();
    expect(venuesApi.updateVenue).not.toHaveBeenCalled();
    expect(screen.getByText(/Max participants per slot must be a whole number/)).toBeTruthy();
  });

  // ── Booking reference format ────────────────────────────────────────────
  // Option values are the backend BookingRefFormat member names, sent verbatim.
  const WORDS_FORMAT = "Words (swift-cedar-meadow)";
  const NUMBERS_FORMAT = "Numbers (48273910)";

  it("renders the booking ref format select at AlphaNumeric when the location has none set", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText(WORDS_FORMAT)).toBeTruthy();
  });

  it("renders the booking ref format select at the location's saved value", () => {
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, bookingRefFormat: "Numeric" as const }}
        onSaved={onSaved}
      />
    );
    expect(screen.getByText(NUMBERS_FORMAT)).toBeTruthy();
  });

  it("marks the form dirty and updates the selection when the ref format changes", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption(WORDS_FORMAT, NUMBERS_FORMAT);
    expect(screen.getByText(NUMBERS_FORMAT)).toBeTruthy();
  });

  it("saves the newly selected ref format", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      bookingRefFormat: "Numeric",
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    chooseOption(WORDS_FORMAT, NUMBERS_FORMAT);
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ bookingRefFormat: "Numeric" })
    );
    expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ bookingRefFormat: "Numeric" }));
  });

  // ── Per-day opening hours ────────────────────────────────────────────────

  const uniformWeek = [1, 2, 3, 4, 5, 6, 7].map((day) => ({
    day,
    open: "09:00",
    close: "22:00",
  }));

  const customWeek = uniformWeek.map((h) =>
    h.day === 6 ? { ...h, open: "11:00", close: "23:00" } : h
  );

  const customVenue = { ...mockVenue, openHours: customWeek };

  it("starts in uniform mode when hours are the same every day", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    // Uniform mode shows the full-name day chips
    expect(screen.getByText("Monday")).toBeTruthy();
    expect(screen.queryByTestId("day-toggle-1")).toBeNull();
  });

  it("starts in custom mode when the location has per-day hours", () => {
    render(<VenueInfoForm venue={customVenue} onSaved={onSaved} />);
    expect(screen.getByTestId("day-toggle-1")).toBeTruthy();
    expect(screen.getByTestId("day-toggle-7")).toBeTruthy();
  });

  it("switches to custom mode and shows 7 day rows", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.press(screen.getByTestId("hours-mode-custom"));
    for (let day = 1; day <= 7; day++) {
      expect(screen.getByTestId(`day-toggle-${day}`)).toBeTruthy();
    }
  });

  it("shows Closed for days not in openDays in custom mode", () => {
    // mockVenue is open Mon–Fri only
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.press(screen.getByTestId("hours-mode-custom"));
    expect(screen.getAllByText("Closed")).toHaveLength(2); // Sat + Sun
  });

  it("toggling a closed day open in custom mode reveals its time pickers", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.press(screen.getByTestId("hours-mode-custom"));
    fireEvent.press(screen.getByTestId("day-toggle-6"));
    expect(screen.getAllByText("Closed")).toHaveLength(1); // only Sunday left
  });

  it("does not mark the form dirty when only the mode is toggled", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.press(screen.getByTestId("hours-mode-custom"));
  });

  it("saves per-day hours after editing a single day", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      openHours: customWeek,
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.press(screen.getByTestId("hours-mode-custom"));
    // Mock TimePicker's "Pick Time" sets 10:00; first picker is Monday's opening time
    fireEvent.press(screen.getAllByText("Pick Time")[0]);
    await flushAutosave();
    const payload = (venuesApi.updateVenue as jest.Mock).mock.calls[0][1];
    expect(payload.openHours).toHaveLength(7);
    expect(payload.openHours[0]).toEqual({ day: 1, open: "10:00", close: "22:00" });
    expect(payload.openHours[1]).toEqual({ day: 2, open: "09:00", close: "22:00" });
    expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ openHours: customWeek }));
  });

  it("saves uniform hours for all 7 days in uniform mode", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue(mockVenue);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    // First picker in uniform mode is "Opens"; the mock sets it to 10:00
    fireEvent.press(screen.getAllByText("Pick Time")[0]);
    await flushAutosave();
    const payload = (venuesApi.updateVenue as jest.Mock).mock.calls[0][1];
    expect(payload.openTime).toBe("10:00");
    expect(payload.openHours).toHaveLength(7);
    expect(payload.openHours.every((h: { open: string }) => h.open === "10:00")).toBe(true);
  });

  it("copies one day's hours to the whole week", async () => {
    // Open Saturday so its row shows time pickers and the copy button
    const withSaturday = { ...customVenue, openDays: "1,2,3,4,5,6" };
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue(withSaturday);
    render(<VenueInfoForm venue={withSaturday} onSaved={onSaved} />);
    // Saturday (day 6) has 11:00–23:00; copy it everywhere
    fireEvent.press(screen.getByTestId("copy-hours-6"));
    await flushAutosave();
    const payload = (venuesApi.updateVenue as jest.Mock).mock.calls[0][1];
    expect(
      payload.openHours.every(
        (h: { open: string; close: string }) => h.open === "11:00" && h.close === "23:00"
      )
    ).toBe(true);
  });

  it("shows the after-midnight hint when closing time is before opening", () => {
    const overnight = {
      ...mockVenue,
      openTime: "18:00",
      closeTime: "02:00",
    };
    render(<VenueInfoForm venue={overnight} onSaved={onSaved} />);
    expect(screen.getByText(/closes after midnight/)).toBeTruthy();
  });

  describe("reservations / walk-in policy", () => {
    it("shows the online-bookings mode and day chips by default", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      expect(screen.getByText("Walk-in Policy")).toBeTruthy();
      expect(screen.getByText("Online bookings on every open day")).toBeTruthy();
      expect(screen.getByTestId("walkin-day-1")).toBeTruthy();
      expect(screen.getByTestId("walkin-day-7")).toBeTruthy();
    });

    it("switching to walk-ins only hides the day chips and marks the form dirty", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      fireEvent.press(screen.getByTestId("walkin-mode-walkin"));
      expect(screen.getByText("Walk-ins only, online booking is off")).toBeTruthy();
      expect(screen.queryByTestId("walkin-day-1")).toBeNull();
    });

    it("saves walkInOnly=true when toggled", async () => {
      (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
        ...mockVenue,
        walkInOnly: true,
        walkInDays: "",
      });
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      fireEvent.press(screen.getByTestId("walkin-mode-walkin"));
      await flushAutosave();
      expect(venuesApi.updateVenue).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ walkInOnly: true, walkInDays: "" })
      );
      expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ walkInOnly: true }));
    });

    it("toggles walk-in days and saves the joined list", async () => {
      (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
        ...mockVenue,
        walkInDays: "6,7",
      });
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      fireEvent.press(screen.getByTestId("walkin-day-6"));
      fireEvent.press(screen.getByTestId("walkin-day-7"));
      expect(screen.getByText("Walk-ins only on 2 days")).toBeTruthy();
      await flushAutosave();
      expect(venuesApi.updateVenue).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ walkInOnly: false, walkInDays: "6,7" })
      );
    });

    it("unselecting a walk-in day removes it from the payload", () => {
      const withWalkIn = { ...mockVenue, walkInDays: "6" };
      render(<VenueInfoForm venue={withWalkIn} onSaved={onSaved} />);
      expect(screen.getByText("Walk-ins only on 1 day")).toBeTruthy();
      fireEvent.press(screen.getByTestId("walkin-day-6"));
      expect(screen.getByText("Online bookings on every open day")).toBeTruthy();
    });
  });

  // ── Fallback branches for unset optional venue fields ─────────────
  // VenueDto marks address/tags/walkInOnly/walkInDays/defaultBookingDurationMinutes as
  // optional, and even openTime/closeTime/timezone (typed as required strings) are read
  // defensively with `??` in case the API ever omits them. mockVenue always supplies
  // every field, so those `??` fallback branches (state init, initialOpenHours, the dirty
  // check, and discard()) were never exercised on the "value is missing" side.
  const sparseVenue = {
    id: 2,
    name: "Sparse Location",
    openDays: "1,2,3,4,5,6,7",
    openTime: undefined as unknown as string,
    closeTime: undefined as unknown as string,
    timezone: undefined as unknown as string,
    sections: [],
  };

  // ── Duration rules by party size ─────────────────────────────────────────

  it("adds a duration rule from the first participant count at the default duration and saves it", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      durationRules: [{ minPartySize: 1, minutes: 90 }],
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.press(screen.getByText("Add a rule"));
    expect(screen.getByText("Groups of 1 or more")).toBeTruthy();
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ durationRules: [{ minPartySize: 1, minutes: 90 }] })
    );
    expect(onSaved).toHaveBeenCalledWith(
      expect.objectContaining({ durationRules: [{ minPartySize: 1, minutes: 90 }] })
    );
  });

  it("shows the party sizes each saved rule covers", () => {
    render(
      <VenueInfoForm
        venue={{
          ...mockVenue,
          durationRules: [
            { minPartySize: 1, minutes: 60 },
            { minPartySize: 3, minutes: 90 },
            { minPartySize: 5, minutes: 120 },
          ],
        }}
        onSaved={onSaved}
      />
    );
    expect(screen.getByText("Groups of 1–2")).toBeTruthy();
    expect(screen.getByText("Groups of 3–4")).toBeTruthy();
    expect(screen.getByText("Groups of 5 or more")).toBeTruthy();
  });

  it("starts the next rule one participant above the last", () => {
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, durationRules: [{ minPartySize: 4, minutes: 120 }] }}
        onSaved={onSaved}
      />
    );
    fireEvent.press(screen.getByText("Add a rule"));
    expect(screen.getByText("Groups of 4")).toBeTruthy();
    expect(screen.getByText("Groups of 5 or more")).toBeTruthy();
  });

  it("saves a new length for a rule", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      durationRules: [{ minPartySize: 5, minutes: 180 }],
    });
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, durationRules: [{ minPartySize: 5, minutes: 120 }] }}
        onSaved={onSaved}
      />
    );
    chooseOption("2h", "3h");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ durationRules: [{ minPartySize: 5, minutes: 180 }] })
    );
  });

  it("saves an empty list when the last rule is removed", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      durationRules: [],
    });
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, durationRules: [{ minPartySize: 5, minutes: 120 }] }}
        onSaved={onSaved}
      />
    );
    fireEvent.press(screen.getByLabelText("Remove the duration rule from 5"));
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ durationRules: [] })
    );
  });

  it("holds the save while two rules start at the same participant count", async () => {
    render(
      <VenueInfoForm
        venue={{
          ...mockVenue,
          durationRules: [
            { minPartySize: 3, minutes: 90 },
            { minPartySize: 4, minutes: 120 },
          ],
        }}
        onSaved={onSaved}
      />
    );
    fireEvent.press(screen.getByText("From 4 participants"));
    // The first row's trigger already reads "From 3 participants"; the open list's option is the last.
    const options = screen.getAllByText("From 3 participants");
    fireEvent.press(options[options.length - 1]);
    await flushAutosave();
    expect(
      screen.getByText("Two duration rules start at the same participant count.")
    ).toBeTruthy();
    expect(venuesApi.updateVenue).not.toHaveBeenCalled();
  });

  it("falls back to defaults when optional location fields are unset", () => {
    render(<VenueInfoForm venue={sparseVenue} onSaved={onSaved} />);
    expect(screen.getByDisplayValue("Sparse Location")).toBeTruthy();
    expect(screen.getByPlaceholderText("e.g. 123 Main St")).toBeTruthy();
    expect(screen.getByText("1h")).toBeTruthy();
    expect(screen.getByText("30m")).toBeTruthy();
    expect(screen.getByText("Online bookings on every open day")).toBeTruthy();
  });

  it("saves address as null when the address field is cleared to blank", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      address: null,
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("123 Main St"), "   ");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ address: null })
    );
  });

  it("renders without crashing in dark mode", () => {
    (useColorScheme as jest.Mock).mockReturnValueOnce("dark");
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByDisplayValue("Test Location")).toBeTruthy();
  });

  it("reports Saving… while the request is in flight, then Saved", async () => {
    let resolveUpdate: (value: unknown) => void = () => {};
    (venuesApi.updateVenue as jest.Mock).mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveUpdate = resolve;
        })
    );
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Updated Location");
    await flushAutosave();
    expect(await screen.findByText("Saving…")).toBeTruthy();
    await act(async () => {
      resolveUpdate({ ...mockVenue, name: "Updated Location" });
    });
    expect(screen.getByText("Saved")).toBeTruthy();
  });

  it("reports the failure and offers a retry when the save is rejected", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue(null);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Updated Location");
    await flushAutosave();
    expect(screen.getByText("Couldn't reach the server.")).toBeTruthy();

    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      name: "Updated Location",
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Retry"));
    });
    expect(screen.getByText("Saved")).toBeTruthy();
  });

  // ── Description blurb ─────────────────────────────────────────────────────

  it("renders the description field (empty when unset)", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(
      screen.getByPlaceholderText(
        "Short blurb shown on the location page. Supports links like [guide](https://example.com)."
      )
    ).toBeTruthy();
  });

  it("pre-fills the description field from the location", () => {
    render(
      <VenueInfoForm venue={{ ...mockVenue, description: "A cozy spot." }} onSaved={onSaved} />
    );
    expect(screen.getByDisplayValue("A cozy spot.")).toBeTruthy();
  });

  it("marks the form dirty and saves the description when edited", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      description: "Our little place",
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(
      screen.getByPlaceholderText(
        "Short blurb shown on the location page. Supports links like [guide](https://example.com)."
      ),
      "Our little place"
    );
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ description: "Our little place" })
    );
    expect(onSaved).toHaveBeenCalledWith(
      expect.objectContaining({ description: "Our little place" })
    );
  });

  it("saves description as an empty string when cleared to blank", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      description: null,
    });
    render(
      <VenueInfoForm venue={{ ...mockVenue, description: "Existing blurb" }} onSaved={onSaved} />
    );
    fireEvent.changeText(screen.getByDisplayValue("Existing blurb"), "   ");
    await flushAutosave();
    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      // "" clears server-side; null would be read as "leave untouched" and the blurb would stick.
      expect.objectContaining({ description: "" })
    );
  });

  // ── Guide file upload ────────────────────────────────────────────────────
  // Admins can either paste an external link OR upload a PDF — both reuse
  // Venue.GuideUrl. A served file is recognized by its /media/guide-<id>.pdf
  // shape, which swaps the link input for a "Remove file" affordance.

  it("shows the Upload PDF button and the link input when no guide is set", () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    expect(screen.getByText("Upload PDF")).toBeTruthy();
    expect(screen.getByPlaceholderText("https://your-site.com/guide.pdf")).toBeTruthy();
  });

  it("pre-fills the link input when the location has an external guide URL", () => {
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, guideUrl: "https://example.com/guide.pdf" }}
        onSaved={onSaved}
      />
    );
    expect(screen.getByDisplayValue("https://example.com/guide.pdf")).toBeTruthy();
  });

  it("shows Remove file instead of the link input when a PDF is uploaded", () => {
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, guideUrl: "/media/guide-1.pdf?v=123" }}
        onSaved={onSaved}
      />
    );
    expect(screen.getByText("Uploaded guide PDF")).toBeTruthy();
    expect(screen.getByText("Remove file")).toBeTruthy();
    expect(screen.queryByPlaceholderText("https://your-site.com/guide.pdf")).toBeNull();
    expect(screen.queryByText("Upload PDF")).toBeNull();
  });

  it("calls uploadGuideFile and onSaved when a PDF is selected", async () => {
    (venuesApi.uploadGuideFile as jest.Mock).mockResolvedValue("/media/guide-1.pdf?v=1");
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [new File(["pdf-bytes"], "guide.pdf", { type: "application/pdf" })],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload PDF"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    expect(venuesApi.uploadGuideFile).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ type: "application/pdf" })
    );
    expect(onSaved).toHaveBeenCalledWith({ guideUrl: "/media/guide-1.pdf?v=1" });
  });

  it("shows Guide uploaded message after successful upload", async () => {
    (venuesApi.uploadGuideFile as jest.Mock).mockResolvedValue("/media/guide-1.pdf?v=1");
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [new File(["pdf-bytes"], "guide.pdf", { type: "application/pdf" })],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload PDF"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(screen.getByText("Guide uploaded.")).toBeTruthy();
    });
  });

  it("shows an error message when upload fails", async () => {
    (venuesApi.uploadGuideFile as jest.Mock).mockResolvedValue(null);
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [new File(["pdf-bytes"], "guide.pdf", { type: "application/pdf" })],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload PDF"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(screen.getByText("Failed to upload guide.")).toBeTruthy();
    });
  });

  it("shows a size error when the selected file exceeds 10 MB", async () => {
    const largeFile = new File(["x".repeat(11 * 1024 * 1024)], "big.pdf", {
      type: "application/pdf",
    });
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [largeFile],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload PDF"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(screen.getByText("Guide file must be under 10 MB.")).toBeTruthy();
    });
    expect(venuesApi.uploadGuideFile).not.toHaveBeenCalled();
  });

  it("calls deleteGuideFile and onSaved with null guideUrl when Remove file is pressed", async () => {
    (venuesApi.deleteGuideFile as jest.Mock).mockResolvedValue(true);
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, guideUrl: "/media/guide-1.pdf?v=123" }}
        onSaved={onSaved}
      />
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Remove file"));
    });
    expect(venuesApi.deleteGuideFile).toHaveBeenCalledWith(1);
    expect(onSaved).toHaveBeenCalledWith({ guideUrl: null });
  });

  it("shows Guide removed message after successful delete", async () => {
    (venuesApi.deleteGuideFile as jest.Mock).mockResolvedValue(true);
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, guideUrl: "/media/guide-1.pdf?v=123" }}
        onSaved={onSaved}
      />
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Remove file"));
    });
    await waitFor(() => {
      expect(screen.getByText("Guide removed.")).toBeTruthy();
    });
  });

  it("shows an error when delete fails", async () => {
    (venuesApi.deleteGuideFile as jest.Mock).mockResolvedValue(false);
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, guideUrl: "/media/guide-1.pdf?v=123" }}
        onSaved={onSaved}
      />
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Remove file"));
    });
    await waitFor(() => {
      expect(screen.getByText("Failed to remove guide.")).toBeTruthy();
    });
  });

  it("does nothing when no file is selected in the picker", async () => {
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload PDF"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    expect(venuesApi.uploadGuideFile).not.toHaveBeenCalled();
  });

  it("blocks save with an inline error when guideUrl is an invalid link (pre-flight)", async () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    const guideInput = screen.getByPlaceholderText("https://your-site.com/guide.pdf");
    fireEvent.changeText(guideInput, "javascript:alert(1)");
    await flushAutosave();
    await waitFor(() => expect(screen.getByText(/valid http\(s\) link/)).toBeTruthy());
    // Pre-flight means the API was never called.
    expect(venuesApi.updateVenue).not.toHaveBeenCalled();
  });
  // ── Contact info ──────────────────────────────────────────────────────

  it("renders stored contact fields", () => {
    render(
      <VenueInfoForm
        venue={{
          ...mockVenue,
          phoneNumber: "+44 20 7946 0958",
          emailAddress: "hello@example.com",
        }}
        onSaved={onSaved}
      />
    );
    expect(screen.getByDisplayValue("+44 20 7946 0958")).toBeTruthy();
    expect(screen.getByDisplayValue("hello@example.com")).toBeTruthy();
  });

  it("saves trimmed contact fields", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      phoneNumber: "+1 555 0100",
      emailAddress: "hi@example.com",
    });
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);

    fireEvent.changeText(screen.getByPlaceholderText("e.g. +44 20 7946 0958"), " +1 555 0100 ");
    fireEvent.changeText(
      screen.getByPlaceholderText("e.g. bookings@example.com"),
      " hi@example.com "
    );
    await flushAutosave();

    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ phoneNumber: "+1 555 0100", emailAddress: "hi@example.com" })
    );
    await waitFor(() =>
      expect(onSaved).toHaveBeenCalledWith(
        expect.objectContaining({ phoneNumber: "+1 555 0100", emailAddress: "hi@example.com" })
      )
    );
  });

  it("sends an empty string to clear a contact field (PATCH convention)", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue(mockVenue);
    render(
      <VenueInfoForm venue={{ ...mockVenue, phoneNumber: "+1 555 0100" }} onSaved={onSaved} />
    );

    fireEvent.changeText(screen.getByPlaceholderText("e.g. +44 20 7946 0958"), "");
    await flushAutosave();

    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ phoneNumber: "" })
    );
  });

  it("blocks save with an inline error when the contact email is malformed (pre-flight)", async () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByPlaceholderText("e.g. bookings@example.com"), "not-an-email");
    await flushAutosave();
    await waitFor(() => expect(screen.getByText(/valid email address/)).toBeTruthy());
    expect(venuesApi.updateVenue).not.toHaveBeenCalled();
  });

  it("blocks save with an inline error when the contact phone exceeds the cap", async () => {
    render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
    fireEvent.changeText(screen.getByPlaceholderText("e.g. +44 20 7946 0958"), "9".repeat(33));
    await flushAutosave();
    await waitFor(() => expect(screen.getByText(/cannot exceed 32 characters/)).toBeTruthy());
    expect(venuesApi.updateVenue).not.toHaveBeenCalled();
  });
  it("sends an empty string to clear a pasted guide link", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue({
      ...mockVenue,
      guideUrl: null,
    });
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, guideUrl: "https://example.com/guide.pdf" }}
        onSaved={onSaved}
      />
    );

    fireEvent.changeText(screen.getByDisplayValue("https://example.com/guide.pdf"), "");
    await flushAutosave();

    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      // "" clears; null would be read as "leave untouched" and the link would stick.
      expect.objectContaining({ guideUrl: "" })
    );
  });

  it("sends null for guideUrl while an uploaded file is the stored guide", async () => {
    (venuesApi.updateVenue as jest.Mock).mockResolvedValue(mockVenue);
    render(
      <VenueInfoForm
        venue={{ ...mockVenue, guideUrl: "/media/guide-1.pdf?v=123" }}
        onSaved={onSaved}
      />
    );

    // The upload flow leaves local guideUrl blank while the served path is stored; sending ""
    // here would wipe the freshly-uploaded file, so this save must leave the field untouched.
    fireEvent.changeText(screen.getByDisplayValue("Test Location"), "Renamed Location");
    await flushAutosave();

    expect(venuesApi.updateVenue).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ guideUrl: null })
    );
  });

  describe("card accordions", () => {
    it("collapses the Basic Info card when its header is pressed", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      expect(screen.getByDisplayValue("Test Location")).toBeTruthy();
      fireEvent.press(screen.getByText("Basic Info"));
      expect(screen.queryByDisplayValue("Test Location")).toBeNull();
    });

    it("collapses the Guide card when its header is pressed", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      expect(screen.getByText("Upload PDF")).toBeTruthy();
      fireEvent.press(screen.getByText("Guide"));
      expect(screen.queryByText("Upload PDF")).toBeNull();
    });

    it("collapses the Contact card when its header is pressed", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      expect(screen.getByPlaceholderText("e.g. +44 20 7946 0958")).toBeTruthy();
      fireEvent.press(screen.getByText("Contact"));
      expect(screen.queryByPlaceholderText("e.g. +44 20 7946 0958")).toBeNull();
    });

    it("collapses the Booking Settings card when its header is pressed", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);
      expect(screen.getByText("Default booking duration")).toBeTruthy();
      fireEvent.press(screen.getByText("Booking Settings"));
      expect(screen.queryByText("Default booking duration")).toBeNull();
    });
  });

  // Changing a location's timezone reinterprets the local wall-clock time of every booking it
  // already holds, without moving the booking or telling the guest. A warning, not a
  // gate: the bookings are correct, only the time the guest was told has drifted.
  describe("timezone warning", () => {
    it("warns when the location already holds upcoming bookings", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} upcomingBookingsCount={3} />);

      expect(screen.getByTestId("timezone-rebase-warning")).toBeTruthy();
      expect(screen.getByText(/3 upcoming bookings on the books/)).toBeTruthy();
    });

    it("stays out of the way when the location holds none", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} upcomingBookingsCount={0} />);

      expect(screen.queryByTestId("timezone-rebase-warning")).toBeNull();
    });

    it("keeps the count singular for one booking", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} upcomingBookingsCount={1} />);

      expect(screen.getByText(/1 upcoming booking on the books/)).toBeTruthy();
    });

    it("hides when the count is absent, rather than assuming there are bookings", () => {
      render(<VenueInfoForm venue={mockVenue} onSaved={onSaved} />);

      expect(screen.queryByTestId("timezone-rebase-warning")).toBeNull();
    });
  });
});
