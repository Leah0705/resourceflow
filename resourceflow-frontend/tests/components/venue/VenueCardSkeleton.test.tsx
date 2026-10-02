/**
 * @jest-environment jsdom
 */
import React from "react";
import { screen } from "@testing-library/react-native";
import VenueCardSkeleton from "@/components/venue/VenueCardSkeleton";
import { renderWithProviders } from "@/tests/helpers/renderWithProviders";

describe("VenueCardSkeleton", () => {
  it("stands in for a card without announcing itself to a screen reader", () => {
    renderWithProviders(<VenueCardSkeleton />);

    const skeleton = screen.getByTestId("venue-card-skeleton", {
      includeHiddenElements: true,
    });
    expect(skeleton).toBeTruthy();
    // Placeholder blocks carry no information, so they are not worth stopping on.
    expect(skeleton.props.accessibilityElementsHidden).toBe(true);
    expect(skeleton.props.importantForAccessibility).toBe("no-hide-descendants");
  });
});
