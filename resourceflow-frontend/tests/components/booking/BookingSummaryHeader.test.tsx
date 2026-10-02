import React from "react";
import { render, screen } from "@testing-library/react-native";
import { StyleSheet } from "react-native";
import BookingSummaryHeader, {
  buildPlacementLine,
} from "@/components/booking/BookingSummaryHeader";
import { BookingDto } from "@/api/bookings";
import { VenueDto } from "@/api/venues";

jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

const booking = {
  id: 1,
  venueId: 1,
  customerEmail: "test@test.com",
  date: "2026-10-10T19:00:00Z",
  partySize: 2,
  isHeld: false,
  sectionName: "Annex",
  resourceName: "Resource 5",
} as BookingDto;

const venue = { id: 1, name: "Location 1", address: "123 Main St" } as VenueDto;

const renderHeader = (props: Partial<React.ComponentProps<typeof BookingSummaryHeader>> = {}) =>
  render(
    <BookingSummaryHeader
      booking={booking}
      venue={venue}
      statusLabel="Booking Found"
      statusIcon="checkmark-circle"
      statusColor="green"
      mutedColor="gray"
      {...props}
    />
  );

describe("BookingSummaryHeader", () => {
  it("heads the card with the location, and collapses address, section and resource into one line", () => {
    renderHeader();
    expect(screen.getByText("Booking Found")).toBeTruthy();
    expect(screen.getByText("Location 1")).toBeTruthy();
    expect(screen.getByText("123 Main St · Annex · Resource 5")).toBeTruthy();
  });

  it("promotes the status to the heading when the location didn't resolve", () => {
    renderHeader({ venue: null });
    // One "Booking Found", not two — the eyebrow gives way rather than repeating itself.
    expect(screen.getAllByText("Booking Found")).toHaveLength(1);
    expect(screen.queryByText(/123 Main St/)).toBeNull();
  });

  it("omits the placement line entirely when there is nothing to put in it", () => {
    renderHeader({
      booking: { ...booking, sectionName: undefined, resourceName: undefined },
      venue: { ...venue, address: "" } as VenueDto,
    });
    expect(screen.getByText("Location 1")).toBeTruthy();
    expect(screen.queryByText(/·/)).toBeNull();
  });

  it("washes the header with the tint it is given, and stays untinted without one", () => {
    renderHeader({ tint: "#ff000012" });
    expect(
      StyleSheet.flatten(screen.getByTestId("booking-summary-header").props.style).backgroundColor
    ).toBe("#ff000012");

    screen.unmount();
    renderHeader();
    expect(
      StyleSheet.flatten(screen.getByTestId("booking-summary-header").props.style).backgroundColor
    ).toBeUndefined();
  });

  describe("buildPlacementLine", () => {
    it("uses the group label for a combined-resource booking", () => {
      expect(
        buildPlacementLine(
          { ...booking, resourceGroupId: 7, resourceName: "Resources T2 + T3" },
          venue
        )
      ).toBe("123 Main St · Annex · Resources T2 + T3");
    });

    it("falls back to a generic label when a group booking carries no name", () => {
      expect(
        buildPlacementLine({ ...booking, resourceGroupId: 7, resourceName: undefined }, venue)
      ).toBe("123 Main St · Annex · Combined resources");
    });

    it("drops the separators for the parts that are missing", () => {
      expect(
        buildPlacementLine({ ...booking, sectionName: undefined, resourceName: undefined }, venue)
      ).toBe("123 Main St");
      expect(buildPlacementLine(booking, null)).toBe("Annex · Resource 5");
    });
  });
});
