/**
 * @jest-environment jsdom
 *
 * The guards around booking, kept apart from BookingForm.test.tsx (which covers the
 * form's own layout, hold and availability behaviour): the per-resource capacity confirm,
 * the large-party ceiling and its contact modal, the resource-label fallbacks, and what a
 * slot's availableResourceIds do to the dropdown.
 */
import React from "react";
import { screen, fireEvent, waitFor } from "@testing-library/react-native";
import BookingForm from "@/components/booking/BookingForm";
import { useResourceHold } from "@/components/booking/useResourceHold";
import { renderWithProviders } from "@/tests/helpers/renderWithProviders";
import { confirm } from "@/utils/confirm";

// The oversize prompt goes through the cross-platform helper (window.confirm on web,
// Alert.alert on native), so the guard is exercised through it rather than through a
// browser API the form no longer calls directly.
// WalkInNotice links to the waitlist; the real router can't load under Jest.
jest.mock("expo-router", () => ({ useRouter: () => ({ push: jest.fn() }) }));

jest.mock("@/utils/confirm", () => ({ confirm: jest.fn() }));

// Mock useResourceHold
jest.mock("@/components/booking/useResourceHold");
const mockSetHoldStatus = jest.fn();

jest.mock("@/api/availability", () => ({
  fetchAvailability: jest.fn(() =>
    Promise.resolve({
      slots: [
        { time: "12:00", isAvailable: true, category: "AM" },
        { time: "13:00", isAvailable: true, category: "AM" },
        { time: "18:00", isAvailable: true, category: "PM" },
      ],
    })
  ),
}));

// Mock Modal to always render children (react-native-testing-library doesn't render it by default)
jest.mock("react-native", () => {
  const rn = jest.requireActual("react-native");
  rn.Modal = ({ children, visible }: any) => (visible ? children : null);
  return rn;
});

// Stub social-links fetch so the large-party notice modal renders without a
// network call. Returns no contact links by default; individual tests override.
jest.mock("@/api/venues", () => ({
  ...jest.requireActual("@/api/venues"),
  fetchSocialLinks: jest.fn(() => Promise.resolve([])),
}));

describe("BookingForm", () => {
  /**
   * Party size is a stepper off web and this file runs at Jest's default native
   * platform, so party sizes are reached by ticking up from the form's default of two rather than by
   * opening a list. The guards below key off the party size, never off the control that produced it.
   */
  const setPartySize = (target: number) => {
    for (let partySize = 2; partySize < target; partySize++) {
      fireEvent.press(screen.getByLabelText("One more participant"));
    }
  };

  const mockVenue = {
    id: 1,
    name: "Test Location",
    openTime: "00:00",
    closeTime: "23:59",
    openDays: "1,2,3,4,5,6,7",
    timezone: "UTC",
    sections: [
      {
        id: 10,
        name: "Main",
        resources: [
          { id: 100, name: "T1", capacity: 2, sectionId: 10 },
          { id: 101, name: "T2", capacity: 4, sectionId: 10 },
        ],
      },
    ],
  } as any;

  beforeEach(() => {
    jest.clearAllMocks();
    (useResourceHold as jest.Mock).mockReturnValue({
      holdStatus: "held",
      secondsLeft: 60,
      holdId: "h-123",
      resolvedResourceId: null,
      resolvedSectionId: null,
      setHoldStatus: mockSetHoldStatus,
    });
    (confirm as jest.Mock).mockResolvedValue(true);
  });

  /**
   * The form defaults to "Any section" (resource dropdown hidden). Tests that exercise the
   * explicit-resource path call this to switch into the "Main" section first.
   */
  function selectMainSection() {
    // The section Select's trigger shows the selected option label. Open the modal, then
    // pick "Main".
    fireEvent.press(screen.getByText("Any section"));
    fireEvent.press(screen.getByText("Main"));
  }

  it("renders correctly and handles submission", async () => {
    const onSubmit = jest.fn();
    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={onSubmit} />);

    // Fill name and email (both required by isValid)
    fireEvent.changeText(screen.getByPlaceholderText("Your full name"), "Test User");
    fireEvent.changeText(screen.getByPlaceholderText("your@email.com"), "test@test.com");

    // Click submit
    fireEvent.press(screen.getByText("Confirm Booking"));

    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        customerName: "Test User",
        customerEmail: "test@test.com",
        holdId: "h-123",
      })
    );
  });

  it("shows warning when the selected resource can't fit the party", async () => {
    // This section has only a capacity-2 resource while the location's largest (in
    // Main) fits 4, so booking 3 guests here lands the auto-select on an
    // undersized resource — the per-resource confirm path, distinct from the global
    // large-party guard (3 <= 4 max capacity).
    const onSubmit = jest.fn();
    const venue = {
      ...mockVenue,
      sections: [
        mockVenue.sections[0],
        { id: 11, name: "Annex", resources: [{ id: 102, name: "P1", capacity: 2, sectionId: 11 }] },
      ],
    };
    renderWithProviders(<BookingForm venue={venue} onSubmit={onSubmit} />);

    // Open the section list and pick the Annex (capacity-2-only) section.
    fireEvent.press(screen.getByText("Any section"));
    fireEvent.press(screen.getByText("Annex"));

    // 3 guests > Annex's capacity-2 resource, but <= the location's capacity-4 max.
    setPartySize(3);

    fireEvent.changeText(screen.getByPlaceholderText("Your full name"), "Test User");
    fireEvent.changeText(screen.getByPlaceholderText("your@email.com"), "test@test.com");
    fireEvent.press(screen.getByText("Confirm Booking"));

    expect(confirm).toHaveBeenCalled();
    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
  });

  it("does not submit when the party-exceeds-capacity confirmation is declined", async () => {
    (confirm as jest.Mock).mockResolvedValue(false);
    const onSubmit = jest.fn();
    const venue = {
      ...mockVenue,
      sections: [
        mockVenue.sections[0],
        { id: 11, name: "Annex", resources: [{ id: 102, name: "P1", capacity: 2, sectionId: 11 }] },
      ],
    };
    renderWithProviders(<BookingForm venue={venue} onSubmit={onSubmit} />);

    fireEvent.press(screen.getByText("Any section"));
    fireEvent.press(screen.getByText("Annex"));

    setPartySize(3);

    fireEvent.changeText(screen.getByPlaceholderText("Your full name"), "Test User");
    fireEvent.changeText(screen.getByPlaceholderText("your@email.com"), "test@test.com");
    fireEvent.press(screen.getByText("Confirm Booking"));

    await waitFor(() => expect(confirm).toHaveBeenCalled());
    expect(onSubmit).not.toHaveBeenCalled();
  });

  // ── Large-party guard (interim: block parties bigger than any single resource) ─

  it("blocks submission and shows the large-party notice when the party exceeds the largest resource", async () => {
    const onSubmit = jest.fn();
    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={onSubmit} />);

    // mockVenue's largest resource fits 4. Bump to 5 to trip the global guard.
    setPartySize(5);

    // The inline bubble renders with the cap copy…
    expect(
      screen.getAllByText(/Our largest resource accommodates 4 participants/).length
    ).toBeGreaterThan(0);
    // …and the modal auto-opens on the over-capacity change.
    await waitFor(() => expect(screen.getByText(/need to be arranged directly/)).toBeTruthy());

    fireEvent.changeText(screen.getByPlaceholderText("Your full name"), "Test User");
    fireEvent.changeText(screen.getByPlaceholderText("your@email.com"), "test@test.com");
    fireEvent.press(screen.getByText("Confirm Booking"));

    // Guard short-circuits isValid, so the submit handler never runs.
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("reopens the large-party modal when tapping the inline bubble", async () => {
    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={jest.fn()} />);

    setPartySize(5);

    await waitFor(() => expect(screen.getByText(/need to be arranged directly/)).toBeTruthy());
    // Dismiss, then re-open via the bubble's Contact us affordance.
    fireEvent.press(screen.getByText("Got it"));
    expect(screen.queryByText(/need to be arranged directly/)).toBeNull();

    fireEvent.press(screen.getByText(/Contact us/));
    expect(screen.getByText(/need to be arranged directly/)).toBeTruthy();
  });

  it("lists every configured social link as a contact option in the modal", async () => {
    const { fetchSocialLinks } = require("@/api/venues");
    (fetchSocialLinks as jest.Mock).mockResolvedValueOnce([
      {
        id: 1,
        label: "Call us",
        url: "tel:+15551234567",
        iconKey: "call-outline",
        sortOrder: 2,
      },
      {
        id: 2,
        label: "Email",
        url: "mailto:hi@resourceflow.example",
        iconKey: "mail-outline",
        sortOrder: 1,
      },
    ]);

    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={jest.fn()} />);

    setPartySize(5);

    // Both configured links render (sorted by sortOrder), regardless of icon
    // key — the venue controls what shows up. ("Email" alone collides
    // with the email input field, so assert via a more specific text.)
    await waitFor(() => expect(screen.getByText("Call us")).toBeTruthy());
    expect(screen.getAllByText("Email").length).toBeGreaterThan(0);
    // The empty-state fallback must NOT render.
    expect(screen.queryByText(/No contact details are listed/)).toBeNull();
  });

  it("disables submit when invalid", () => {
    (useResourceHold as jest.Mock).mockReturnValue({
      holdStatus: "idle",
      secondsLeft: 0,
      holdId: null,
      setHoldStatus: mockSetHoldStatus,
    });
    const onSubmit = jest.fn();
    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={onSubmit} />);

    const btn = screen.getByText("Confirm Booking");
    // Button component renders a Pressable.
    // We check if it's disabled via props if we can, or just try to press it.
    fireEvent.press(btn);
    // holdStatus "idle" makes isValid false, so handleSubmit's early-return
    // guard should keep onSubmit from ever firing.
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("renders a fallback 'Resource {id}' label when a resource has no name", () => {
    const venue = {
      ...mockVenue,
      sections: [
        {
          id: 10,
          name: "Main",
          resources: [
            { id: 100, name: "T1", capacity: 2, sectionId: 10 },
            { id: 102, capacity: 6, sectionId: 10 }, // no `name` -> falls back to "Resource {id}"
          ],
        },
      ],
    };
    renderWithProviders(<BookingForm venue={venue} onSubmit={jest.fn()} />);

    // Switch out of "Any section" so the explicit resource dropdown is visible.
    selectMainSection();

    // T1 (capacity 2) is auto-selected by default; open the resource Select to
    // reveal the full option list, including the unnamed resource's fallback label.
    fireEvent.press(screen.getByText("T1 (2 places)"));

    expect(screen.getByText("Resource 102 (6 places)")).toBeTruthy();
  });

  it("renders 'No resources available' when participants exceed all resources", () => {
    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={jest.fn()} />);

    // Switch out of "Any section" so the explicit resource dropdown is visible.
    selectMainSection();

    setPartySize(10); // mockVenue max is 4

    expect(screen.getByText("No resources available for 10 participants.")).toBeTruthy();
  });

  it("handles null fetchAvailability response without crashing", async () => {
    const { fetchAvailability } = require("@/api/availability");
    (fetchAvailability as jest.Mock).mockResolvedValueOnce(null);

    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={jest.fn()} />);

    await waitFor(() => {
      expect(screen.getByText("Confirm Booking")).toBeTruthy();
    });
  });

  it("filters resources by availableResourceIds from availability slot", async () => {
    const { fetchAvailability } = require("@/api/availability");
    (fetchAvailability as jest.Mock).mockResolvedValueOnce({
      slots: [
        {
          time: "09:00",
          isAvailable: true,
          category: "AM" as const,
          availableResourceIds: [101],
        },
      ],
    });

    renderWithProviders(<BookingForm venue={mockVenue} onSubmit={jest.fn()} initialTime="09:00" />);

    // Switch out of "Any section" so the explicit resource dropdown is visible.
    selectMainSection();

    await waitFor(() => {
      // Only T2 (id 101) should be shown; T1 (id 100) should be filtered out
      expect(screen.getByText("T2 (4 places)")).toBeTruthy();
    });
  });

  // ── Combinable resource groups ───────────────────────────────────────────────

  it("renders combinable groups in the dropdown with the expected label", async () => {
    const { fetchAvailability } = require("@/api/availability");
    (fetchAvailability as jest.Mock).mockResolvedValueOnce({
      slots: [
        {
          time: "09:00",
          isAvailable: true,
          category: "AM" as const,
          availableResourceIds: [],
          availableGroupIds: [7],
        },
      ],
    });

    const venueWithNamedGroup = {
      ...mockVenue,
      groups: [
        {
          id: 7,
          name: "Window desks",
          combinedCapacity: 6,
          members: [
            { id: 100, name: "T1", capacity: 2 },
            { id: 101, name: "T2", capacity: 4 },
          ],
        },
      ],
    } as any;

    renderWithProviders(
      <BookingForm
        venue={venueWithNamedGroup}
        onSubmit={jest.fn()}
        initialTime="09:00"
        initialPartySize={5}
      />
    );
    selectMainSection();

    // No single resource fits a party of 5, so the trigger shows the placeholder. Open the dropdown
    // to reveal the group option.
    await waitFor(() => expect(screen.getByText("Select a resource")).toBeTruthy());
    fireEvent.press(screen.getByText("Select a resource"));

    // A named group uses its name in the label.
    await waitFor(() => {
      expect(screen.getByText("Window desks (6 places)")).toBeTruthy();
    });
  });

  it("uses the member-names fallback label for an unnamed group", async () => {
    const { fetchAvailability } = require("@/api/availability");
    (fetchAvailability as jest.Mock).mockResolvedValueOnce({
      slots: [
        {
          time: "09:00",
          isAvailable: true,
          category: "AM" as const,
          availableResourceIds: [],
          availableGroupIds: [7],
        },
      ],
    });

    const venueWithUnnamedGroup = {
      ...mockVenue,
      groups: [
        {
          id: 7,
          name: null,
          combinedCapacity: 6,
          members: [
            { id: 100, name: "T1", capacity: 2 },
            { id: 101, name: "T2", capacity: 4 },
          ],
        },
      ],
    } as any;

    renderWithProviders(
      <BookingForm
        venue={venueWithUnnamedGroup}
        onSubmit={jest.fn()}
        initialTime="09:00"
        initialPartySize={5}
      />
    );
    selectMainSection();

    // No single resource fits a party of 5, so the trigger shows the placeholder. Open the dropdown
    // to reveal the group option.
    await waitFor(() => expect(screen.getByText("Select a resource")).toBeTruthy());
    fireEvent.press(screen.getByText("Select a resource"));

    // An unnamed group falls back to "Resources T1 + T2 (6 places combined)".
    await waitFor(() => {
      expect(screen.getByText("Resources T1 + T2 (6 places combined)")).toBeTruthy();
    });
  });
});
