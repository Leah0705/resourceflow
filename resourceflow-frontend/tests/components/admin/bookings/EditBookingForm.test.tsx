import React from "react";
import { render, screen, fireEvent } from "@testing-library/react-native";
import { EditBookingForm } from "@/components/admin/bookings/EditBookingForm";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/context/BrandContext", () => {
  const brand = { primaryColor: "#0a7ea4", appName: "ResourceFlow" };
  return { useBrand: () => brand };
});

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

jest.mock("@/components/common/Select", () => {
  const { View, Text, Pressable } = require("react-native");
  return function MockSelect({
    selectedValue: _selectedValue,
    onSelect,
    options,
  }: {
    selectedValue?: number;
    onSelect: (v: number) => void;
    options: { label: string; value: number }[];
  }) {
    return (
      <View testID="mock-select">
        {options.map((o) => (
          <Pressable key={o.value} onPress={() => onSelect(o.value)}>
            <Text>{o.label}</Text>
          </Pressable>
        ))}
      </View>
    );
  };
});

jest.mock("@/components/common/DatePicker", () => {
  const { View, Text } = require("react-native");
  return function MockDatePicker({ selectedDate }: { selectedDate?: string }) {
    return (
      <View>
        <Text testID="date-picker">{selectedDate ?? "no date"}</Text>
      </View>
    );
  };
});

jest.mock("@/components/common/TimePicker", () => {
  const { View, Text } = require("react-native");
  return function MockTimePicker({ selectedTime }: { selectedTime?: string }) {
    return (
      <View>
        <Text testID="time-picker">{selectedTime ?? "no time"}</Text>
      </View>
    );
  };
});

const baseProps = {
  borderColor: "#ddd",
  loadingVenues: false,
  venueOptions: [
    { label: "Location A", value: 1 },
    { label: "Location B", value: 2 },
  ],
  sectionOptions: [{ label: "Main", value: 10 }],
  resourceOptions: [{ label: "Resource 1", value: 100 }],
  partySizeOptions: [
    { label: "1", value: 1 },
    { label: "2", value: 2 },
  ],
  editVenueId: 1,
  editSectionId: 10,
  editResourceId: 100,
  editPartySize: "2",
  editCustomerName: "Stub Name",
  editEmail: "test@example.com",
  editSpecialRequests: "Projector needed",
  editDate: "2026-10-01",
  editTime: "18:00",
  selectedVenue: { openTime: "09:00", closeTime: "22:00" },
  setEditResourceId: jest.fn(),
  setEditPartySize: jest.fn(),
  setEditCustomerName: jest.fn(),
  setEditEmail: jest.fn(),
  setEditSpecialRequests: jest.fn(),
  setEditDate: jest.fn(),
  setEditTime: jest.fn(),
  handleVenueChange: jest.fn(),
  handleSectionChange: jest.fn(),
};

describe("EditBookingForm", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("shows loading indicator and hides form when loadingVenues is true", () => {
    render(<EditBookingForm {...baseProps} loadingVenues />);
    // Venue options should not be visible while loading
    expect(screen.queryByText("Location A")).toBeNull();
    expect(screen.queryByText("Location B")).toBeNull();
  });

  it("renders location options", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByText("Location A")).toBeTruthy();
    expect(screen.getByText("Location B")).toBeTruthy();
  });

  it("renders section labels", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByText("Section")).toBeTruthy();
    expect(screen.getByText("Resource")).toBeTruthy();
  });

  it("renders date and time labels", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByText("Date")).toBeTruthy();
    expect(screen.getByText("Time")).toBeTruthy();
  });

  it("renders participants and email labels", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByText("Participants")).toBeTruthy();
    expect(screen.getByText("Participant email")).toBeTruthy();
  });

  it("renders special requests label and input", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByText("Special requests")).toBeTruthy();
    expect(screen.getByDisplayValue("Projector needed")).toBeTruthy();
  });

  it("renders email input with current value", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByDisplayValue("test@example.com")).toBeTruthy();
  });

  it("calls setEditEmail when email input changes", () => {
    render(<EditBookingForm {...baseProps} />);
    fireEvent.changeText(screen.getByDisplayValue("test@example.com"), "new@example.com");
    expect(baseProps.setEditEmail).toHaveBeenCalledWith("new@example.com");
  });

  it("calls setEditSpecialRequests when special requests input changes", () => {
    render(<EditBookingForm {...baseProps} />);
    fireEvent.changeText(screen.getByDisplayValue("Projector needed"), "Whiteboard please");
    expect(baseProps.setEditSpecialRequests).toHaveBeenCalledWith("Whiteboard please");
  });

  it("calls handleVenueChange when a location is selected", () => {
    render(<EditBookingForm {...baseProps} />);
    fireEvent.press(screen.getByText("Location B"));
    expect(baseProps.handleVenueChange).toHaveBeenCalledWith(2);
  });

  it("renders with null selectedVenue", () => {
    render(<EditBookingForm {...baseProps} selectedVenue={null} />);
    expect(screen.getByText("Location A")).toBeTruthy();
  });

  it("calls setEditResourceId when a resource option is selected", () => {
    render(<EditBookingForm {...baseProps} />);
    fireEvent.press(screen.getByText("Resource 1"));
    expect(baseProps.setEditResourceId).toHaveBeenCalledWith(100);
  });

  it("calls handleSectionChange when a section option is selected", () => {
    render(<EditBookingForm {...baseProps} />);
    fireEvent.press(screen.getByText("Main"));
    expect(baseProps.handleSectionChange).toHaveBeenCalledWith(10);
  });

  it("calls setEditPartySize when a participant count option is selected", () => {
    render(<EditBookingForm {...baseProps} />);
    const guestOptions = screen.getAllByText("1");
    fireEvent.press(guestOptions[0]);
    expect(baseProps.setEditPartySize).toHaveBeenCalledWith("1");
  });

  it("renders date picker with current date value", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByTestId("date-picker")).toBeTruthy();
    expect(screen.getByText("2026-10-01")).toBeTruthy();
  });

  it("renders time picker with current time value", () => {
    render(<EditBookingForm {...baseProps} />);
    expect(screen.getByTestId("time-picker")).toBeTruthy();
    expect(screen.getByText("18:00")).toBeTruthy();
  });
});
