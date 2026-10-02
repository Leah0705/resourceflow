import i18n from "@/i18n";
import { describeBookingRow } from "@/components/admin/bookings/bookingRowProps";

const t = i18n.getFixedT("en");

describe("describeBookingRow", () => {
  const base = {
    customerName: "Alice",
    customerEmail: "alice@example.com",
    date: "2026-10-10T18:00:00Z",
    partySize: 2,
  };

  it("includes the resource name when the booking has one", () => {
    const label = describeBookingRow({ ...base, resourceName: "T4" }, t);
    expect(label).toContain("T4");
    expect(label).toContain("Alice");
    expect(label).toContain("2 participants");
  });

  it("omits the resource clause when the booking has no resource (unassigned)", () => {
    const label = describeBookingRow({ ...base, resourceName: null }, t);
    expect(label).not.toContain("null");
    expect(label.endsWith("2 participants")).toBe(true);
  });

  it("falls back to the customer email when no name is set", () => {
    const label = describeBookingRow({ ...base, customerName: null, resourceName: undefined }, t);
    expect(label).toContain("alice@example.com");
  });
});
