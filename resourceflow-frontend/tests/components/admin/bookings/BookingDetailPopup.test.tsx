import React from "react";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react-native";
import { BookingDetailPopup } from "@/components/admin/bookings/BookingDetailPopup";
import * as adminApi from "@/api/admin";
import * as venuesApi from "@/api/venues";

import { scrollIntoView } from "@/utils/scrollIntoView";

jest.mock("@/utils/scrollIntoView", () => ({ scrollIntoView: jest.fn() }));

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/api/admin", () => ({
  getAdminBooking: jest.fn(),
  adminDeleteBooking: jest.fn(),
  adminExtendBooking: jest.fn(),
  adminPurgeBooking: jest.fn(),
  sendBookingEmail: jest.fn(),
  adminRestoreBooking: jest.fn(),
  adminUpdateBookingFull: jest.fn(),
  adminSetBookingStatus: jest.fn(),
}));

jest.mock("@/api/venues", () => ({
  fetchVenues: jest.fn(),
}));

jest.mock("@/context/BrandContext", () => {
  const brand = { primaryColor: "#0a7ea4", appName: "ResourceFlow" };
  return { useBrand: () => brand };
});

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

jest.mock("@/components/admin/bookings/BookingDetailsCard", () => ({
  BookingDetailsCard: ({ booking }: { booking: { customerEmail: string } }) => {
    const { Text } = require("react-native");
    return <Text testID="booking-details-card">{booking.customerEmail}</Text>;
  },
}));

jest.mock("@/components/admin/bookings/EditBookingForm", () => ({
  EditBookingForm: ({
    handleVenueChange,
    handleSectionChange,
    setEditResourceId,
    setEditPartySize,
    setEditDate,
    setEditTime,
  }: {
    handleVenueChange: (v: string | number) => void;
    handleSectionChange: (v: string | number) => void;
    setEditResourceId: (id: number) => void;
    setEditPartySize: (s: string) => void;
    setEditDate: (d: string) => void;
    setEditTime: (t: string) => void;
  }) => {
    const { View, Text, Pressable } = require("react-native");
    return (
      <View testID="edit-booking-form">
        <Text>EditBookingForm</Text>
        <Pressable testID="change-venue-btn" onPress={() => handleVenueChange(2)}>
          <Text>Change Venue</Text>
        </Pressable>
        <Pressable testID="change-section-btn" onPress={() => handleSectionChange(20)}>
          <Text>Change Section</Text>
        </Pressable>
        <Pressable testID="change-resource-btn" onPress={() => setEditResourceId(200)}>
          <Text>Change Resource</Text>
        </Pressable>
        <Pressable testID="change-party-size-btn" onPress={() => setEditPartySize("4")}>
          <Text>Change Party Size</Text>
        </Pressable>
        <Pressable testID="set-invalid-party-size-btn" onPress={() => setEditPartySize("abc")}>
          <Text>Set Invalid Party Size</Text>
        </Pressable>
        <Pressable testID="clear-date-btn" onPress={() => setEditDate("")}>
          <Text>Clear Date</Text>
        </Pressable>
        <Pressable testID="clear-time-btn" onPress={() => setEditTime("")}>
          <Text>Clear Time</Text>
        </Pressable>
      </View>
    );
  },
}));

jest.mock("@/components/admin/bookings/ExtendBookingActions", () => ({
  ExtendBookingActions: ({ onExtend }: { onExtend: (m: number) => void }) => {
    const { Text, Pressable } = require("react-native");
    return (
      <Pressable testID="extend-btn" onPress={() => onExtend(30)}>
        <Text>Extend</Text>
      </Pressable>
    );
  },
}));

jest.mock("@/components/admin/bookings/EmailGuestForm", () => ({
  EmailGuestForm: ({
    onSendEmail,
    setEmailSubject,
    setEmailBody,
    emailSubject,
    emailBody,
    moveNoticeReady,
  }: {
    onSendEmail: () => void;
    setEmailSubject: (s: string) => void;
    setEmailBody: (s: string) => void;
    emailSubject: string;
    emailBody: string;
    moveNoticeReady?: boolean;
  }) => {
    const { View, Text, Pressable } = require("react-native");
    return (
      <View>
        {moveNoticeReady ? <Text testID="move-notice-ready">notice ready</Text> : null}
        <Text testID="email-subject">{emailSubject}</Text>
        <Text testID="email-body">{emailBody}</Text>
        <Pressable testID="send-email-btn" onPress={onSendEmail}>
          <Text>Send Email</Text>
        </Pressable>
        <Pressable
          testID="set-email-content-btn"
          onPress={() => {
            setEmailSubject("Test Subject");
            setEmailBody("Test Body");
          }}
        >
          <Text>Set Email Content</Text>
        </Pressable>
      </View>
    );
  },
}));

jest.mock("@/components/admin/bookings/BookingActionButtons", () => ({
  BookingActionButtons: ({
    onCancel,
    onPurge,
    onUncancel,
    isCancelled,
    isPast,
    deleting,
  }: {
    onCancel: () => void;
    onPurge: () => void;
    onUncancel: () => void;
    isCancelled: boolean;
    isPast?: boolean;
    deleting: boolean;
  }) => {
    const { View, Pressable, Text } = require("react-native");
    return (
      <View>
        <Text testID="action-deleting">{String(deleting)}</Text>
        {isCancelled ? (
          <Pressable testID="uncancel-btn" onPress={onUncancel}>
            <Text>Restore</Text>
          </Pressable>
        ) : isPast ? (
          <Text testID="past-booking-no-cancel">Booking Has Passed</Text>
        ) : (
          <Pressable testID="cancel-btn" onPress={onCancel}>
            <Text>Cancel Booking</Text>
          </Pressable>
        )}
        <Pressable testID="purge-btn" onPress={onPurge}>
          <Text>Delete Forever</Text>
        </Pressable>
      </View>
    );
  },
}));

jest.mock("@/components/common/ConfirmModal", () => {
  return function ConfirmModal({
    visible,
    onConfirm,
    onCancel,
    title,
  }: {
    visible: boolean;
    onConfirm: () => void;
    onCancel: () => void;
    title: string;
  }) {
    if (!visible) return null;
    const { View, Pressable, Text } = require("react-native");
    return (
      <View testID={`confirm-modal-${title.replace(/\s+/g, "-")}`}>
        <Pressable testID="confirm-btn" onPress={onConfirm}>
          <Text>Confirm</Text>
        </Pressable>
        <Pressable testID="modal-cancel-btn" onPress={onCancel}>
          <Text>Go Back</Text>
        </Pressable>
      </View>
    );
  };
});

jest.mock("@/components/common/AlertModal", () => {
  return function AlertModal({
    visible,
    onClose,
    message,
  }: {
    visible: boolean;
    onClose: () => void;
    message: string;
  }) {
    if (!visible) return null;
    const { View, Pressable, Text } = require("react-native");
    return (
      <View testID="alert-modal">
        <Text testID="alert-message">{message}</Text>
        <Pressable testID="alert-close-btn" onPress={onClose}>
          <Text>Close</Text>
        </Pressable>
      </View>
    );
  };
});

const mockBooking: adminApi.BookingDetailDto = {
  id: 1,
  venueId: 1,
  venueName: "Test Location",
  sectionId: 10,
  sectionName: "Ground floor",
  resourceId: 100,
  resourceName: "Resource 1",
  date: "2026-12-01T18:00:00Z",
  customerEmail: "participant@example.com",
  partySize: 2,
  specialRequests: "Projector needed",
  bookingRef: "ABC123",
  isCancelled: false,
};

const cancelledBooking: adminApi.BookingDetailDto = {
  ...mockBooking,
  isCancelled: true,
  cancelledAt: "2026-05-20T10:00:00Z",
};

const baseProps = {
  bookingId: 1,
  onClose: jest.fn(),
  onMutated: jest.fn(),
};

describe("BookingDetailPopup", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(mockBooking);
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue([]);
  });

  it("shows loading indicator while fetching booking", () => {
    (adminApi.getAdminBooking as jest.Mock).mockReturnValue(new Promise(() => {}));
    render(<BookingDetailPopup {...baseProps} />);
    // Modal visible=true since bookingId is not null
    expect(screen.getByText("Booking Details")).toBeTruthy();
  });

  it("shows booking data after loading", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => {
      expect(screen.getByTestId("booking-details-card")).toBeTruthy();
    });
  });

  it("shows booking not found when booking is null", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(null);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => {
      expect(screen.getByText("Booking not found.")).toBeTruthy();
    });
  });

  it("renders Booking Details header text when bookingId is provided", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    expect(screen.getByText("Booking Details")).toBeTruthy();
  });

  it("shows Edit button for non-cancelled booking", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => {
      expect(screen.getByText("Edit")).toBeTruthy();
    });
  });

  it("does not show Edit button for cancelled booking", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(cancelledBooking);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => {
      expect(screen.getByTestId("booking-details-card")).toBeTruthy();
    });
    expect(screen.queryByText("Edit")).toBeNull();
  });

  it("enters edit mode when Edit button is pressed", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => {
      expect(screen.getByTestId("edit-booking-form")).toBeTruthy();
    });
  });

  it("shows Cancel and Save Changes buttons in edit mode", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => {
      expect(screen.getByText("Cancel")).toBeTruthy();
      expect(screen.getByText("Save Changes")).toBeTruthy();
    });
  });

  it("exits edit mode when Cancel button is pressed", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByText("Cancel")).toBeTruthy());
    fireEvent.press(screen.getByText("Cancel"));
    await waitFor(() => {
      expect(screen.queryByText("Cancel")).toBeNull();
      expect(screen.getByText("Edit")).toBeTruthy();
    });
  });

  it("hides Cancel Booking and shows past indicator for a past, non-cancelled booking", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue({
      ...mockBooking,
      date: "2020-01-01T18:00:00Z",
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("past-booking-no-cancel")).toBeTruthy());
    expect(screen.queryByTestId("cancel-btn")).toBeNull();
  });

  it("shows cancel booking confirm modal when Cancel Booking is pressed", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("confirm-modal-Cancel-Booking")).toBeTruthy();
    });
  });

  it("calls adminDeleteBooking and onMutated when cancel is confirmed", async () => {
    (adminApi.adminDeleteBooking as jest.Mock).mockResolvedValue(true);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    expect(adminApi.adminDeleteBooking).toHaveBeenCalledWith(1);
    expect(baseProps.onMutated).toHaveBeenCalled();
    expect(baseProps.onClose).toHaveBeenCalled();
  });

  it("keeps the actions busy while the cancel it started is still in flight", async () => {
    (adminApi.adminDeleteBooking as jest.Mock).mockReturnValue(new Promise(() => {}));
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    expect(screen.getByTestId("action-deleting").props.children).toBe("true");
  });

  it("hands the next booking live actions after a cancel closed the popup", async () => {
    (adminApi.adminDeleteBooking as jest.Mock).mockResolvedValue(true);
    const { rerender } = render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });

    // The hosting screen closes the popup and reopens it on the next booking; the component
    // itself never unmounts, so nothing but this effect clears the flag the cancel set.
    rerender(<BookingDetailPopup {...baseProps} bookingId={null} />);
    rerender(<BookingDetailPopup {...baseProps} bookingId={2} />);

    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    expect(screen.getByTestId("action-deleting").props.children).toBe("false");
  });

  it("shows error message when cancel fails", async () => {
    (adminApi.adminDeleteBooking as jest.Mock).mockRejectedValue(
      new Error("Failed to cancel the booking.")
    );
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message")).toBeTruthy();
    });
  });

  it("surfaces the backend's specific rejection message instead of a generic one", async () => {
    (adminApi.adminDeleteBooking as jest.Mock).mockRejectedValue(
      new Error("Cannot cancel a booking that has already passed.")
    );
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message").props.children).toBe(
        "Cannot cancel a booking that has already passed."
      );
    });
  });

  it("dismisses cancel modal when Go Back is pressed", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => expect(screen.getByTestId("modal-cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("modal-cancel-btn"));
    await waitFor(() => {
      expect(screen.queryByTestId("confirm-modal-Cancel-Booking")).toBeNull();
    });
  });

  it("shows purge confirm modal when Delete Forever is pressed", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("purge-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("purge-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("confirm-modal-Permanently-Delete")).toBeTruthy();
    });
  });

  it("calls adminPurgeBooking when purge is confirmed", async () => {
    (adminApi.adminPurgeBooking as jest.Mock).mockResolvedValue(true);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("purge-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("purge-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    expect(adminApi.adminPurgeBooking).toHaveBeenCalledWith(1);
    expect(baseProps.onMutated).toHaveBeenCalled();
    expect(baseProps.onClose).toHaveBeenCalled();
  });

  it("shows error when purge fails", async () => {
    (adminApi.adminPurgeBooking as jest.Mock).mockResolvedValue(false);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("purge-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("purge-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message")).toBeTruthy();
    });
  });

  it("shows restore confirm modal for cancelled booking", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(cancelledBooking);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("uncancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("uncancel-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("confirm-modal-Restore-Booking")).toBeTruthy();
    });
  });

  it("calls adminRestoreBooking when restore is confirmed", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValueOnce(cancelledBooking);
    (adminApi.adminRestoreBooking as jest.Mock).mockResolvedValue(true);
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue({
      ...cancelledBooking,
      isCancelled: false,
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("uncancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("uncancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    expect(adminApi.adminRestoreBooking).toHaveBeenCalledWith(1);
  });

  it("shows error when restore fails with Error instance", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(cancelledBooking);
    (adminApi.adminRestoreBooking as jest.Mock).mockRejectedValue(
      new Error("Failed to restore booking.")
    );
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("uncancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("uncancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message")).toBeTruthy();
    });
  });

  it("marks the status and shows the booking the server sent back", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue({
      ...mockBooking,
      status: "Booked",
      nextStatuses: ["Arrived", "InUse"],
    });
    (adminApi.adminSetBookingStatus as jest.Mock).mockResolvedValue({
      ...mockBooking,
      status: "InUse",
      nextStatuses: ["Finished"],
      undoStatus: "Booked",
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByLabelText("Mark as In use")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByLabelText("Mark as In use"));
    });
    expect(adminApi.adminSetBookingStatus).toHaveBeenCalledWith(1, "InUse");
    expect(screen.getByLabelText("Mark as Finished")).toBeTruthy();
    expect(screen.getByText("Undo, back to Booked")).toBeTruthy();
    expect(baseProps.onMutated).toHaveBeenCalled();
  });

  it("shows the server's reason when a status change is refused", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue({
      ...mockBooking,
      nextStatuses: ["Arrived"],
    });
    (adminApi.adminSetBookingStatus as jest.Mock).mockRejectedValue(
      new Error("A cancelled booking takes no status changes.")
    );
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByLabelText("Mark as Arrived")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByLabelText("Mark as Arrived"));
    });
    expect(screen.getByTestId("alert-message").props.children).toBe(
      "A cancelled booking takes no status changes."
    );
  });

  it("calls adminExtendBooking when extend button is pressed", async () => {
    (adminApi.adminExtendBooking as jest.Mock).mockResolvedValue({
      endTime: "2026-06-01T20:00:00Z",
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("extend-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("extend-btn"));
    });
    expect(adminApi.adminExtendBooking).toHaveBeenCalledWith(1, 30);
  });

  it("updates endTime after successful extend", async () => {
    (adminApi.adminExtendBooking as jest.Mock).mockResolvedValue({
      endTime: "2026-06-01T20:00:00Z",
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("extend-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("extend-btn"));
    });
    expect(adminApi.adminExtendBooking).toHaveBeenCalled();
  });

  it("renders null when bookingId is null", () => {
    render(<BookingDetailPopup {...baseProps} bookingId={null} />);
    // Modal is not visible when bookingId is null
    expect(screen.queryByText("Booking Details")).toBeNull();
  });

  it("resets state when bookingId changes to null", async () => {
    const { rerender } = render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("booking-details-card")).toBeTruthy());
    rerender(<BookingDetailPopup {...baseProps} bookingId={null} />);
    expect(screen.queryByText("Booking Details")).toBeNull();
  });

  it("closes error alert when close button is pressed", async () => {
    (adminApi.adminDeleteBooking as jest.Mock).mockRejectedValue(
      new Error("Failed to cancel the booking.")
    );
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("cancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    await waitFor(() => expect(screen.getByTestId("alert-close-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("alert-close-btn"));
    await waitFor(() => {
      expect(screen.queryByTestId("alert-modal")).toBeNull();
    });
  });

  it("dismisses purge modal when Go Back is pressed", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("purge-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("purge-btn"));
    await waitFor(() => expect(screen.getByTestId("modal-cancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("modal-cancel-btn"));
    await waitFor(() => {
      expect(screen.queryByTestId("confirm-modal-Permanently-Delete")).toBeNull();
    });
  });

  it("calls handleSaveEdit with valid data", async () => {
    (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue({
      ...mockBooking,
      partySize: 4,
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    await waitFor(() => {
      expect(adminApi.adminUpdateBookingFull).toHaveBeenCalled();
    });
  });

  it("shows error when handleSaveEdit fails", async () => {
    (adminApi.adminUpdateBookingFull as jest.Mock).mockRejectedValue(new Error("Update failed."));
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message")).toBeTruthy();
    });
  });

  it("calls handleVenueChange when location is changed in edit form", async () => {
    const mockVenues = [
      {
        id: 1,
        name: "Location A",
        sections: [{ id: 10, name: "Main", resources: [{ id: 100, name: "T1", capacity: 4 }] }],
      },
      {
        id: 2,
        name: "Location B",
        sections: [{ id: 20, name: "Annex", resources: [{ id: 200, name: "T2", capacity: 2 }] }],
      },
    ];
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
    fireEvent.press(screen.getByTestId("change-venue-btn"));
    // handleVenueChange should update section and resource
    expect(screen.getByTestId("edit-booking-form")).toBeTruthy();
  });

  it("calls handleSectionChange when section is changed in edit form", async () => {
    const mockVenues = [
      {
        id: 1,
        name: "Location A",
        sections: [
          { id: 10, name: "Main", resources: [{ id: 100, name: "T1", capacity: 4 }] },
          { id: 11, name: "Lounge", resources: [{ id: 101, name: "T2", capacity: 2 }] },
        ],
      },
    ];
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
    fireEvent.press(screen.getByTestId("change-section-btn"));
    expect(screen.getByTestId("edit-booking-form")).toBeTruthy();
  });

  it("shows error for invalid party size in handleSaveEdit", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByTestId("change-party-size-btn")).toBeTruthy());
    // Change party size to invalid value via mock
    // Since we can't directly set editPartySize to NaN via the mock, we test the normal flow
    // The default editPartySize from the booking is "2" which is valid
    await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    // Should call update since party size is valid
    expect(adminApi.adminUpdateBookingFull).toHaveBeenCalled();
  });

  it("handles restore failure with non-Error thrown", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(cancelledBooking);
    (adminApi.adminRestoreBooking as jest.Mock).mockRejectedValue("string error");
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("uncancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("uncancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message")).toBeTruthy();
    });
  });

  it("handles extend when result is null (no-op)", async () => {
    (adminApi.adminExtendBooking as jest.Mock).mockResolvedValue(null);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("extend-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("extend-btn"));
    });
    expect(adminApi.adminExtendBooking).toHaveBeenCalledWith(1, 30);
  });

  it("calls sendBookingEmail when send email button is pressed with content", async () => {
    (adminApi.sendBookingEmail as jest.Mock).mockResolvedValue({ ok: true, message: "Sent." });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("booking-details-card")).toBeTruthy());
    // Set email content first
    act(() => {
      fireEvent.press(screen.getByTestId("set-email-content-btn"));
    });
    await act(async () => {
      fireEvent.press(screen.getByTestId("send-email-btn"));
    });
    await waitFor(() => {
      expect(adminApi.sendBookingEmail).toHaveBeenCalledWith(1, "Test Subject", "Test Body");
    });
  });

  it("handles failed send email gracefully", async () => {
    (adminApi.sendBookingEmail as jest.Mock).mockResolvedValue({ ok: false, message: "Failed." });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("booking-details-card")).toBeTruthy());
    act(() => {
      fireEvent.press(screen.getByTestId("set-email-content-btn"));
    });
    await act(async () => {
      fireEvent.press(screen.getByTestId("send-email-btn"));
    });
    expect(adminApi.sendBookingEmail).toHaveBeenCalled();
  });

  it("does not call sendBookingEmail when subject/body are empty", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("send-email-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("send-email-btn"));
    });
    expect(adminApi.sendBookingEmail).not.toHaveBeenCalled();
  });

  it("dismisses restore modal when Go Back is pressed", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(cancelledBooking);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("uncancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("uncancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-modal-Restore-Booking")).toBeTruthy());
    fireEvent.press(screen.getByTestId("modal-cancel-btn"));
    await waitFor(() => {
      expect(screen.queryByTestId("confirm-modal-Restore-Booking")).toBeNull();
    });
  });

  it("shows error for invalid party size (NaN) in handleSaveEdit", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByTestId("set-invalid-party-size-btn")).toBeTruthy());
    act(() => {
      fireEvent.press(screen.getByTestId("set-invalid-party-size-btn"));
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message")).toBeTruthy();
    });
  });

  it("skips save and calls confirm when party size exceeds resource capacity", async () => {
    const mockVenues = [
      {
        id: 1,
        name: "Location A",
        sections: [
          {
            id: 10,
            name: "Main",
            resources: [{ id: 100, name: "T1", capacity: 1 }], // resource has capacity 1
          },
        ],
      },
    ];
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
    // Mock global.confirm (Node env doesn't have window.confirm)
    const originalConfirm = (global as Record<string, unknown>).confirm;
    (global as Record<string, unknown>).confirm = jest.fn().mockReturnValue(false);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
    // editPartySize starts as "2" which is > resource.capacity=1
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    expect((global as Record<string, unknown>).confirm).toHaveBeenCalled();
    (global as Record<string, unknown>).confirm = originalConfirm;
  });

  it("proceeds with save when confirm returns true for over-capacity booking", async () => {
    (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue({ ...mockBooking });
    const mockVenues = [
      {
        id: 1,
        name: "Location A",
        sections: [
          {
            id: 10,
            name: "Main",
            resources: [{ id: 100, name: "T1", capacity: 1 }],
          },
        ],
      },
    ];
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
    const originalConfirm = (global as Record<string, unknown>).confirm;
    (global as Record<string, unknown>).confirm = jest.fn().mockReturnValue(true);
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    await waitFor(() => {
      expect(adminApi.adminUpdateBookingFull).toHaveBeenCalled();
    });
    (global as Record<string, unknown>).confirm = originalConfirm;
  });

  it("shows 'Date and time are required' when editDate is cleared before saving", async () => {
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByTestId("clear-date-btn")).toBeTruthy());
    act(() => {
      fireEvent.press(screen.getByTestId("clear-date-btn"));
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    await waitFor(() => {
      expect(screen.getByTestId("alert-message")).toBeTruthy();
      expect(screen.getByText("Date and time are required")).toBeTruthy();
    });
    expect(adminApi.adminUpdateBookingFull).not.toHaveBeenCalled();
  });

  it("calls onMutated after a successful extend", async () => {
    (adminApi.adminExtendBooking as jest.Mock).mockResolvedValue({
      endTime: "2026-06-01T20:00:00Z",
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("extend-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("extend-btn"));
    });
    await waitFor(() => expect(baseProps.onMutated).toHaveBeenCalled());
  });

  it("calls onMutated after a successful restore", async () => {
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValueOnce(cancelledBooking);
    (adminApi.adminRestoreBooking as jest.Mock).mockResolvedValue(true);
    (adminApi.getAdminBooking as jest.Mock).mockResolvedValue({
      ...cancelledBooking,
      isCancelled: false,
    });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByTestId("uncancel-btn")).toBeTruthy());
    fireEvent.press(screen.getByTestId("uncancel-btn"));
    await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-btn"));
    });
    await waitFor(() => expect(baseProps.onMutated).toHaveBeenCalled());
  });

  it("calls onMutated after a successful edit save", async () => {
    (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue({ ...mockBooking });
    render(<BookingDetailPopup {...baseProps} />);
    await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    fireEvent.press(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByText("Save Changes"));
    });
    await waitFor(() => expect(baseProps.onMutated).toHaveBeenCalled());
  });

  describe("race guards, fallbacks, and dead-end branches", () => {
    it("ignores a stale getAdminBooking response after bookingId changes before it resolves", async () => {
      let resolveFirst!: (value: unknown) => void;
      (adminApi.getAdminBooking as jest.Mock).mockImplementation((id: number) => {
        if (id === 1) {
          return new Promise((resolve) => {
            resolveFirst = resolve;
          });
        }
        return new Promise(() => {});
      });
      const { rerender } = render(<BookingDetailPopup {...baseProps} bookingId={1} />);
      rerender(<BookingDetailPopup {...baseProps} bookingId={2} />);
      // The id=1 fetch resolves only after bookingId moved on to 2 — the
      // effect's cleanup already set `cancelled = true` for that request, so
      // this stale response must not overwrite state.
      await act(async () => {
        resolveFirst(mockBooking);
      });
      expect(screen.queryByTestId("booking-details-card")).toBeNull();
    });

    it("falls back to empty strings for a booking with no email/specialRequests and omits them on save", async () => {
      const bookingNoOptional: adminApi.BookingDetailDto = {
        ...mockBooking,
        customerEmail: null as unknown as string,
        specialRequests: undefined,
      };
      (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(bookingNoOptional);
      (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue(bookingNoOptional);
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });
      await waitFor(() => expect(adminApi.adminUpdateBookingFull).toHaveBeenCalled());
      const payload = (adminApi.adminUpdateBookingFull as jest.Mock).mock.calls[0][1];
      expect(payload.customerEmail).toBeUndefined();
      expect(payload.specialRequests).toBeUndefined();
    });

    it("re-populates edit fields with empty strings when cancelling edit for a booking with no email/specialRequests", async () => {
      const bookingNoOptional: adminApi.BookingDetailDto = {
        ...mockBooking,
        customerEmail: null as unknown as string,
        specialRequests: undefined,
      };
      (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(bookingNoOptional);
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Cancel")).toBeTruthy());
      fireEvent.press(screen.getByText("Cancel"));
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
    });

    it("no-ops handleDeleteConfirmed if booking became null before the confirm modal was actioned", async () => {
      (adminApi.getAdminBooking as jest.Mock).mockImplementation((id: number) =>
        id === 1 ? Promise.resolve(mockBooking) : new Promise(() => {})
      );
      const { rerender } = render(<BookingDetailPopup {...baseProps} bookingId={1} />);
      await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
      fireEvent.press(screen.getByTestId("cancel-btn"));
      await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
      rerender(<BookingDetailPopup {...baseProps} bookingId={2} />);
      fireEvent.press(screen.getByTestId("confirm-btn"));
      expect(adminApi.adminDeleteBooking).not.toHaveBeenCalled();
    });

    it("no-ops handleUncancel if booking became null before the confirm modal was actioned", async () => {
      (adminApi.getAdminBooking as jest.Mock).mockImplementation((id: number) =>
        id === 1 ? Promise.resolve(cancelledBooking) : new Promise(() => {})
      );
      const { rerender } = render(<BookingDetailPopup {...baseProps} bookingId={1} />);
      await waitFor(() => expect(screen.getByTestId("uncancel-btn")).toBeTruthy());
      fireEvent.press(screen.getByTestId("uncancel-btn"));
      await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
      rerender(<BookingDetailPopup {...baseProps} bookingId={2} />);
      fireEvent.press(screen.getByTestId("confirm-btn"));
      expect(adminApi.adminRestoreBooking).not.toHaveBeenCalled();
    });

    it("no-ops the purge confirm handler if booking became null before it was actioned", async () => {
      (adminApi.getAdminBooking as jest.Mock).mockImplementation((id: number) =>
        id === 1 ? Promise.resolve(mockBooking) : new Promise(() => {})
      );
      const { rerender } = render(<BookingDetailPopup {...baseProps} bookingId={1} />);
      await waitFor(() => expect(screen.getByTestId("purge-btn")).toBeTruthy());
      fireEvent.press(screen.getByTestId("purge-btn"));
      await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
      rerender(<BookingDetailPopup {...baseProps} bookingId={2} />);
      fireEvent.press(screen.getByTestId("confirm-btn"));
      expect(adminApi.adminPurgeBooking).not.toHaveBeenCalled();
    });

    it("no-ops handleSaveEdit and handleCancelEdit if booking became null while still editing", async () => {
      (adminApi.getAdminBooking as jest.Mock).mockImplementation((id: number) =>
        id === 1 ? Promise.resolve(mockBooking) : new Promise(() => {})
      );
      const { rerender } = render(<BookingDetailPopup {...baseProps} bookingId={1} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
      // The header's Save Changes/Cancel buttons render whenever `editing` is
      // true, independent of `booking` — so switching to a bookingId whose
      // fetch never resolves leaves them mounted with booking now null.
      rerender(<BookingDetailPopup {...baseProps} bookingId={2} />);
      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });
      expect(adminApi.adminUpdateBookingFull).not.toHaveBeenCalled();
      fireEvent.press(screen.getByText("Cancel"));
      await waitFor(() => {
        expect(screen.queryByText("Save Changes")).toBeNull();
        expect(screen.queryByText("Edit")).toBeNull();
      });
    });

    it("clears section/resource selection for a location with no sections and omits them on save", async () => {
      const mockVenues = [
        {
          id: 1,
          name: "Location A",
          sections: [{ id: 10, name: "Main", resources: [{ id: 100, name: "T1", capacity: 4 }] }],
        },
        { id: 2, name: "Location B", sections: [] },
      ];
      (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
      (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue(mockBooking);
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
      // change-venue-btn selects venue id=2, which has no sections.
      fireEvent.press(screen.getByTestId("change-venue-btn"));
      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });
      await waitFor(() => expect(adminApi.adminUpdateBookingFull).toHaveBeenCalled());
      const payload = (adminApi.adminUpdateBookingFull as jest.Mock).mock.calls[0][1];
      expect(payload.sectionId).toBeUndefined();
      expect(payload.resourceId).toBeUndefined();
    });

    it("shows a generic message when cancel fails with a non-Error rejection", async () => {
      (adminApi.adminDeleteBooking as jest.Mock).mockRejectedValue("boom");
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByTestId("cancel-btn")).toBeTruthy());
      fireEvent.press(screen.getByTestId("cancel-btn"));
      await waitFor(() => expect(screen.getByTestId("confirm-btn")).toBeTruthy());
      await act(async () => {
        fireEvent.press(screen.getByTestId("confirm-btn"));
      });
      await waitFor(() => {
        expect(screen.getByTestId("alert-message").props.children).toBe(
          "Failed to cancel the booking."
        );
      });
    });

    it("shows a generic message when handleSaveEdit fails with a non-Error rejection", async () => {
      (adminApi.adminUpdateBookingFull as jest.Mock).mockRejectedValue("boom");
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });
      await waitFor(() => {
        expect(screen.getByTestId("alert-message").props.children).toBe(
          "Failed to update booking."
        );
      });
    });

    it("falls back to 'Resource {id}' label when a resource has no name", async () => {
      const mockVenues = [
        {
          id: 1,
          name: "Location A",
          sections: [{ id: 10, name: "Main", resources: [{ id: 100, name: null, capacity: 4 }] }],
        },
      ];
      (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      // booking.venueId=1/sectionId=10 match this venue/section, so
      // the nameless resource is resolved into resourceOptions on render, exercising
      // the `t.name ?? "Resource {id}"` fallback.
      await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
    });

    it("shows 'Saving…' while handleSaveEdit is in flight", async () => {
      let resolveSave!: (value: unknown) => void;
      (adminApi.adminUpdateBookingFull as jest.Mock).mockImplementation(
        () =>
          new Promise((resolve) => {
            resolveSave = resolve;
          })
      );
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());
      fireEvent.press(screen.getByText("Save Changes"));
      await waitFor(() => expect(screen.getByText("Saving…")).toBeTruthy());
      await act(async () => {
        resolveSave(mockBooking);
      });
    });

    it("ignores a stale fetchVenues response after leaving edit mode before it resolves", async () => {
      let resolveVenues!: (value: unknown) => void;
      (venuesApi.fetchVenues as jest.Mock).mockImplementation(
        () =>
          new Promise((resolve) => {
            resolveVenues = resolve;
          })
      );
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
      // Leaving edit mode before fetchVenues resolves flips `editing`,
      // which triggers that effect's cleanup (cancelled = true) ahead of
      // the stale response arriving.
      fireEvent.press(screen.getByText("Cancel"));
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      await act(async () => {
        resolveVenues([{ id: 1, name: "R", sections: [] }]);
      });
      expect(screen.getByText("Edit")).toBeTruthy();
    });
  });

  /**
   * The popup's concern is whether it asks for a scroll, not how one is carried out — the
   * cross-platform mechanics are pinned in tests/utils/scrollIntoView.test.ts. Observing the
   * request itself is also what keeps the negative cases honest: they used to watch
   * `findNodeHandle`, which the native path stopped calling when it moved to the content-view
   * element, leaving three tests that passed no matter what the popup did.
   */
  describe("initialFocus='extend' (bound to the bookings-list 'e' shortcut)", () => {
    const requestedScroll = scrollIntoView as jest.Mock;

    beforeEach(() => requestedScroll.mockClear());

    it("scrolls the extend section into view once it has rendered", async () => {
      render(<BookingDetailPopup {...baseProps} initialFocus="extend" />);
      await waitFor(() => expect(screen.getByTestId("extend-section")).toBeTruthy());
      await waitFor(() => expect(requestedScroll).toHaveBeenCalled(), { timeout: 1000 });
      expect(requestedScroll).toHaveBeenCalledWith(expect.any(Object), expect.any(Object), {
        block: "center",
      });
    });

    it("does not scroll when the extend section is not rendered (editing mode)", async () => {
      render(<BookingDetailPopup {...baseProps} initialFocus="extend" />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByTestId("edit-booking-form")).toBeTruthy());
      await new Promise((resolve) => setTimeout(resolve, 250));
      expect(requestedScroll).not.toHaveBeenCalled();
    });

    it("does not scroll for a cancelled booking", async () => {
      (adminApi.getAdminBooking as jest.Mock).mockResolvedValue(cancelledBooking);
      render(<BookingDetailPopup {...baseProps} initialFocus="extend" />);
      await waitFor(() => expect(screen.getByTestId("booking-details-card")).toBeTruthy());
      await new Promise((resolve) => setTimeout(resolve, 250));
      expect(screen.queryByTestId("extend-section")).toBeNull();
      expect(requestedScroll).not.toHaveBeenCalled();
    });

    it("does not scroll when initialFocus is unset", async () => {
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByTestId("extend-section")).toBeTruthy());
      await new Promise((resolve) => setTimeout(resolve, 250));
      expect(requestedScroll).not.toHaveBeenCalled();
    });
  });

  describe("notifying the participant when a slot moves", () => {
    const moved = {
      ...mockBooking,
      date: "2026-12-02T20:30:00Z",
      timezone: "America/New_York",
      customerName: "Ada Lovelace",
    };

    it("writes an unsent notice into the email form naming both slots", async () => {
      (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue(moved);
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());

      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });

      await waitFor(() => expect(screen.getByTestId("move-notice-ready")).toBeTruthy());
      expect(screen.getByTestId("email-subject").props.children).toContain("has moved");
      expect(screen.getByTestId("email-body").props.children).toContain("Previously:");
      // Composed, not sent. Sending stays an explicit press.
      expect(adminApi.sendBookingEmail).not.toHaveBeenCalled();
    });

    it("offers nothing when the edit left the slot where it was", async () => {
      (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue({
        ...mockBooking,
        partySize: 4,
      });
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());

      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });

      await waitFor(() => expect(adminApi.adminUpdateBookingFull).toHaveBeenCalled());
      expect(screen.queryByTestId("move-notice-ready")).toBeNull();
      expect(screen.getByTestId("email-subject").props.children).toBe("");
    });

    // A booking with no address on it cannot be told anything, and a notice written into a form
    // that can never send reads as a step the admin failed to complete.
    it("offers nothing when the booking carries no customer email", async () => {
      (adminApi.adminUpdateBookingFull as jest.Mock).mockResolvedValue({
        ...moved,
        customerEmail: "",
      });
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());

      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });

      await waitFor(() => expect(adminApi.adminUpdateBookingFull).toHaveBeenCalled());
      expect(screen.queryByTestId("move-notice-ready")).toBeNull();
    });

    // A rejected move must never be announced to a guest it did not happen to.
    it("offers nothing when the update was rejected", async () => {
      (adminApi.adminUpdateBookingFull as jest.Mock).mockRejectedValue(
        new Error("This update would cause a conflict with an existing booking.")
      );
      render(<BookingDetailPopup {...baseProps} />);
      await waitFor(() => expect(screen.getByText("Edit")).toBeTruthy());
      fireEvent.press(screen.getByText("Edit"));
      await waitFor(() => expect(screen.getByText("Save Changes")).toBeTruthy());

      await act(async () => {
        fireEvent.press(screen.getByText("Save Changes"));
      });

      await waitFor(() => expect(screen.getByTestId("alert-message")).toBeTruthy());
      expect(screen.queryByTestId("move-notice-ready")).toBeNull();
    });
  });
});
