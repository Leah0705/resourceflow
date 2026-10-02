import React from "react";
import { render, screen } from "@testing-library/react-native";
import { GuestPacingCard } from "@/components/admin/dashboard/GuestPacingCard";
import type { LocationPacingDto } from "@/api/admin";

jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" }),
}));

const capped = (slots: LocationPacingDto["slots"]): LocationPacingDto => ({
  venueId: 3,
  venueName: "Marie's Studio",
  maxGuestsPerSlot: 8,
  slots,
});

describe("GuestPacingCard", () => {
  it("stays silent when no location has a cap", () => {
    render(<GuestPacingCard pacing={[]} />);

    expect(screen.queryByTestId("guest-pacing-card")).toBeNull();
  });

  it("marks only the slot that reached the cap as full", () => {
    render(
      <GuestPacingCard
        pacing={[
          capped([
            { time: "19:00", guests: 8 },
            { time: "19:30", guests: 7 },
          ]),
        ]}
      />
    );

    expect(screen.getByText("Cap 8 participants")).toBeTruthy();
    expect(screen.getByLabelText("19:00: 8 of 8 participants")).toBeTruthy();
    expect(screen.getAllByTestId("pacing-bar-full")).toHaveLength(1);
  });

  it("says so when a capped location has no arrivals yet", () => {
    render(<GuestPacingCard pacing={[capped([])]} />);

    expect(screen.getByText("No arrivals yet today.")).toBeTruthy();
  });
});
