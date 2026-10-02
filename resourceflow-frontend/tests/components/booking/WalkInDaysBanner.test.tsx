import React from "react";
import { render, screen } from "@testing-library/react-native";
import WalkInDaysBanner from "@/components/booking/WalkInDaysBanner";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

describe("WalkInDaysBanner", () => {
  it("renders nothing when no walk-in days are configured", () => {
    render(<WalkInDaysBanner venue={{}} />);
    expect(screen.queryByTestId("walk-in-days-banner")).toBeNull();
  });

  it("renders nothing for a fully walk-in location (handled by WalkInNotice instead)", () => {
    render(<WalkInDaysBanner venue={{ walkInOnly: true }} />);
    expect(screen.queryByTestId("walk-in-days-banner")).toBeNull();
  });

  it("names a single walk-in day", () => {
    render(<WalkInDaysBanner venue={{ walkInDays: "5" }} />);
    expect(screen.getByTestId("walk-in-days-banner")).toBeTruthy();
    expect(screen.getByText(/Walk-ins only on Fridays/)).toBeTruthy();
  });

  it("names multiple walk-in days", () => {
    render(<WalkInDaysBanner venue={{ walkInDays: "6,7" }} />);
    expect(screen.getByText(/Walk-ins only on Saturdays and Sundays/)).toBeTruthy();
  });
});
