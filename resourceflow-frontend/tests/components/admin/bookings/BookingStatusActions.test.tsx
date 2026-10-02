import React from "react";
import { fireEvent, render, screen } from "@testing-library/react-native";
import { BookingStatusActions } from "@/components/admin/bookings/BookingStatusActions";
import type { BookingDetailDto } from "@/api/admin";

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

const booking = (overrides: Partial<BookingDetailDto>): BookingDetailDto => ({
  id: 1,
  venueId: 1,
  venueName: "R",
  sectionId: 1,
  sectionName: "Main",
  resourceId: 1,
  resourceName: "T1",
  date: new Date(Date.now() - 10 * 60000).toISOString(),
  customerEmail: "ada@example.com",
  partySize: 2,
  status: "Booked",
  nextStatuses: [],
  undoStatus: null,
  ...overrides,
});

const renderActions = (b: BookingDetailDto, onSetStatus = jest.fn()) => {
  render(
    <BookingStatusActions
      booking={b}
      busy={false}
      onSetStatus={onSetStatus}
      borderColor="#ddd"
      mutedColor="#666"
      isDark={false}
    />
  );
  return onSetStatus;
};

describe("BookingStatusActions", () => {
  it("offers only the moves the server listed", () => {
    renderActions(booking({ nextStatuses: ["Arrived", "InUse"] }));

    expect(screen.getByLabelText("Mark as Arrived")).toBeTruthy();
    expect(screen.getByLabelText("Mark as In use")).toBeTruthy();
    expect(screen.queryByLabelText("Mark as No-show")).toBeNull();
  });

  it("sends the chosen status", () => {
    const onSetStatus = renderActions(booking({ nextStatuses: ["Arrived", "NoShow"] }));

    fireEvent.press(screen.getByLabelText("Mark as No-show"));

    expect(onSetStatus).toHaveBeenCalledWith("NoShow");
  });

  it("names where undo goes back to, and sends that status", () => {
    const onSetStatus = renderActions(
      booking({ status: "Finished", nextStatuses: [], undoStatus: "InUse" })
    );

    fireEvent.press(screen.getByText("Undo, back to In use"));

    expect(onSetStatus).toHaveBeenCalledWith("InUse");
  });

  it("offers no undo once the window has closed", () => {
    renderActions(booking({ status: "Finished", undoStatus: null }));

    expect(screen.queryByText(/Undo/)).toBeNull();
  });
});
