import {
  createBooking,
  getBookingById,
  getBookingByRef,
  getBookingsByVenue,
  deleteBooking,
  cancelBookingByRef,
} from "@/api/bookings";

// Mock fetch globally
const mockFetch = jest.fn();
global.fetch = mockFetch;

beforeEach(() => {
  mockFetch.mockReset();
});

describe("createBooking", () => {
  const validBooking = {
    venueId: 1,
    resourceId: 2,
    sectionId: 1,
    customerEmail: "test@example.com",
    customerName: "Test User",
    partySize: 4,
    date: "2026-06-15T19:00:00Z",
  };

  it("posts to /api/bookings and returns the created booking", async () => {
    const created = { ...validBooking, id: 42, isHeld: false, bookingRef: "swift-cedar" };
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => created,
    });

    const result = await createBooking(validBooking);

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/bookings");
    expect(opts.method).toBe("POST");
    expect(JSON.parse(opts.body)).toEqual(validBooking);
    // toMatchObject (not toEqual) because normalizeBooking fills optional keys (resourceGroupId,
    // endTime, isCancelled) that the minimal mock doesn't carry.
    expect(result).toMatchObject(created);
  });

  it("throws with server message on 409 conflict", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 409,
      json: async () => ({ message: "Resource already booked" }),
    });

    await expect(createBooking(validBooking)).rejects.toThrow("Resource already booked");
  });

  it("throws generic message on 409 without body", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 409,
      json: async () => {
        throw new Error("no json");
      },
    });

    await expect(createBooking(validBooking)).rejects.toThrow(
      "This resource is no longer available."
    );
  });

  it("throws on non-ok non-409 response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500 });

    await expect(createBooking(validBooking)).rejects.toThrow("Failed to create booking");
  });
});

describe("getBookingById", () => {
  it("fetches and returns booking by id", async () => {
    const booking = { id: 5, customerEmail: "a@b.com", partySize: 2 };
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => booking,
    });

    const result = await getBookingById(5);
    expect(result).toMatchObject(booking);
    expect(mockFetch.mock.calls[0][0]).toContain("/api/bookings/5");
  });

  it("returns null on error", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });

    const result = await getBookingById(999);
    expect(result).toBeNull();
  });

  it("returns null on network failure", async () => {
    mockFetch.mockRejectedValueOnce(new Error("Network error"));

    const result = await getBookingById(1);
    expect(result).toBeNull();
  });
});

describe("getBookingByRef", () => {
  it("fetches by ref and email", async () => {
    const booking = { id: 3, bookingRef: "sunny-cedar", customerEmail: "u@x.com" };
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => booking,
    });

    const result = await getBookingByRef("sunny-cedar", "u@x.com");
    expect(result).toMatchObject(booking);
    const url = mockFetch.mock.calls[0][0] as string;
    expect(url).toContain("/api/bookings/ref/sunny-cedar");
    expect(url).toContain("email=u%40x.com");
  });

  it("returns null on a 404 (no such booking, or a ref/email mismatch)", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 404 });
    expect(await getBookingByRef("no-exist", "a@b.com")).toBeNull();
  });

  it("throws on a non-404 failure instead of also returning null", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500 });
    await expect(getBookingByRef("ref", "a@b.com")).rejects.toThrow("Failed to fetch booking");
  });

  it("throws on network error rather than swallowing it into a false not-found", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    await expect(getBookingByRef("ref", "a@b.com")).rejects.toThrow("offline");
  });
});

describe("getBookingsByVenue", () => {
  it("fetches bookings for a location", async () => {
    const bookings = [{ id: 1 }, { id: 2 }];
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => bookings });
    const result = await getBookingsByVenue(5);
    expect(result[0]).toMatchObject({ id: 1 });
    expect(result[1]).toMatchObject({ id: 2 });
    expect(mockFetch.mock.calls[0][0]).toContain("/api/venues/5/bookings");
  });

  it("returns empty array on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await getBookingsByVenue(5)).toEqual([]);
  });

  it("returns empty array on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await getBookingsByVenue(5)).toEqual([]);
  });
});

describe("deleteBooking", () => {
  it("sends DELETE and returns true on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });

    const result = await deleteBooking(10);
    expect(result).toBe(true);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/bookings/10");
    expect(opts.method).toBe("DELETE");
  });

  it("returns false on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await deleteBooking(10)).toBe(false);
  });

  it("returns false on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await deleteBooking(10)).toBe(false);
  });
});

describe("cancelBookingByRef", () => {
  it("posts to cancel endpoint and returns true on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });

    const result = await cancelBookingByRef("ref-abc", "user@test.com");

    expect(result).toBe(true);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/bookings/ref/ref-abc/cancel");
    expect(opts.method).toBe("POST");
    expect(JSON.parse(opts.body)).toEqual({ email: "user@test.com" });
  });

  it("throws with server message on failure", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      json: async () => ({ message: "Cannot cancel a booking that has already passed." }),
    });

    await expect(cancelBookingByRef("ref-abc", "u@t.com")).rejects.toThrow(
      "Cannot cancel a booking that has already passed."
    );
  });

  it("throws generic message on failure without body", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      json: async () => {
        throw new Error("no json");
      },
    });

    await expect(cancelBookingByRef("ref-abc", "u@t.com")).rejects.toThrow(
      "Failed to cancel booking."
    );
  });

  it("throws on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    await expect(cancelBookingByRef("ref-abc", "u@t.com")).rejects.toThrow("offline");
  });
});

describe("normalizeBooking (PascalCase fields)", () => {
  it("normalizes PascalCase response to camelCase", async () => {
    const pascalBooking = {
      Id: 99,
      ResourceId: 3,
      SectionId: 4,
      ResourceGroupId: null,
      VenueId: 5,
      Date: "2026-06-15T19:00:00Z",
      EndTime: "2026-06-15T20:30:00Z",
      CustomerEmail: "pascal@test.com",
      CustomerName: "Pascal User",
      PartySize: 3,
      IsHeld: true,
      SpecialRequests: "projector needed",
      BookingRef: "sunny-maple",
      ResourceName: "T3",
      SectionName: "Annex",
      ResourceCapacity: 4,
      IsCancelled: false,
    };
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => pascalBooking,
    });

    const result = await getBookingById(99);

    expect(result).toMatchObject({
      id: 99,
      resourceId: 3,
      sectionId: 4,
      resourceGroupId: null,
      venueId: 5,
      endTime: "2026-06-15T20:30:00Z",
      customerEmail: "pascal@test.com",
      customerName: "Pascal User",
      partySize: 3,
      isHeld: true,
      bookingRef: "sunny-maple",
      resourceName: "T3",
      sectionName: "Annex",
    });
  });

  it("normalizes a group booking (ResourceGroupId set, ResourceId null)", async () => {
    const groupBooking = {
      Id: 50,
      ResourceId: null,
      SectionId: 1,
      ResourceGroupId: 7,
      VenueId: 5,
      Date: "2026-06-15T19:00:00Z",
      CustomerEmail: "group@test.com",
      PartySize: 6,
      IsHeld: false,
      BookingRef: "merged-bay",
      ResourceName: "Resources T2 + T3",
      ResourceCapacity: 8,
    };
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => groupBooking,
    });

    const result = await getBookingById(50);

    expect(result).toMatchObject({
      id: 50,
      resourceId: null,
      resourceGroupId: 7,
      resourceName: "Resources T2 + T3",
      resourceCapacity: 8,
    });
  });
});
