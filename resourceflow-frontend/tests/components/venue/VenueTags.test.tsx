/**
 * @jest-environment jsdom
 */
import React from "react";
import { screen } from "@testing-library/react-native";
import { StyleSheet } from "react-native";
import { VenueTags } from "@/components/venue/VenueTags";
import { renderWithProviders } from "@/tests/helpers/renderWithProviders";
import * as useAppThemeModule from "@/hooks/use-app-theme";
import { getThemeColors } from "@/theme/theme";

describe("VenueTags", () => {
  it("renders nothing when there are no tags", () => {
    renderWithProviders(<VenueTags tags={[]} />);
    expect(screen.queryByTestId("venue-tags")).toBeNull();
  });

  it("renders every tag when the row is within the cap", () => {
    renderWithProviders(<VenueTags tags={["Accessible", "Projector", "Dog friendly"]} />);
    expect(screen.getByText("Accessible")).toBeTruthy();
    expect(screen.getByText("Projector")).toBeTruthy();
    expect(screen.getByText("Dog friendly")).toBeTruthy();
    expect(screen.queryByText(/^\+/)).toBeNull();
  });

  it("collapses the overflow into a +N chip so card heights stay level", () => {
    renderWithProviders(
      <VenueTags tags={["Accessible", "Projector", "Dog friendly", "Live music"]} />
    );
    expect(screen.getByText("+1")).toBeTruthy();
    expect(screen.queryByText("Live music")).toBeNull();
    expect(screen.getByLabelText("1 more tag: Live music")).toBeTruthy();
  });

  it("names every hidden tag on the overflow chip", () => {
    renderWithProviders(
      <VenueTags tags={["Accessible", "Projector", "Dog friendly", "Live music", "Rooftop"]} />
    );
    expect(screen.getByText("+2")).toBeTruthy();
    expect(screen.getByLabelText("2 more tags: Live music, Rooftop")).toBeTruthy();
  });

  it("lifts the chip tint in dark mode", () => {
    const spy = jest.spyOn(useAppThemeModule, "useAppTheme").mockReturnValue({
      colors: getThemeColors(true),
      isDark: true,
      primaryColor: "#0a7ea4",
    } as ReturnType<typeof useAppThemeModule.useAppTheme>);
    try {
      renderWithProviders(<VenueTags tags={["Accessible"]} />);
      let chip = screen.getByText("Accessible").parent;
      while (chip && !StyleSheet.flatten(chip.props.style)?.backgroundColor) {
        chip = chip.parent;
      }
      expect(StyleSheet.flatten(chip?.props.style)).toMatchObject({
        backgroundColor: "rgba(10,126,164,0.15)",
      });
    } finally {
      spy.mockRestore();
    }
  });
});
