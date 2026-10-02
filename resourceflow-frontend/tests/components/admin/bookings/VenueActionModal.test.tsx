import React from "react";
import { render, fireEvent, waitFor, act } from "@testing-library/react-native";
import VenueActionModal from "@/components/admin/bookings/VenueActionModal";
import * as adminApi from "@/api/admin";
import { useAppTheme } from "@/hooks/use-app-theme";

// Mock the API and theme hook
jest.mock("@/api/admin");
jest.mock("@/hooks/use-app-theme");

const mockVenues = [
  { id: 1, name: "Test Location 1", bookingsPausedUntil: null, activeBookingsCount: 5 },
  {
    id: 2,
    name: "Test Location 2",
    bookingsPausedUntil: new Date(Date.now() + 3600000).toISOString(),
    activeBookingsCount: 2,
  },
];

const mockTheme = {
  colors: {
    card: "#fff",
    border: "#eee",
    muted: "#888",
    success: "#16a34a",
  },
  primaryColor: "#007AFF",
};

describe("VenueActionModal", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (useAppTheme as jest.Mock).mockReturnValue(mockTheme);
    (adminApi.adminGetVenues as jest.Mock).mockResolvedValue(mockVenues);
  });

  it("renders correctly when visible and type is pause", async () => {
    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={() => {}} />
    );

    // Wait for loading to finish
    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    expect(getByText("Pause Bookings")).toBeTruthy();
    expect(
      getByText("Pausing turns away new bookings for the next hour. Later times stay bookable.")
    ).toBeTruthy();
    expect(getByText("Test Location 1")).toBeTruthy();
    expect(getByText("Test Location 2")).toBeTruthy();
  });

  it("renders correctly when type is extend", async () => {
    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="extend" onClose={() => {}} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    expect(getByText("Extend Bookings")).toBeTruthy();
    expect(getByText("Select a location to extend all active bookings by 1 hour.")).toBeTruthy();
  });

  it("calls pauseVenueBookings when a location is clicked and type is pause", async () => {
    const onSuccess = jest.fn();
    const onClose = jest.fn();
    (adminApi.pauseVenueBookings as jest.Mock).mockResolvedValue({ ok: true });

    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={onClose} onSuccess={onSuccess} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    fireEvent.press(getByText("Test Location 1"));

    await waitFor(() => {
      expect(adminApi.pauseVenueBookings).toHaveBeenCalledWith(1, 60);
      expect(onSuccess).toHaveBeenCalledWith(
        expect.stringMatching(
          /^Bookings for Test Location 1 up to .+ are paused\. Later times stay open\.$/
        )
      );
      expect(onClose).toHaveBeenCalled();
    });
  });

  it("calls unpauseVenueBookings when a paused location is clicked and type is pause", async () => {
    const onSuccess = jest.fn();
    const onClose = jest.fn();
    (adminApi.unpauseVenueBookings as jest.Mock).mockResolvedValue({ ok: true });

    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={onClose} onSuccess={onSuccess} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    fireEvent.press(getByText("Test Location 2"));

    await waitFor(() => {
      expect(adminApi.unpauseVenueBookings).toHaveBeenCalledWith(2);
      expect(onSuccess).toHaveBeenCalledWith("Bookings for Test Location 2 have been resumed.");
      expect(onClose).toHaveBeenCalled();
    });
  });

  it("calls extendVenueBookings and shows extended bookings list when type is extend", async () => {
    const extendedBookings = [
      {
        id: 101,
        customerEmail: "test@example.com",
        date: new Date().toISOString(),
        partySize: 2,
        endTime: new Date(Date.now() + 7200000).toISOString(),
      },
    ];
    (adminApi.extendVenueBookings as jest.Mock).mockResolvedValue({
      ok: true,
      extendedBookings,
    });

    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="extend" onClose={() => {}} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    fireEvent.press(getByText("Test Location 1"));

    await waitFor(() => {
      expect(adminApi.extendVenueBookings).toHaveBeenCalledWith(1, 60);
      expect(getByText("Bookings Extended")).toBeTruthy();
      expect(getByText("test@example.com")).toBeTruthy();
    });
  });

  it("calls onClose when close button is pressed", () => {
    const onClose = jest.fn();
    const { getByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={onClose} />
    );

    fireEvent.press(getByTestId("close-modal-button"));
    expect(onClose).toHaveBeenCalled();
  });

  it("handles error when loading locations fails", async () => {
    const consoleSpy = jest.spyOn(console, "error").mockImplementation(() => {});
    (adminApi.adminGetVenues as jest.Mock).mockRejectedValue(new Error("Network error"));

    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={() => {}} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    expect(consoleSpy).toHaveBeenCalledWith("Failed to load locations", expect.any(Error));
    expect(getByText("No locations found.")).toBeTruthy();
    consoleSpy.mockRestore();
  });

  it("shows empty state when no locations are found", async () => {
    (adminApi.adminGetVenues as jest.Mock).mockResolvedValue([]);

    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={() => {}} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    expect(getByText("No locations found.")).toBeTruthy();
  });

  it("calls onSuccess and onClose when extend finds no active bookings", async () => {
    const onSuccess = jest.fn();
    const onClose = jest.fn();
    (adminApi.extendVenueBookings as jest.Mock).mockResolvedValue({
      ok: true,
      extendedBookings: [],
    });

    const { getByText, queryByTestId } = render(
      <VenueActionModal
        visible={true}
        actionType="extend"
        onClose={onClose}
        onSuccess={onSuccess}
      />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    fireEvent.press(getByText("Test Location 1"));

    await waitFor(() => {
      expect(onSuccess).toHaveBeenCalledWith(
        "No active bookings found to extend for Test Location 1."
      );
      expect(onClose).toHaveBeenCalled();
    });
  });

  it("shows extended bookings with null endTime as 'Extended'", async () => {
    const extendedBookings = [
      {
        id: 200,
        customerEmail: "noend@test.com",
        date: new Date().toISOString(),
        partySize: 3,
        endTime: null,
      },
    ];
    (adminApi.extendVenueBookings as jest.Mock).mockResolvedValue({
      ok: true,
      extendedBookings,
    });

    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="extend" onClose={() => {}} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    fireEvent.press(getByText("Test Location 1"));

    await waitFor(() => {
      expect(getByText("noend@test.com")).toBeTruthy();
      expect(getByText("Bookings Extended")).toBeTruthy();
    });
  });

  it("handles error in handleAction gracefully", async () => {
    const consoleSpy = jest.spyOn(console, "error").mockImplementation(() => {});
    (adminApi.pauseVenueBookings as jest.Mock).mockRejectedValue(new Error("Network error"));

    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={() => {}} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    await act(async () => {
      fireEvent.press(getByText("Test Location 1"));
    });

    expect(consoleSpy).toHaveBeenCalled();
    consoleSpy.mockRestore();
  });

  it("does not load locations when visible is false", () => {
    render(<VenueActionModal visible={false} actionType="pause" onClose={() => {}} />);
    expect(adminApi.adminGetVenues).not.toHaveBeenCalled();
  });

  it("shows PAUSED badge for paused location", async () => {
    const { getByText, queryByTestId } = render(
      <VenueActionModal visible={true} actionType="pause" onClose={() => {}} />
    );

    await waitFor(() => {
      expect(queryByTestId("loading-indicator")).toBeNull();
    });

    expect(getByText("PAUSED")).toBeTruthy();
  });
});
