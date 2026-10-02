import {
  fetchSocialLinks,
  createResourceGroup,
  updateResourceGroup,
  deleteResourceGroup,
  fetchResourceDeleteImpact,
  fetchSectionDeleteImpact,
  fetchScheduleConflicts,
} from "@/api/venues";

const mockFetch = jest.fn();
global.fetch = mockFetch;

beforeEach(() => {
  mockFetch.mockReset();
  // mockClear as well as re-spying: jest.spyOn returns the existing mock on the second call, so
  // without this the call history accumulates across tests and the "does not log" assertion below
  // sees every earlier suite's errors.
  jest.spyOn(console, "error").mockImplementation().mockClear();
});

// ---------- Social links ----------

describe("fetchSocialLinks", () => {
  it("returns the links when the response is ok", async () => {
    const links = [
      {
        id: 1,
        label: "Instagram",
        url: "https://ig.test",
        iconKey: "logo-instagram",
        sortOrder: 0,
      },
    ];
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => links });

    expect(await fetchSocialLinks()).toEqual(links);
    expect(mockFetch.mock.calls[0][0]).toContain("/api/social-links");
  });

  it("returns an empty list on a non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await fetchSocialLinks()).toEqual([]);
  });

  it("returns an empty list on a network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await fetchSocialLinks()).toEqual([]);
  });
});

// ---------- Combinable resource groups -----------------

const group = {
  id: 3,
  name: "Window desks",
  combinedCapacity: 8,
  members: [
    { id: 1, name: "T1", capacity: 4 },
    { id: 2, name: "T2", capacity: 4 },
  ],
};

describe("createResourceGroup", () => {
  it("posts the group and returns it", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => group });

    const result = await createResourceGroup(1, {
      name: "Window desks",
      members: [1, 2],
      combinedCapacity: 8,
    });

    expect(result).toEqual(group);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/groups");
    expect(opts.method).toBe("POST");
    expect(opts.credentials).toBe("include");
    expect(JSON.parse(opts.body)).toEqual({
      name: "Window desks",
      members: [1, 2],
      combinedCapacity: 8,
    });
  });

  it("returns null on a non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await createResourceGroup(1, { members: [1, 2], combinedCapacity: 8 })).toBeNull();
  });

  it("returns null on a network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await createResourceGroup(1, { members: [1, 2], combinedCapacity: 8 })).toBeNull();
  });
});

describe("updateResourceGroup", () => {
  it("puts to the group endpoint and returns the updated group", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => group });

    const result = await updateResourceGroup(1, 3, {
      name: "Renamed",
      members: [1, 2],
      combinedCapacity: 7,
    });

    expect(result).toEqual(group);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/groups/3");
    expect(opts.method).toBe("PUT");
    expect(JSON.parse(opts.body)).toEqual({
      name: "Renamed",
      members: [1, 2],
      combinedCapacity: 7,
    });
  });

  it("returns null on a non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await updateResourceGroup(1, 3, { members: [1, 2], combinedCapacity: 7 })).toBeNull();
  });

  it("returns null on a network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await updateResourceGroup(1, 3, { members: [1, 2], combinedCapacity: 7 })).toBeNull();
  });
});

describe("deleteResourceGroup", () => {
  it("sends DELETE and returns true on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });

    expect(await deleteResourceGroup(1, 3)).toBe(true);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/groups/3");
    expect(opts.method).toBe("DELETE");
    expect(opts.credentials).toBe("include");
  });

  it("returns false on a non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await deleteResourceGroup(1, 3)).toBe(false);
  });

  it("returns false on a network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await deleteResourceGroup(1, 3)).toBe(false);
  });
});

// ---------- Delete-impact previews -----------------
//
// The count is best-effort friction for the two-step delete UI, never a gate — every failure
// mode must resolve to null so the UI degrades to generic copy instead of blocking the delete.

describe("fetchResourceDeleteImpact", () => {
  it("returns the impact when the response is ok", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ bookings: 4 }) });

    expect(await fetchResourceDeleteImpact(1, 2, 3)).toEqual({ bookings: 4 });
    expect(mockFetch.mock.calls[0][0]).toContain("/api/venues/1/sections/2/resources/3/impact");
  });

  it("returns null on a non-ok response without logging", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await fetchResourceDeleteImpact(1, 2, 3)).toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it("returns null on a network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await fetchResourceDeleteImpact(1, 2, 3)).toBeNull();
    expect(console.error).toHaveBeenCalled();
  });
});

describe("fetchSectionDeleteImpact", () => {
  it("returns the impact when the response is ok", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ bookings: 2 }) });

    expect(await fetchSectionDeleteImpact(1, 2)).toEqual({ bookings: 2 });
    expect(mockFetch.mock.calls[0][0]).toContain("/api/venues/1/sections/2/impact");
  });

  it("returns null on a non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await fetchSectionDeleteImpact(1, 2)).toBeNull();
  });

  it("returns null on a network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await fetchSectionDeleteImpact(1, 2)).toBeNull();
  });
});

describe("fetchScheduleConflicts", () => {
  const conflict = {
    bookingId: 7,
    bookingRef: "swift-cedar-harbor",
    customerName: "Ada",
    date: "2026-09-02T10:00:00Z",
    partySize: 2,
    reason: "outsideHours",
  };

  it("returns the conflicts when the response is ok", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => [conflict] });

    expect(await fetchScheduleConflicts(1)).toEqual([conflict]);
    expect(mockFetch.mock.calls[0][0]).toContain("/api/venues/1/schedule-conflicts");
  });

  // Null, not [] — the caller renders nothing for null and would otherwise announce all-clear.
  it("returns null on a non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await fetchScheduleConflicts(1)).toBeNull();
  });

  it("returns null on a network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await fetchScheduleConflicts(1)).toBeNull();
    expect(console.error).toHaveBeenCalled();
  });
});
