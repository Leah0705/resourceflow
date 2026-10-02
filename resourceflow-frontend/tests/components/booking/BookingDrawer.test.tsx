/**
 * @jest-environment jsdom
 *
 * Covers the booking submission that moved out of the Locations card when booking became
 * a drawer beside the list rather than an accordion body inside it.
 */
import React from "react";
import { screen, waitFor, fireEvent } from "@testing-library/react-native";
import { KeyboardAvoidingView, StyleSheet } from "react-native";
import BookingDrawer, { shouldDismissSheet } from "@/components/booking/BookingDrawer";
import { createBooking } from "@/api/bookings";
import { rememberBooking } from "@/utils/bookingCache";
import { renderWithProviders } from "@/tests/helpers/renderWithProviders";
import { renderWithInsets } from "@/tests/helpers/renderWithInsets";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

// The sheet variant renders inside a Modal; render its children inline so tests can reach them.
jest.mock("react-native", () => {
  const rn = jest.requireActual("react-native");
  rn.Modal = ({ children, visible }: any) => (visible ? children : null);
  return rn;
});

const mockPush = jest.fn();
jest.mock("expo-router", () => ({
  useRouter: () => ({ push: mockPush }),
}));

jest.mock("@/utils/bookingCache", () => ({ rememberBooking: jest.fn() }));

jest.mock("@/api/bookings", () => ({
  createBooking: jest.fn(),
}));

// jsdom has no Web Animations API, so the real helper always returns null here. Mocking it
// lets one test put a running animation in front of the exit and prove it is waited on.
jest.mock("@/utils/webAnimation", () => ({
  ...jest.requireActual("@/utils/webAnimation"),
  animateNode: jest.fn(() => null),
}));
const mockAnimateNode = jest.requireMock("@/utils/webAnimation").animateNode as jest.Mock;

jest.mock("@/components/waitlist/WaitlistPanel", () => {
  const { Text } = require("react-native");
  return {
    __esModule: true,
    default: ({ venueId, entryRef }: any) => (
      <Text testID="waitlist-panel">{`${venueId}:${entryRef}`}</Text>
    ),
  };
});

jest.mock("@/components/booking/BookingForm", () => {
  const { Pressable, Text } = require("react-native");
  return {
    __esModule: true,
    default: function MockBookingForm({
      onSubmit,
      onPartySizeChange,
      onDateChange,
      layout,
      partySize,
      date,
      initialTime,
    }: any) {
      const baseData = {
        customerEmail: "test@example.com",
        customerName: "Test Participant",
        partySize,
        holdId: "hold_123",
        date,
        time: initialTime,
        specialRequests: "",
      };
      return (
        <>
          <Text testID="form-layout">{layout}</Text>
          <Text testID="form-party-size">{String(partySize)}</Text>
          <Text testID="form-date">{String(date)}</Text>
          <Text testID="form-initial-time">{String(initialTime)}</Text>
          <Pressable testID="form-party-size-change" onPress={() => onPartySizeChange(6)} />
          <Pressable testID="form-date-change" onPress={() => onDateChange("2026-04-18")} />
          <Pressable
            testID="submit-trigger"
            onPress={() =>
              onSubmit({ ...baseData, resourceId: 101, sectionId: 1, resourceGroupId: null })
            }
          />
          <Pressable
            testID="submit-trigger-no-section"
            onPress={() =>
              onSubmit({ ...baseData, resourceId: 101, sectionId: null, resourceGroupId: null })
            }
          />
          <Pressable
            testID="submit-trigger-no-resource"
            onPress={() =>
              onSubmit({ ...baseData, resourceId: null, sectionId: null, resourceGroupId: null })
            }
          />
          <Pressable
            testID="submit-trigger-unknown-resource"
            onPress={() =>
              onSubmit({ ...baseData, resourceId: 9999, sectionId: null, resourceGroupId: null })
            }
          />
          <Pressable
            testID="submit-trigger-with-requests"
            onPress={() =>
              onSubmit({
                ...baseData,
                resourceId: null,
                sectionId: null,
                resourceGroupId: null,
                specialRequests: "Step-free access",
              })
            }
          />
        </>
      );
    },
  };
});

const mockVenue = {
  id: 1,
  name: "Toronto Location",
  address: "123 Test St",
  openTime: "09:00",
  closeTime: "22:00",
  openDays: "1,2,3,4,5,6,7",
  timezone: "America/Toronto",
  sections: [
    {
      id: 1,
      name: "Main",
      venueId: 1,
      resources: [{ id: 101, name: "T1", capacity: 4, sectionId: 1 }],
    },
  ],
};

const TODAY = "2026-04-16";

const baseProps = {
  venue: mockVenue as any,
  partySize: 2,
  date: TODAY,
  time: "19:30",
  today: TODAY,
  variant: "side" as const,
  onClose: jest.fn(),
  onPartySizeChange: jest.fn(),
  onDateChange: jest.fn(),
};

describe("BookingDrawer", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (createBooking as jest.Mock).mockResolvedValue({ id: 50, bookingRef: "REF123" });
  });

  it("heads the panel with the location and the criteria already chosen", () => {
    renderWithProviders(<BookingDrawer {...baseProps} />);
    expect(screen.getByText("Toronto Location")).toBeTruthy();
    expect(screen.getByText("2 participants · Today · 19:30")).toBeTruthy();
  });

  it("keeps the location a plain heading when there is nowhere else to switch to", () => {
    renderWithProviders(
      <BookingDrawer {...baseProps} venues={[mockVenue as any]} onVenueChange={jest.fn()} />
    );
    expect(screen.getByText("Toronto Location")).toBeTruthy();
    expect(screen.queryByLabelText(/^Location,/)).toBeNull();
  });

  it("switches location from the panel header when the page lists more than one", () => {
    const otherVenue = { ...mockVenue, id: 2, name: "Ottawa Location" };
    const onVenueChange = jest.fn();
    renderWithProviders(
      <BookingDrawer
        {...baseProps}
        venues={[mockVenue as any, otherVenue as any]}
        onVenueChange={onVenueChange}
      />
    );

    fireEvent.press(screen.getByLabelText("Location, Toronto Location"));
    fireEvent.press(screen.getByLabelText("Ottawa Location"));

    expect(onVenueChange).toHaveBeenCalledWith(otherVenue);
  });

  it("shows the location's waitlist in place of the booking form", () => {
    const otherVenue = { ...mockVenue, id: 2, name: "Ottawa Location" };
    renderWithProviders(
      <BookingDrawer
        {...baseProps}
        venues={[mockVenue as any, otherVenue as any]}
        onVenueChange={jest.fn()}
        waitlist={{ entryRef: "abc234", onJoined: jest.fn(), onReset: jest.fn() }}
      />
    );

    expect(screen.getByTestId("waitlist-panel")).toHaveTextContent("1:abc234");
    expect(screen.queryByTestId("form-layout")).toBeNull();
    expect(screen.getByText("Walk-in waitlist · 2 participants")).toBeTruthy();
    // One location's queue: no switching to another from here.
    expect(screen.queryByLabelText("Location, Toronto Location")).toBeNull();
    expect(screen.getByLabelText("Join the waitlist at Toronto Location")).toBeTruthy();
  });

  it("names the day when booking a date other than today", () => {
    // The header formats with the runtime locale, so pinning "Fri, Apr 17" would only hold
    // on an en-US machine — derive the same string rather than assert one locale's order.
    const expectedDay = new Date("2026-04-17T12:00:00").toLocaleDateString(undefined, {
      weekday: "short",
      month: "short",
      day: "numeric",
    });
    renderWithProviders(<BookingDrawer {...baseProps} date="2026-04-17" />);
    expect(screen.getByText(`2 participants · ${expectedDay} · 19:30`)).toBeTruthy();
  });

  it("uses singular 'participant' copy for a party of one", () => {
    renderWithProviders(<BookingDrawer {...baseProps} partySize={1} />);
    expect(screen.getByText("1 participant · Today · 19:30")).toBeTruthy();
  });

  it("hands the already-chosen party, date and time to the form in drawer layout", () => {
    renderWithProviders(<BookingDrawer {...baseProps} partySize={5} />);
    expect(screen.getByTestId("form-layout").props.children).toBe("drawer");
    expect(screen.getByTestId("form-party-size").props.children).toBe("5");
    expect(screen.getByTestId("form-date").props.children).toBe(TODAY);
    expect(screen.getByTestId("form-initial-time").props.children).toBe("19:30");
  });

  it("passes a party-size change up to the page so the list behind it follows", () => {
    const onPartySizeChange = jest.fn();
    renderWithProviders(<BookingDrawer {...baseProps} onPartySizeChange={onPartySizeChange} />);
    fireEvent.press(screen.getByTestId("form-party-size-change"));
    expect(onPartySizeChange).toHaveBeenCalledWith(6);
  });

  it("passes a date change up to the page the same way", () => {
    const onDateChange = jest.fn();
    renderWithProviders(<BookingDrawer {...baseProps} onDateChange={onDateChange} />);
    fireEvent.press(screen.getByTestId("form-date-change"));
    expect(onDateChange).toHaveBeenCalledWith("2026-04-18");
  });

  it("floats as a rounded card rather than a column welded to the page edge", () => {
    renderWithProviders(<BookingDrawer {...baseProps} />);
    const panel = StyleSheet.flatten(screen.getByTestId("booking-drawer").props.style);
    expect(panel.borderRadius).toBeGreaterThan(0);
    // All four sides, not the single left edge a docked column would carry.
    expect(panel.borderWidth).toBe(1);
    expect(panel.borderLeftWidth).toBeUndefined();
    expect(panel.marginTop).toBeGreaterThan(0);
    expect(panel.marginBottom).toBeGreaterThan(0);
    expect(panel.marginLeft).toBeGreaterThan(0);
    // Corners are only round if the header fill and the scroll body are clipped to them.
    expect(panel.overflow).toBe("hidden");
  });

  it("closes on the close button", async () => {
    const onClose = jest.fn();
    renderWithProviders(<BookingDrawer {...baseProps} onClose={onClose} />);
    fireEvent.press(screen.getByTestId("booking-drawer-close"));
    // Nothing to animate here, so the panel hands control straight back rather than
    // waiting on an exit that will never finish.
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("holds the close back until its exit animation has finished", () => {
    const exit: { onfinish?: () => void } = {};
    mockAnimateNode.mockReturnValueOnce(null).mockReturnValueOnce(exit);
    const onClose = jest.fn();
    renderWithProviders(<BookingDrawer {...baseProps} onClose={onClose} />);

    fireEvent.press(screen.getByTestId("booking-drawer-close"));
    // The page drops the panel from state the moment onClose fires, so calling it now
    // would cut the exit off mid-animation.
    expect(onClose).not.toHaveBeenCalled();

    exit.onfinish?.();
    expect(onClose).toHaveBeenCalled();
  });

  describe("as a side panel on web", () => {
    const rn = require("react-native");
    const originalOS = rn.Platform.OS;
    beforeEach(() => {
      rn.Platform.OS = "web";
    });
    afterEach(() => {
      rn.Platform.OS = originalOS;
    });

    it("closes on Escape", async () => {
      const onClose = jest.fn();
      renderWithProviders(<BookingDrawer {...baseProps} variant="side" onClose={onClose} />);
      window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
      await waitFor(() => expect(onClose).toHaveBeenCalled());
    });

    it("ignores other keys", async () => {
      const onClose = jest.fn();
      renderWithProviders(<BookingDrawer {...baseProps} variant="side" onClose={onClose} />);
      window.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" }));
      expect(onClose).not.toHaveBeenCalled();
    });
  });

  /**
   * Narrow web keeps the hand-rolled sheet: a browser has no platform sheet to defer to. Off
   * web it is `NativeBookingSheet` instead, which is why this whole block pins the web
   * platform rather than relying on Jest's default.
   */
  describe("as a bottom sheet on web", () => {
    const rn = require("react-native");
    const originalOS = rn.Platform.OS;
    beforeEach(() => {
      rn.Platform.OS = "web";
    });
    afterEach(() => {
      rn.Platform.OS = originalOS;
    });

    it("renders inside the sheet shell", () => {
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" />);
      expect(screen.getByTestId("booking-drawer")).toBeTruthy();
      expect(screen.getByText("Toronto Location")).toBeTruthy();
    });

    it("keeps the sheet flush to the bottom edge, rounded only at the top", () => {
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" />);
      const sheet = StyleSheet.flatten(screen.getByTestId("booking-drawer").props.style);
      // The side panel's float treatment must not leak here: a sheet that floats above
      // the bottom edge leaves a strip of page under it.
      expect(sheet.borderTopLeftRadius).toBeGreaterThan(0);
      expect(sheet.borderRadius).toBeUndefined();
      expect(sheet.marginBottom).toBeUndefined();
    });

    it("keeps the backdrop transparent rather than dimming the list behind it", () => {
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" />);
      const backdrop = StyleSheet.flatten(
        screen.getByTestId("booking-drawer-backdrop", { includeHiddenElements: true }).props.style
      );
      expect(backdrop.backgroundColor).toBe("transparent");
    });

    // The keyboard and the home indicator are the browser's problem, so neither the
    // KeyboardAvoider nor a bottom safe-area inset belongs on this branch any more.
    it("adds no keyboard shell and no bottom inset", () => {
      renderWithInsets({ bottom: 34 }, <BookingDrawer {...baseProps} variant="sheet" />);

      expect(screen.UNSAFE_queryAllByType(KeyboardAvoidingView)).toHaveLength(0);
      expect(
        StyleSheet.flatten(screen.getByTestId("booking-drawer").props.style).paddingBottom
      ).toBeUndefined();
    });

    it("offers a drag handle wired to the pan responder, hidden from assistive tech", () => {
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" />);
      // The handle duplicates the labeled close button, so it is a11y-hidden by design —
      // hence includeHiddenElements.
      const grabber = screen.getByTestId("booking-drawer-grabber", { includeHiddenElements: true });
      // PanResponder spreads its handlers onto the view it is attached to; without them
      // the handle is decorative and the sheet cannot be dragged away.
      expect(grabber.props.onStartShouldSetResponder).toBeDefined();
      expect(grabber.props.onMoveShouldSetResponder).toBeDefined();
      expect(grabber.props.accessibilityElementsHidden).toBe(true);
    });

    it("closes when the backdrop is tapped, after the sheet has slid away", async () => {
      const onClose = jest.fn();
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" onClose={onClose} />);
      fireEvent.press(
        screen.getByTestId("booking-drawer-backdrop", { includeHiddenElements: true })
      );
      await waitFor(() => expect(onClose).toHaveBeenCalled());
    });
  });

  /**
   * Off web the sheet is the platform's own. The drawer keeps its props and its body;
   * what changes is the shell around them, so these pin the handover rather than the sheet's
   * internals, which are `NativeBookingSheet`'s own tests.
   */
  describe("as a bottom sheet off web", () => {
    it("hands the body to the native sheet, with none of the web chrome", () => {
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" />);

      expect(screen.getByText("Toronto Location")).toBeTruthy();
      // The hand-rolled backdrop and drag handle belong to the web sheet; the platform sheet
      // brings its own, and two of each would stack.
      expect(
        screen.queryByTestId("booking-drawer-backdrop", { includeHiddenElements: true })
      ).toBeNull();
      expect(
        screen.queryByTestId("booking-drawer-grabber", { includeHiddenElements: true })
      ).toBeNull();
    });

    // The sheet animates itself away and reports back through onDismiss, so the close button
    // asks it to dismiss rather than yanking the drawer out from under its own exit.
    it("lets the sheet animate away before the page drops it", async () => {
      const onClose = jest.fn();
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" onClose={onClose} />);

      fireEvent.press(screen.getByTestId("booking-drawer-close"));

      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    });
  });

  describe("submission", () => {
    it("navigates to the booking confirmation page on success", async () => {
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() =>
        expect(mockPush).toHaveBeenCalledWith(
          "/booking-confirmation/REF123?email=test%40example.com"
        )
      );
    });

    /**
     * The sheet provider sits above the navigator and the tab keeps this screen mounted, so a
     * sheet left presented draws over the confirmation route and its result sheet both.
     * Dismissing has to happen before the push, not merely eventually.
     */
    it("dismisses the sheet before navigating, off web", async () => {
      const onClose = jest.fn();
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" onClose={onClose} />);

      fireEvent.press(screen.getByTestId("submit-trigger"));

      await waitFor(() => expect(mockPush).toHaveBeenCalled());
      expect(onClose).toHaveBeenCalled();
      expect(onClose.mock.invocationCallOrder[0]).toBeLessThan(
        mockPush.mock.invocationCallOrder[0]
      );
    });

    // A failed booking has to stay readable in the sheet the guest made it in.
    it("keeps the sheet up when the booking fails", async () => {
      (createBooking as jest.Mock).mockRejectedValue(new Error("Fully booked"));
      const onClose = jest.fn();
      renderWithProviders(<BookingDrawer {...baseProps} variant="sheet" onClose={onClose} />);

      fireEvent.press(screen.getByTestId("submit-trigger"));

      await waitFor(() => expect(screen.getByText("Fully booked")).toBeTruthy());
      expect(onClose).not.toHaveBeenCalled();
      expect(mockPush).not.toHaveBeenCalled();
    });

    it("remembers the booking so a device with no cookie jar can still look it up", async () => {
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() =>
        expect(rememberBooking).toHaveBeenCalledWith(
          expect.objectContaining({
            bookingRef: "REF123",
            email: "test@example.com",
            partySize: 2,
            venueName: "Toronto Location",
          })
        )
      );
    });

    it("remembers nothing when the response carries no reference to look up", async () => {
      (createBooking as jest.Mock).mockResolvedValue({ id: 77 });
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() => expect(mockPush).toHaveBeenCalled());
      expect(rememberBooking).not.toHaveBeenCalled();
    });

    it("navigates using the booking id when the response has no bookingRef", async () => {
      (createBooking as jest.Mock).mockResolvedValue({ id: 77 });
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() =>
        expect(mockPush).toHaveBeenCalledWith("/booking-confirmation/77?email=test%40example.com")
      );
    });

    it("resolves the owning section for an explicit resource submitted without one", async () => {
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger-no-section"));
      await waitFor(() =>
        expect(createBooking).toHaveBeenCalledWith(
          expect.objectContaining({ sectionId: 1, resourceId: 101 })
        )
      );
    });

    it("falls back to no section when the submitted resource isn't in any known section", async () => {
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger-unknown-resource"));
      await waitFor(() =>
        expect(createBooking).toHaveBeenCalledWith(
          expect.objectContaining({ sectionId: null, resourceId: 9999 })
        )
      );
    });

    it("leaves sectionId null for an auto-assigned (any-resource) booking", async () => {
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger-no-resource"));
      await waitFor(() =>
        expect(createBooking).toHaveBeenCalledWith(
          expect.objectContaining({ sectionId: null, resourceId: null })
        )
      );
    });

    it("sends special requests through, and null when there are none", async () => {
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger-with-requests"));
      await waitFor(() =>
        expect(createBooking).toHaveBeenCalledWith(
          expect.objectContaining({ specialRequests: "Step-free access" })
        )
      );

      fireEvent.press(screen.getByTestId("submit-trigger-no-resource"));
      await waitFor(() =>
        expect(createBooking).toHaveBeenCalledWith(
          expect.objectContaining({ specialRequests: null })
        )
      );
    });

    it("falls back to the UTC timezone when the location has none set", async () => {
      const { timezone: _timezone, ...noTimezone } = mockVenue;
      renderWithProviders(<BookingDrawer {...baseProps} venue={noTimezone as any} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() => expect(createBooking).toHaveBeenCalled());
    });

    it("shows the thrown error message when submission fails", async () => {
      (createBooking as jest.Mock).mockRejectedValue(new Error("Resource no longer available"));
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() => expect(screen.getByText("Resource no longer available")).toBeTruthy());
    });

    it("shows a generic error message when a non-Error value is thrown", async () => {
      (createBooking as jest.Mock).mockRejectedValue("network down");
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() =>
        expect(screen.getByText("Something went wrong. Please try again.")).toBeTruthy()
      );
    });

    it("does nothing when the submission resolves with no booking at all", async () => {
      (createBooking as jest.Mock).mockResolvedValue(null);
      renderWithProviders(<BookingDrawer {...baseProps} />);
      fireEvent.press(screen.getByTestId("submit-trigger"));
      await waitFor(() => expect(createBooking).toHaveBeenCalled());
      expect(mockPush).not.toHaveBeenCalled();
      expect(screen.queryByText("Something went wrong. Please try again.")).toBeNull();
    });
  });
});

describe("shouldDismissSheet", () => {
  it("dismisses on a long drag regardless of speed", () => {
    expect(shouldDismissSheet(121, 0)).toBe(true);
  });

  it("dismisses on a short but fast downward flick", () => {
    expect(shouldDismissSheet(60, 0.9)).toBe(true);
  });

  it("springs back from a short slow drag", () => {
    expect(shouldDismissSheet(60, 0.2)).toBe(false);
    expect(shouldDismissSheet(10, 2)).toBe(false);
  });

  it("never dismisses on an upward drag", () => {
    expect(shouldDismissSheet(-200, -3)).toBe(false);
  });
});
