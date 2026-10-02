import React from "react";
import { render, screen } from "@testing-library/react-native";
import UpdateRequired from "@/components/common/UpdateRequired";

jest.mock("@/hooks/use-app-theme", () => ({
  useAppTheme: () => ({
    brand: { appName: "Test Location", primaryColor: "#0a7ea4" },
    colors: { muted: "#666" },
    primaryColor: "#0a7ea4",
    isDark: false,
  }),
}));

describe("UpdateRequired", () => {
  it("names the brand and tells the guest where the update comes from", () => {
    render(<UpdateRequired />);

    expect(screen.getByTestId("update-required")).toBeTruthy();
    expect(screen.getByText("Test Location")).toBeTruthy();
    expect(screen.getByText("Update required")).toBeTruthy();
    expect(screen.getByText("Please update the app from the store to keep booking.")).toBeTruthy();
  });
});
