/**
 * @jest-environment jsdom
 *
 * The standalone booking screen has been folded into the Locations list.
 * `book/[venueId]` is now a thin <Redirect> to
 * `/(user)/locations/[venueId]`, preserving `time`/`party` query params
 * for slot prefill. The booking-submission logic itself now lives in
 * LocationListItem (covered by its own tests).
 */
import React from "react";
import { render } from "@testing-library/react-native";
import BookScreen from "@/app/(user)/(locations)/book/[venueId]";

let capturedHref: unknown;

jest.mock("expo-router", () => ({
  Redirect: ({ href }: { href: unknown }) => {
    capturedHref = href;
    return null;
  },
  useLocalSearchParams: jest.fn(),
}));

import { useLocalSearchParams } from "expo-router";

describe("BookScreen (redirect to Locations)", () => {
  beforeEach(() => {
    capturedHref = undefined;
    jest.clearAllMocks();
  });

  it("redirects to /(user)/locations/[id] preserving time and party params", () => {
    (useLocalSearchParams as jest.Mock).mockReturnValue({
      venueId: "9",
      time: "18:30",
      party: "4",
    });
    render(<BookScreen />);
    expect(capturedHref).toBe("/(user)/locations/9?time=18%3A30&party=4");
  });

  it("redirects without a query string when no time/party params are present", () => {
    (useLocalSearchParams as jest.Mock).mockReturnValue({ venueId: "9" });
    render(<BookScreen />);
    expect(capturedHref).toBe("/(user)/locations/9");
  });

  it("redirects to / when venueId is absent", () => {
    (useLocalSearchParams as jest.Mock).mockReturnValue({});
    render(<BookScreen />);
    expect(capturedHref).toBe("/");
  });
});
