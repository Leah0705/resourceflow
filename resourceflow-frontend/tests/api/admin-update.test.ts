import { adminUpdateBookingFull, AdminUpdateBookingRequest } from "../../api/admin";

// Mock fetch for testing
global.fetch = jest.fn();

describe("adminUpdateBookingFull", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  const mockBooking = {
    id: 123,
    venueId: 1,
    venueName: "Test Location",
    sectionId: 1,
    sectionName: "Main",
    resourceId: 1,
    resourceName: "Resource 1",
    date: "2026-03-29T19:00:00Z",
    customerEmail: "participant@example.com",
    partySize: 2,
  };

  it("should successfully update a booking with full details", async () => {
    // Arrange
    const updateReq: AdminUpdateBookingRequest = {
      venueId: 2,
      sectionId: 3,
      resourceId: 4,
      date: "2026-03-30T20:00:00Z",
      partySize: 4,
      customerEmail: "updated@example.com",
      specialRequests: "Projector needed please",
    };

    (fetch as jest.Mock).mockResolvedValueOnce({
      ok: true,
      json: jest.fn().mockResolvedValue({ ...mockBooking, ...updateReq }),
    });

    // Act
    const result = await adminUpdateBookingFull(123, updateReq);

    // Assert
    expect(fetch).toHaveBeenCalledWith("/api/admin/bookings/123", {
      method: "PUT",
      credentials: "include",
      // Jest's platform is ios, so the native client-identity header rides along too.
      headers: expect.objectContaining({ "Content-Type": "application/json" }),
      body: JSON.stringify(updateReq),
    });
    expect(result?.venueId).toBe(2);
    expect(result?.sectionId).toBe(3);
    expect(result?.resourceId).toBe(4);
    expect(result?.partySize).toBe(4);
    expect(result?.customerEmail).toBe("updated@example.com");
  });

  it("should throw error when update fails", async () => {
    // Arrange
    (fetch as jest.Mock).mockResolvedValueOnce({
      ok: false,
      json: jest.fn().mockResolvedValue({ message: "Resource already booked" }),
    });

    const updateReq: AdminUpdateBookingRequest = { resourceId: 99 };

    // Act & Assert
    await expect(adminUpdateBookingFull(123, updateReq)).rejects.toThrow("Resource already booked");
  });

  it("should handle network errors during update", async () => {
    // Arrange
    (fetch as jest.Mock).mockRejectedValueOnce(new Error("Network failure"));

    // Act & Assert
    await expect(adminUpdateBookingFull(123, {})).rejects.toThrow("Network failure");
  });
});
