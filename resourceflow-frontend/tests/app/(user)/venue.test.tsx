/**
 * @jest-environment jsdom
 *
 * The standalone venue detail screen has been folded into the Locations
 * list. `venue/[id]` is now a thin <Redirect> to
 * `/(user)/locations/[id]`; these tests cover that redirect behaviour. The
 * detail/booking logic itself now lives in LocationListItem (covered by its
 * own tests).
 */
import React from "react";
import { render } from "@testing-library/react-native";
import VenueScreen from "@/app/(user)/(locations)/venue/[id]";

let capturedHref: unknown;

jest.mock("expo-router", () => ({
  Redirect: ({ href }: { href: unknown }) => {
    capturedHref = href;
    return null;
  },
  useLocalSearchParams: jest.fn(),
}));

import { useLocalSearchParams } from "expo-router";

describe("VenueScreen (redirect to Locations)", () => {
  beforeEach(() => {
    capturedHref = undefined;
    jest.clearAllMocks();
  });

  it("redirects to /(user)/locations/[id] when id is present", () => {
    (useLocalSearchParams as jest.Mock).mockReturnValue({ id: "7" });
    render(<VenueScreen />);
    expect(capturedHref).toBe("/(user)/locations/7");
  });

  it("redirects to / when id is absent", () => {
    (useLocalSearchParams as jest.Mock).mockReturnValue({ id: undefined });
    render(<VenueScreen />);
    expect(capturedHref).toBe("/");
  });
});
