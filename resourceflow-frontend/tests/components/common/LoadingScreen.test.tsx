import React from "react";
import { render, screen } from "@testing-library/react-native";
import LoadingScreen from "@/components/common/LoadingScreen";

jest.mock("@expo/vector-icons", () => ({
  MaterialCommunityIcons: () => null,
}));

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: jest.fn(() => "light"),
}));

const brand = { primaryColor: "#0a7ea4", appName: "ResourceFlow" };

describe("LoadingScreen", () => {
  it("renders test-mode fallback with default message", () => {
    render(<LoadingScreen brand={brand} />);
    expect(screen.getByTestId("loading-screen")).toBeTruthy();
    expect(screen.getByText("Preparing your resource...")).toBeTruthy();
    expect(screen.getByText("ResourceFlow")).toBeTruthy();
  });

  it("renders custom message", () => {
    render(<LoadingScreen brand={brand} message="Loading locations..." />);
    expect(screen.getByText("Loading locations...")).toBeTruthy();
  });

  it("renders correctly in dark mode", () => {
    const { useColorScheme } = require("@/hooks/use-color-scheme");
    (useColorScheme as jest.Mock).mockReturnValue("dark");
    render(<LoadingScreen brand={brand} />);
    expect(screen.getByTestId("loading-screen")).toBeTruthy();
    (useColorScheme as jest.Mock).mockReturnValue("light");
  });
});
