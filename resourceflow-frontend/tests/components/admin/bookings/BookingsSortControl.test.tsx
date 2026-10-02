import React from "react";
import { render, screen, fireEvent } from "@testing-library/react-native";
import { BookingsSortControl } from "@/components/admin/bookings/BookingsSortControl";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

const theme = {
  borderColor: "#ddd",
  cardBg: "#fff",
  mutedColor: "#888",
  primaryColor: "#0a7ea4",
};

describe("BookingsSortControl", () => {
  it("renders a chip per sort column", () => {
    render(
      <BookingsSortControl sort={{ key: "date", dir: "asc" }} onSortChange={() => {}} {...theme} />
    );
    expect(screen.getByText("Time")).toBeTruthy();
    expect(screen.getByText("Participant")).toBeTruthy();
    expect(screen.getByText("Group")).toBeTruthy();
    expect(screen.getByText("Resource")).toBeTruthy();
    expect(screen.getByText("Status")).toBeTruthy();
  });

  it("marks the active column as sorted with its direction in the label", () => {
    render(
      <BookingsSortControl
        sort={{ key: "guest", dir: "desc" }}
        onSortChange={() => {}}
        {...theme}
      />
    );
    expect(screen.getByLabelText("Sort by Participant, descending")).toBeTruthy();
    // Inactive chips are labeled "not sorted"
    expect(screen.getByLabelText("Sort by Time, not sorted")).toBeTruthy();
  });

  it("calls onSortChange with the column key when a chip is pressed", () => {
    const onSort = jest.fn();
    render(
      <BookingsSortControl sort={{ key: "date", dir: "asc" }} onSortChange={onSort} {...theme} />
    );
    fireEvent.press(screen.getByTestId("sort-chip-partySize"));
    expect(onSort).toHaveBeenCalledWith("partySize");
  });
});
