import { test, expect } from "@playwright/test";

test.describe("Home Page", { tag: "@smoke" }, () => {
  test("should load the home page and show locations", async ({ page }) => {
    await page.goto("/");

    // Check if the app name is visible in the hero section
    await expect(page.locator("text=ResourceFlow").first()).toBeVisible();

    // Check if Navbar is visible
    await expect(page.getByRole("link", { name: "Locations" })).toBeVisible();
    await expect(page.getByRole("link", { name: "My Bookings" })).toBeVisible();

    // Check if we have venue cards
    const venueCards = page
      .getByRole("link")
      .filter({ has: page.locator("text=/./") })
      .filter({ hasNotText: "Locations" })
      .filter({ hasNotText: "My Bookings" })
      .filter({ hasNotText: "Admin" });

    // Wait for the API to load and cards to appear
    await expect(venueCards.first()).toBeVisible({ timeout: 15000 });

    const count = await venueCards.count();

    expect(count).toBeGreaterThan(0);
  });

  test("should navigate to booking page when clicking a location", async ({ page }) => {
    // Booking specs that run first can exhaust the 60 req/min Docker rate limit.
    // Poll with reloads (up to 4×) to give the window time to recover.
    test.setTimeout(120_000);

    await page.goto("/");

    const venueCard = page.getByText("Central Workspace").first();
    let visible = false;
    for (let i = 0; i < 4; i++) {
      if (i > 0) {
        await page.waitForTimeout(20_000);
        await page.reload();
      }
      try {
        await expect(venueCard).toBeVisible({ timeout: 10_000 });
        visible = true;
        break;
      } catch {
        // rate-limit window hasn't cleared yet — loop and wait
      }
    }
    if (!visible) throw new Error("Location cards never appeared — rate limit did not recover");

    await venueCard.click({ force: true });
    // Venue cards route into the merged Locations page, which scrolls to the
    // clicked location and expands its details. Booking starts from a slot on the
    // card, so the page-level filter bar is what proves the list has hydrated.
    await page.waitForURL(/.*\/locations\/\d+/, { timeout: 10_000 });
    await expect(page.getByTestId("locations-filter-bar")).toBeVisible({ timeout: 20_000 });
  });
});
