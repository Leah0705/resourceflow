import { adminGetVenues, adminGetSections } from "../../api/admin";

// Mock fetch for testing
global.fetch = jest.fn();

describe("Admin lookup APIs", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("adminGetVenues", () => {
    it("should return locations on success", async () => {
      const mockData = [
        { id: 1, name: "Location A" },
        { id: 2, name: "Location B" },
      ];
      (fetch as jest.Mock).mockResolvedValueOnce({
        ok: true,
        json: jest.fn().mockResolvedValue(mockData),
      });

      const result = await adminGetVenues();

      expect(fetch).toHaveBeenCalledWith("/api/admin/venues", expect.any(Object));
      expect(result).toEqual(mockData);
    });

    it("should return empty array on failure", async () => {
      (fetch as jest.Mock).mockResolvedValueOnce({ ok: false });
      const result = await adminGetVenues();
      expect(result).toEqual([]);
    });
  });

  describe("adminGetSections", () => {
    it("should return sections for a location", async () => {
      const mockData = [{ id: 10, name: "Main" }];
      (fetch as jest.Mock).mockResolvedValueOnce({
        ok: true,
        json: jest.fn().mockResolvedValue(mockData),
      });

      const result = await adminGetSections(1);

      expect(fetch).toHaveBeenCalledWith("/api/admin/venues/1/sections", expect.any(Object));
      expect(result).toEqual(mockData);
    });

    it("should return empty array on failure", async () => {
      (fetch as jest.Mock).mockResolvedValueOnce({ ok: false });
      const result = await adminGetSections(1);
      expect(result).toEqual([]);
    });
  });
});
