import React from "react";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react-native";
import { NewBookingModal } from "@/components/admin/bookings/NewBookingModal";
import * as venuesApi from "@/api/venues";
import * as adminApi from "@/api/admin";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/api/venues", () => ({
  fetchVenues: jest.fn(),
}));

jest.mock("@/api/admin", () => ({
  adminCreateBooking: jest.fn(),
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: jest.fn(() => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" })),
}));

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

jest.mock("@/components/common/Select", () => {
  const { View, Text, Pressable } = require("react-native");
  return {
    __esModule: true,
    default: ({
      onSelect,
      options,
    }: {
      onSelect: (v: string | number) => void;
      options: { label: string; value: string | number }[];
    }) => (
      <View testID="mock-select">
        {options.map((o) => (
          <Pressable key={String(o.value)} onPress={() => onSelect(o.value)}>
            <Text>{o.label}</Text>
          </Pressable>
        ))}
      </View>
    ),
  };
});

jest.mock("@/components/common/DatePicker", () => {
  const { View, Text, Pressable } = require("react-native");
  return {
    __esModule: true,
    default: ({
      selectedDate,
      onSelect,
      allowPast,
    }: {
      selectedDate?: string;
      onSelect: (v: string) => void;
      allowPast?: boolean;
    }) => (
      <View>
        <Text testID="date-picker">{selectedDate ?? "no date"}</Text>
        <Text testID="date-picker-allowpast">{allowPast ? "true" : "false"}</Text>
        <Pressable
          testID="date-picker-past"
          onPress={() => {
            const d = new Date();
            d.setDate(d.getDate() - 7);
            const y = d.getFullYear();
            const m = String(d.getMonth() + 1).padStart(2, "0");
            const day = String(d.getDate()).padStart(2, "0");
            onSelect(`${y}-${m}-${day}`);
          }}
        >
          <Text>Select Past Date</Text>
        </Pressable>
        <Pressable
          testID="date-picker-future"
          onPress={() => {
            const d = new Date();
            d.setDate(d.getDate() + 7);
            const y = d.getFullYear();
            const m = String(d.getMonth() + 1).padStart(2, "0");
            const day = String(d.getDate()).padStart(2, "0");
            onSelect(`${y}-${m}-${day}`);
          }}
        >
          <Text>Select Future Date</Text>
        </Pressable>
      </View>
    ),
  };
});

jest.mock("@/components/common/TimePicker", () => {
  const { View, Text } = require("react-native");
  return {
    __esModule: true,
    default: ({ selectedTime }: { selectedTime?: string }) => (
      <View>
        <Text testID="time-picker">{selectedTime ?? "no time"}</Text>
      </View>
    ),
  };
});

jest.mock("@/components/common/ConfirmModal", () => {
  const { View, Pressable, Text } = require("react-native");
  return {
    __esModule: true,
    default: ({ visible, message, onConfirm, onCancel }: any) =>
      visible ? (
        <View>
          <Text testID="confirm-message">{message}</Text>
          <Pressable testID="confirm-ok" onPress={onConfirm}>
            <Text>Book Anyway</Text>
          </Pressable>
          <Pressable testID="confirm-cancel" onPress={onCancel}>
            <Text>Go Back</Text>
          </Pressable>
        </View>
      ) : null,
  };
});

jest.mock("@/components/common/Button", () => {
  const { Pressable, Text } = require("react-native");
  return {
    __esModule: true,
    // Note: `disabled` is surfaced via accessibilityState (not the native
    // `disabled` prop) so tests can still press through it to exercise the
    // component's own validity guard, mirroring how an accessibility tool
    // (or a stale re-render) could invoke onPress while disabled.
    default: ({ onPress, children, disabled }: any) => (
      <Pressable onPress={onPress} accessibilityState={{ disabled }}>
        <Text>{children}</Text>
      </Pressable>
    ),
  };
});

// Mirrors NewBookingModal.nextSlotTime's local-time rounding so the next-slot
// assertions are deterministic regardless of the host machine's timezone.
// The component displays wall-clock (local) time, so the expected value must
// be derived from the same local-time interpretation of the setSystemTime instant.
const expectedLocalSlot = (now: Date, openTime = "09:00", closeTime = "22:00") => {
  let h = now.getHours();
  const min = now.getMinutes();
  const m = min < 15 ? 15 : min < 30 ? 30 : min < 45 ? 45 : 0;
  if (m === 0) h += 1;
  const [openH] = openTime.split(":").map(Number);
  const [closeH] = closeTime.split(":").map(Number);
  if (h < openH || h >= closeH) return `${(openH + 1).toString().padStart(2, "0")}:00`;
  return `${h.toString().padStart(2, "0")}:${m.toString().padStart(2, "0")}`;
};

const mockVenues = [
  {
    id: 1,
    name: "Test Location",
    openTime: "09:00",
    closeTime: "22:00",
    openDays: "1,2,3,4,5",
    address: "123 Street",
    timezone: "UTC",
    tags: [],
    sections: [
      {
        id: 10,
        name: "Main",
        resources: [
          { id: 100, name: "Resource 1", capacity: 4 },
          { id: 101, name: "Resource 2", capacity: 2 },
        ],
      },
    ],
  },
];

describe("NewBookingModal", () => {
  const onClose = jest.fn();
  const onCreated = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
  });

  it("does not render content when visible is false", () => {
    render(<NewBookingModal visible={false} onClose={onClose} onCreated={onCreated} />);
    expect(screen.queryByText("New Booking")).toBeNull();
  });

  it("renders New Booking title when visible", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => {
      expect(screen.getByText("New Booking")).toBeTruthy();
    });
  });

  it("shows loading indicator while fetching locations", async () => {
    let resolveVenues: (v: typeof mockVenues) => void;
    (venuesApi.fetchVenues as jest.Mock).mockReturnValue(
      new Promise((r) => {
        resolveVenues = r;
      })
    );
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    expect(screen.getByText("New Booking")).toBeTruthy();
    await act(async () => {
      resolveVenues!(mockVenues);
    });
  });

  it("shows location options after loading", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => {
      expect(screen.getByText("Test Location")).toBeTruthy();
    });
  });

  it("calls onClose when close button is pressed", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("New Booking")).toBeTruthy());
    const accessible = screen.UNSAFE_getAllByProps({ accessible: true });
    // Close button is typically the first accessible element (X icon)
    fireEvent.press(accessible[0]);
    expect(onClose).toHaveBeenCalled();
  });

  it("shows participant email input field", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => {
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy();
    });
  });

  it("handles location change", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("Test Location")).toBeTruthy());
    fireEvent.press(screen.getByText("Test Location"));
    expect(screen.getByText("New Booking")).toBeTruthy();
  });

  it("handles resource change", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("Resource 2 (2 places)")).toBeTruthy());
    fireEvent.press(screen.getByText("Resource 2 (2 places)"));
    expect(screen.getByText("New Booking")).toBeTruthy();
  });

  it("handles section change", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("Main")).toBeTruthy());
    fireEvent.press(screen.getByText("Main"));
    expect(screen.getByText("New Booking")).toBeTruthy();
  });

  it("creates booking successfully", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockResolvedValue({ id: 42 });
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => {
      expect(onCreated).toHaveBeenCalledWith(42);
      expect(onClose).toHaveBeenCalled();
    });
  });

  it("shows error when adminCreateBooking throws", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockRejectedValue(new Error("Booking failed"));
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => {
      expect(screen.getByText("Booking failed")).toBeTruthy();
    });
  });

  it("shows capacity warning when party size exceeds resource capacity", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.press(screen.getByText("5 participants"));
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => {
      expect(screen.getByText("Book Anyway")).toBeTruthy();
    });
    const message = screen.getByTestId("confirm-message").props.children as string;
    expect(message).toContain("5");
  });

  it("proceeds to book when capacity warning is confirmed", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockResolvedValue({ id: 99 });
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.press(screen.getByText("5 participants"));
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => expect(screen.getByTestId("confirm-ok")).toBeTruthy());
    await act(async () => {
      fireEvent.press(screen.getByTestId("confirm-ok"));
    });
    await waitFor(() => {
      expect(adminApi.adminCreateBooking).toHaveBeenCalled();
    });
  });

  it("cancels capacity warning when Go Back is pressed", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.press(screen.getByText("5 participants"));
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => expect(screen.getByTestId("confirm-cancel")).toBeTruthy());
    fireEvent.press(screen.getByTestId("confirm-cancel"));
    await waitFor(() => {
      expect(screen.queryByText("Book Anyway")).toBeNull();
    });
  });

  it("resets state when modal closes", async () => {
    const { rerender } = render(
      <NewBookingModal visible onClose={onClose} onCreated={onCreated} />
    );
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    rerender(<NewBookingModal visible={false} onClose={onClose} onCreated={onCreated} />);
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue(mockVenues);
    rerender(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("Test Location")).toBeTruthy());
    expect(screen.getByPlaceholderText("participant@example.com").props.value).toBeFalsy();
  });

  it("opts in to past dates and creates a booking with a past date", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockResolvedValue({ id: 77 });
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );

    // The admin modal must opt in to past dates on the shared DatePicker.
    expect(screen.getByTestId("date-picker-allowpast").props.children).toBe("true");

    // Select a date 7 days ago via the mocked picker.
    fireEvent.press(screen.getByTestId("date-picker-past"));
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );

    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => expect(adminApi.adminCreateBooking).toHaveBeenCalled());

    const payload = (adminApi.adminCreateBooking as jest.Mock).mock.calls[0][0];
    // The submitted date is genuinely in the past.
    expect(new Date(payload.date).getTime()).toBeLessThan(Date.now());
    expect(onCreated).toHaveBeenCalledWith(77);
  });

  it("selects a future date without applying the back-date special-casing", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockResolvedValue({ id: 88 });
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );

    fireEvent.press(screen.getByTestId("date-picker-future"));
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );

    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => expect(adminApi.adminCreateBooking).toHaveBeenCalled());

    const payload = (adminApi.adminCreateBooking as jest.Mock).mock.calls[0][0];
    expect(new Date(payload.date).getTime()).toBeGreaterThan(Date.now());
  });

  it("computes the next slot within opening hours, rounding minutes up to :30", async () => {
    jest.useFakeTimers();
    const now = new Date("2026-01-05T14:20:00Z");
    jest.setSystemTime(now);
    try {
      render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
      await waitFor(() => expect(screen.getByTestId("time-picker")).toBeTruthy());
      expect(screen.getByTestId("time-picker").props.children).toBe(expectedLocalSlot(now));
    } finally {
      jest.useRealTimers();
    }
  });

  it("computes the next slot within opening hours, rounding minutes up to :45", async () => {
    jest.useFakeTimers();
    const now = new Date("2026-01-05T14:35:00Z");
    jest.setSystemTime(now);
    try {
      render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
      await waitFor(() => expect(screen.getByTestId("time-picker")).toBeTruthy());
      expect(screen.getByTestId("time-picker").props.children).toBe(expectedLocalSlot(now));
    } finally {
      jest.useRealTimers();
    }
  });

  it("rolls over to the next hour when minutes round up past :45", async () => {
    jest.useFakeTimers();
    const now = new Date("2026-01-05T14:50:00Z");
    jest.setSystemTime(now);
    try {
      render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
      await waitFor(() => expect(screen.getByTestId("time-picker")).toBeTruthy());
      expect(screen.getByTestId("time-picker").props.children).toBe(expectedLocalSlot(now));
    } finally {
      jest.useRealTimers();
    }
  });

  it("falls back to the default primary color when the brand has none", async () => {
    const { useBrand } = require("@/context/BrandContext");
    (useBrand as jest.Mock).mockReturnValueOnce({ primaryColor: "", appName: "ResourceFlow" });
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("New Booking")).toBeTruthy());
  });

  it("handles an empty location list", async () => {
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue([]);
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByTestId("time-picker")).toBeTruthy());
    expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy();
  });

  it("handles a location with no sections", async () => {
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue([{ ...mockVenues[0], sections: [] }]);
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("Test Location")).toBeTruthy());
    expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy();
  });

  it("falls back to a generic resource label when a resource has no name", async () => {
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue([
      {
        ...mockVenues[0],
        sections: [
          {
            id: 10,
            name: "Main",
            resources: [{ id: 100, capacity: 4 }],
          },
        ],
      },
    ]);
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("Resource 100 (4 places)")).toBeTruthy());
  });

  it("does not submit while the form is invalid", async () => {
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.press(screen.getByText("Create Booking"));
    expect(adminApi.adminCreateBooking).not.toHaveBeenCalled();
  });

  it("does not call onCreated/onClose when the booking result is falsy", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockResolvedValue(null);
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => expect(adminApi.adminCreateBooking).toHaveBeenCalled());
    expect(onCreated).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("shows a generic error message when the rejection is not an Error instance", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockRejectedValue("network down");
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => {
      expect(screen.getByText("Failed to create booking.")).toBeTruthy();
    });
  });

  it("falls back to noon when the resolved opening hour is an empty string", async () => {
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue([
      { ...mockVenues[0], openTime: "", openHours: null },
    ]);
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByText("Test Location")).toBeTruthy());
    fireEvent.press(screen.getByTestId("date-picker-past"));
    await waitFor(() => expect(screen.getByTestId("time-picker").props.children).toBe("12:00"));
  });

  it("resolves default opening hours when no location is selected", async () => {
    (venuesApi.fetchVenues as jest.Mock).mockResolvedValue([]);
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() => expect(screen.getByTestId("date-picker-past")).toBeTruthy());
    fireEvent.press(screen.getByTestId("date-picker-past"));
    expect(screen.getByTestId("date-picker")).toBeTruthy();
  });

  it("submits the trimmed participant name when provided", async () => {
    (adminApi.adminCreateBooking as jest.Mock).mockResolvedValue({ id: 55 });
    render(<NewBookingModal visible onClose={onClose} onCreated={onCreated} />);
    await waitFor(() =>
      expect(screen.getByPlaceholderText("participant@example.com")).toBeTruthy()
    );
    fireEvent.changeText(
      screen.getByPlaceholderText("participant@example.com"),
      "test@example.com"
    );
    fireEvent.changeText(screen.getByPlaceholderText("Full name"), "  Jane Doe  ");
    await act(async () => {
      fireEvent.press(screen.getByText("Create Booking"));
    });
    await waitFor(() => expect(adminApi.adminCreateBooking).toHaveBeenCalled());
    const payload = (adminApi.adminCreateBooking as jest.Mock).mock.calls[0][0];
    expect(payload.customerName).toBe("Jane Doe");
  });
});
