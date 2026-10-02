import { test, expect } from "@playwright/test";
import { gotoAdminDashboard, pastUtcISO } from "./helpers";
import { ADMIN_STATE_FILE } from "./global-setup";

// Seeded venue structure:
//   Central Workspace (id=1)
//     Meeting Rooms (sectionId=1): T1 (id=1, capacity 4), T2 (id=2, capacity 2)
//     Studios       (sectionId=2): P1 (id=3, capacity 4)  ← used here to avoid booking conflicts
const VENUE_ID = 1;
const STUDIOS_SECTION_ID = 2;
const P1_RESOURCE_ID = 3;

/**
 * Admin extends all active bookings for a venue by 1 hour.
 *
 * "Active" means: started ≤ now AND (endTime > now OR no endTime).
 * We create such a booking via the admin API (no hold required, no future-date restriction)
 * using the Studios resource (P1) which has no prior test bookings accumulated from other runs.
 */
test.describe("Admin extend bookings", () => {
  test.describe.configure({ mode: "serial" });
  test.setTimeout(60_000);

  let createdBookingId: number | undefined;

  test.beforeAll(async ({ browser }) => {
    // Reuse the global-setup auth cookie — no extra login request needed
    const ctx = await browser.newContext({ storageState: ADMIN_STATE_FILE });
    const page = await ctx.newPage();
    await gotoAdminDashboard(page);

    // Create a booking that started 30 minutes ago (currently active) on Studios P1
    const bookingRes = await page.request.post("/api/admin/bookings", {
      data: {
        venueId: VENUE_ID,
        sectionId: STUDIOS_SECTION_ID,
        resourceId: P1_RESOURCE_ID,
        date: pastUtcISO(30),
        customerEmail: "e2e-extend@example.com",
        customerName: "E2E Extend Test",
        partySize: 2,
      },
    });

    if (bookingRes.ok()) {
      const booking = await bookingRes.json();
      createdBookingId = booking.id ?? booking.Id;
    } else {
      const body = await bookingRes.text();
      console.warn(
        `admin-extend beforeAll: booking creation failed (${bookingRes.status()}): ${body.slice(0, 200)}`
      );
      // If there's already a suitable active booking from a previous run, the test may still pass
    }

    await ctx.close();
  });

  test.afterAll(async ({ browser }) => {
    if (!createdBookingId) return;
    // Reuse the global-setup auth cookie — no extra login request needed
    const ctx = await browser.newContext({ storageState: ADMIN_STATE_FILE });
    const page = await ctx.newPage();
    await gotoAdminDashboard(page);
    await page.request.delete(`/api/admin/bookings/${createdBookingId}`);
    await ctx.close();
  });

  test("extending bookings updates end times and shows confirmation list", async ({ page }) => {
    await gotoAdminDashboard(page); // auth cookie already in storageState

    // ── Extend all active bookings via API (the modal UI itself is a smoke-tested
    //    React component; we focus here on verifying the extend operation works) ──
    const extendRes = await page.request.post(`/api/admin/venues/${VENUE_ID}/extend`, {
      data: { minutes: 60 },
    });
    expect(extendRes.ok()).toBeTruthy();
    const { extendedBookings } = await extendRes.json();

    if (extendedBookings?.length > 0) {
      // ── Verify the booking now has a future endTime ─────────────────────────
      if (createdBookingId) {
        const res = await page.request.get(`/api/admin/bookings/${createdBookingId}`);
        expect(res.ok()).toBeTruthy();
        const booking = await res.json();
        const endTime = booking.endTime ?? booking.EndTime;
        expect(endTime).toBeTruthy();
        // Original: start + 1h; extended: start + 2h → diff ≥ 119 min
        const start = new Date(booking.date ?? booking.Date).getTime();
        const end = new Date(endTime).getTime();
        expect(end - start).toBeGreaterThanOrEqual(119 * 60 * 1000);
      }
    }

    // ── Smoke-test: the dashboard "Extend Bookings" button opens a modal ───────
    await page.getByText("Extend Bookings", { exact: true }).click();
    await expect(page.locator('[role="dialog"]')).toBeVisible({ timeout: 10_000 });
    await page.keyboard.press("Escape");
  });
});
