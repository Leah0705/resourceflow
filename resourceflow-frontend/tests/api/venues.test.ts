import {
  createVenue,
  fetchVenues,
  fetchVenueById,
  fetchHighlights,
  updateVenue,
  addSection,
  updateSection,
  deleteSection,
  addResource,
  updateResource,
  deleteResource,
  uploadLocationImage,
  deleteLocationImage,
  uploadGuideFile,
  deleteGuideFile,
} from "@/api/venues";

// Venues API now uses credentials: "include" for cookie-based auth

const mockFetch = jest.fn();
global.fetch = mockFetch;

beforeEach(() => {
  mockFetch.mockReset();
  jest.spyOn(console, "error").mockImplementation();
});

// ---------- Fetch venues ----------

describe("fetchVenues", () => {
  it("fetches GET /api/venues and returns array", async () => {
    const venues = [{ id: 1, name: "Central Workspace", sections: [] }];
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => venues });

    const result = await fetchVenues();

    expect(result).toEqual(venues);
    expect(mockFetch.mock.calls[0][0]).toContain("/api/venues");
  });

  it("returns empty array on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await fetchVenues()).toEqual([]);
  });

  it("returns empty array on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await fetchVenues()).toEqual([]);
  });
});

describe("fetchVenueById", () => {
  it("fetches a single location by id", async () => {
    const venue = { id: 5, name: "Harbour Studio", sections: [] };
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => venue });

    const result = await fetchVenueById(5);

    expect(result).toEqual(venue);
    expect(mockFetch.mock.calls[0][0]).toContain("/api/venues/5");
  });

  it("returns null on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await fetchVenueById(999)).toBeNull();
  });

  it("returns null on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await fetchVenueById(1)).toBeNull();
  });
});

// ---------- Update venue ----------

describe("updateVenue", () => {
  const data = { name: "Updated Workspace", address: "123 Main St" };

  it("puts to /api/venues/:id with auth headers", async () => {
    const updated = { id: 1, ...data, sections: [] };
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => updated });

    const result = await updateVenue(1, data);

    expect(result).toEqual(updated);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1");
    expect(opts.method).toBe("PUT");
    expect(opts.credentials).toBe("include");
    expect(JSON.parse(opts.body)).toEqual(data);
  });

  it("returns null on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await updateVenue(1, data)).toBeNull();
  });
});

// ---------- Section management ----------

describe("addSection", () => {
  it("posts new section with auth headers", async () => {
    const section = { id: 10, name: "Annex", resources: [] };
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => section });

    const result = await addSection(1, "Annex");

    expect(result).toEqual(section);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/sections");
    expect(opts.method).toBe("POST");
    expect(opts.credentials).toBe("include");
    expect(JSON.parse(opts.body)).toEqual({ name: "Annex" });
  });

  it("returns null on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await addSection(1, "Fail")).toBeNull();
  });
});

describe("updateSection", () => {
  it("puts to section endpoint with auth", async () => {
    const section = { id: 10, name: "Studio", resources: [] };
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => section });

    const result = await updateSection(1, 10, "Studio");

    expect(result).toEqual(section);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/sections/10");
    expect(opts.method).toBe("PUT");
    expect(JSON.parse(opts.body)).toEqual({ name: "Studio" });
  });

  it("returns null on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await updateSection(1, 10, "Fail")).toBeNull();
  });
});

describe("deleteSection", () => {
  it("sends DELETE and returns true on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });

    const result = await deleteSection(1, 10);

    expect(result).toBe(true);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/sections/10");
    expect(opts.method).toBe("DELETE");
    expect(opts.credentials).toBe("include");
  });

  it("returns false on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await deleteSection(1, 10)).toBe(false);
  });

  it("returns false on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await deleteSection(1, 10)).toBe(false);
  });
});

// ---------- Resource management ----------

describe("addResource", () => {
  it("posts new resource with auth headers", async () => {
    const resource = { id: 20, name: "T1", capacity: 4 };
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => resource });

    const result = await addResource(1, 10, { name: "T1", capacity: 4 });

    expect(result).toEqual(resource);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/sections/10/resources");
    expect(opts.method).toBe("POST");
    expect(opts.credentials).toBe("include");
  });

  it("returns null on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await addResource(1, 10, { capacity: 2 })).toBeNull();
  });
});

describe("updateResource", () => {
  it("puts to resource endpoint with auth", async () => {
    const resource = { id: 20, name: "T1-updated", capacity: 6 };
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => resource });

    const result = await updateResource(1, 10, 20, { name: "T1-updated", capacity: 6 });

    expect(result).toEqual(resource);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/sections/10/resources/20");
    expect(opts.method).toBe("PUT");
  });

  it("returns null on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await updateResource(1, 10, 20, { capacity: 2 })).toBeNull();
  });

  it("returns null on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await updateResource(1, 10, 20, { capacity: 2 })).toBeNull();
  });
});

describe("deleteResource", () => {
  it("sends DELETE and returns true on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });

    const result = await deleteResource(1, 10, 20);

    expect(result).toBe(true);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues/1/sections/10/resources/20");
    expect(opts.method).toBe("DELETE");
  });

  it("returns false on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await deleteResource(1, 10, 20)).toBe(false);
  });

  it("returns false on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await deleteResource(1, 10, 20)).toBe(false);
  });
});

// ---------- createVenue ----------

describe("createVenue", () => {
  it("posts to /api/venues and returns created venue", async () => {
    const created = { id: 10, name: "New Place", sections: [] };
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => created });

    const result = await createVenue("New Place");

    expect(result).toEqual(created);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/venues");
    expect(opts.method).toBe("POST");
    expect(JSON.parse(opts.body)).toMatchObject({ name: "New Place" });
  });

  it("returns null on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await createVenue("Fail")).toBeNull();
  });

  it("returns null on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await createVenue("Fail")).toBeNull();
  });
});

// ---------- fetchHighlights ----------

describe("fetchHighlights", () => {
  it("fetches GET /api/highlights and returns array", async () => {
    const highlights = [
      { id: 1, title: "Quiet Rooms", body: "Focused", iconKey: "star-outline", sortOrder: 0 },
    ];
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => highlights });

    const result = await fetchHighlights();

    expect(result).toEqual(highlights);
    expect(mockFetch.mock.calls[0][0]).toContain("/api/highlights");
  });

  it("returns empty array on failure", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await fetchHighlights()).toEqual([]);
  });

  it("returns empty array on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await fetchHighlights()).toEqual([]);
  });
});

// ---------- uploadLocationImage ----------

describe("uploadLocationImage", () => {
  it("posts form data and returns url on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ url: "/uploads/img.png" }) });

    const file = new File(["content"], "img.png", { type: "image/png" });
    const result = await uploadLocationImage(1, file);

    expect(result).toBe("/uploads/img.png");
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/media/location/1");
    expect(opts.method).toBe("POST");
    expect(opts.credentials).toBe("include");
  });

  it("returns null when response is not ok", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    const file = new File(["x"], "x.png");
    expect(await uploadLocationImage(1, file)).toBeNull();
  });

  it("returns null when url missing from response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({}) });
    const file = new File(["x"], "x.png");
    expect(await uploadLocationImage(1, file)).toBeNull();
  });

  it("returns null on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    const file = new File(["x"], "x.png");
    expect(await uploadLocationImage(1, file)).toBeNull();
  });
});

// ---------- deleteLocationImage ----------

describe("deleteLocationImage", () => {
  it("sends DELETE to media endpoint and returns true on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });

    const result = await deleteLocationImage(1);

    expect(result).toBe(true);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/media/location/1");
    expect(opts.method).toBe("DELETE");
    expect(opts.credentials).toBe("include");
  });

  it("returns false on non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await deleteLocationImage(1)).toBe(false);
  });

  it("returns false on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await deleteLocationImage(1)).toBe(false);
  });
});

// ---------- uploadGuideFile ----------

describe("uploadGuideFile", () => {
  it("posts form data and returns url on success", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ url: "/media/guide-1.pdf?v=1" }),
    });

    const file = new File(["content"], "guide.pdf", { type: "application/pdf" });
    const result = await uploadGuideFile(1, file);

    expect(result).toBe("/media/guide-1.pdf?v=1");
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/media/guide/1");
    expect(opts.method).toBe("POST");
    expect(opts.credentials).toBe("include");
    expect(opts.body).toBeInstanceOf(FormData);
  });

  it("returns null when response is not ok", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    const file = new File(["x"], "guide.pdf", { type: "application/pdf" });
    expect(await uploadGuideFile(1, file)).toBeNull();
  });

  it("returns null when url missing from response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({}) });
    const file = new File(["x"], "guide.pdf", { type: "application/pdf" });
    expect(await uploadGuideFile(1, file)).toBeNull();
  });

  it("returns null on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    const file = new File(["x"], "guide.pdf", { type: "application/pdf" });
    expect(await uploadGuideFile(1, file)).toBeNull();
  });
});

// ---------- deleteGuideFile ----------

describe("deleteGuideFile", () => {
  it("sends DELETE to guide media endpoint and returns true on success", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });

    const result = await deleteGuideFile(1);

    expect(result).toBe(true);
    const [url, opts] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/media/guide/1");
    expect(opts.method).toBe("DELETE");
    expect(opts.credentials).toBe("include");
  });

  it("returns false on non-ok response", async () => {
    mockFetch.mockResolvedValueOnce({ ok: false });
    expect(await deleteGuideFile(1)).toBe(false);
  });

  it("returns false on network error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("offline"));
    expect(await deleteGuideFile(1)).toBe(false);
  });
});
