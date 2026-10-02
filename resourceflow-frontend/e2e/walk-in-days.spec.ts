import { test, expect, type Browser } from "@playwright/test";
import { buildUpdateVenueBody, selectBookingDate } from "./helpers";
import { ADMIN_STATE_FILE } from "./global-setup";

const CENTRAL_WORKSPACE_ID = 1;

/**
 * Day-scoped walk-in-only — distinct from the location-wide walkInOnly case
 * covered in venue-detail.spec.ts. When `walkInDays` includes a given
 * ISO day (1=Mon … 7=Sun) but `walkInOnly` is false, the location is bookable
 * on most days but the booking form must disable itself for the walk-in days
 * and show the day-scoped WalkInNotice ("Walk-ins only on this day").
 *
 * Setup/teardown runs via the admin API (full UpdateVenueRequest body so
 * no other field is wiped — see buildUpdateVenueBody). The customer-facing
 * assertion navigates the booking form unauthenticated.
 */
test.describe("Walk-in-only day on the booking form", () => {
  test.describe.configure({ mode: "serial" });

  let original: Record<string, unknown> | undefined;

  // Tomorrow, far enough out that its slots aren't half-gone.
  const tomorrow = new Date(Date.now() + 24 * 60 * 60 * 1000);
  const tomorrowIsoDay = ((tomorrow.getUTCDay() + 6) % 7) + 1; // 1=Mon … 7=Sun
  const tomorrowStr = tomorrow.toISOString().split("T")[0];

  // A guaranteed-non-walk-in day: the ISO day after tomorrow, wrapped.
  const otherDay = new Date(tomorrow.getTime() + 24 * 60 * 60 * 1000);
  const otherDayStr = otherDay.toISOString().split("T")[0];

  async function putVenue(browser: Browser, body: Record<string, unknown>) {
    const ctx = await browser.newContext({ storageState: ADMIN_STATE_FILE });
    const page = await ctx.newPage();
    const res = await page.request.put(`/api/venues/${CENTRAL_WORKSPACE_ID}`, { data: body });
    expect(res.ok()).toBeTruthy();
    await ctx.close();
  }

  test.beforeAll(async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const res = await page.request.get(`/api/venues/${CENTRAL_WORKSPACE_ID}`);
    original = (await res.json()) as Record<string, unknown>;
    await ctx.close();

    // Mark tomorrow's weekday as walk-in-only (location-wide flag stays false).
    await putVenue(
      browser,
      buildUpdateVenueBody(original, {
        walkInOnly: false,
        walkInDays: String(tomorrowIsoDay),
      })
    );
  });

  test.afterAll(async ({ browser }) => {
    if (!original) return;
    await putVenue(
      browser,
      buildUpdateVenueBody(original, {
        walkInOnly: original.walkInOnly,
        walkInDays: original.walkInDays,
      })
    );
  });

  test("selecting the walk-in-only day shows the day-scoped notice and hides slots", async ({
    page,
  }) => {
    await page.goto(`/book?venueId=${CENTRAL_WORKSPACE_ID}`);
    await expect(page.getByTestId("locations-filter-bar")).toBeVisible({ timeout: 20_000 });

    // Select tomorrow (a walk-in-only day) via the page-level calendar picker
    // (same helper as booking-flow.spec.ts).
    await selectBookingDate(page, tomorrowStr);

    // The card's slot row is replaced by the walk-in line, so there is no time to
    // tap and no way into the booking drawer for that day. Scope to the card:
    // `/book?venueId=` deep-links into the full locations list, so an
    // unscoped slot count also picks up every other location's times.
    const walkInCard = page.getByTestId(`location-item-${CENTRAL_WORKSPACE_ID}`);
    await expect(
      walkInCard.getByText("No reservations required, first come first served")
    ).toBeVisible({ timeout: 15_000 });
    await expect(walkInCard.getByLabel(/^Book .+ at \d{2}:\d{2}$/)).toHaveCount(0);
  });

  test("a non-walk-in day still offers bookable times", async ({ page }) => {
    await page.goto(`/book?venueId=${CENTRAL_WORKSPACE_ID}`);
    await expect(page.getByTestId("locations-filter-bar")).toBeVisible({ timeout: 20_000 });

    // Select a different day that isn't in walkInDays.
    await selectBookingDate(page, otherDayStr);

    // Bookable times appear on the card once availability loads — proves the
    // booking flow is live for this day. Scoped for the same reason as above:
    // the walk-in line has to be absent from *this* card, not merely from a page
    // that happens to list no other walk-in location.
    const bookableCard = page.getByTestId(`location-item-${CENTRAL_WORKSPACE_ID}`);
    await expect(bookableCard.getByLabel(/^Book .+ at \d{2}:\d{2}$/).first()).toBeVisible({
      timeout: 20_000,
    });
    await expect(
      bookableCard.getByText("No reservations required, first come first served")
    ).toHaveCount(0);
  });
});
