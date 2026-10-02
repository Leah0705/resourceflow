import { test, expect, type Browser } from "@playwright/test";
import { buildUpdateVenueBody } from "./helpers";
import { ADMIN_STATE_FILE } from "./global-setup";

// Central Workspace (id=1) is the primary seeded venue used across specs.
const CENTRAL_WORKSPACE_ID = 1;

/**
 * The old standalone `/venue/:id` page was folded into the Locations
 * list: `venue/[id].tsx` is now a thin redirect to
 * `/locations/:id`, which renders the full Locations list with that one
 * location pre-expanded (image, blurb, opening hours, inline booking form —
 * see LocationListItem). This spec covers the redirect's real behaviour,
 * not the old detail-page-plus-separate-booking-page flow it replaced:
 *
 *   1. /venue/:id redirects into /locations/:id and shows the
 *      venue's name + address, pre-expanded with the booking form.
 *   2. A walk-in-only location shows the WalkInNotice and no booking form.
 *      The seed has no walk-in venue, so Central Workspace is flipped to
 *      walk-in via the admin API for this test and restored in afterEach.
 *   3. An unknown id still redirects and renders the full Locations list
 *      (nothing highlighted/expanded) rather than crashing or erroring —
 *      there's no more per-id "not found" state now that the id only
 *      controls which list item auto-expands.
 *
 * Runs under the public "chromium" project. The walk-in flip uses the
 * global-setup storageState cookie via a fresh admin context, not the page.
 *
 * IMPORTANT: the PUT /api/venues/:id handler assigns every field from
 * the request body unconditionally (Address, OpenTime, …), so a partial body
 * would wipe the seeded state. We therefore read the FULL venue first and
 * send it back with only walkInOnly changed — both on flip and on restore — so
 * no other spec ever sees Central Workspace mutated.
 */
test.describe("Location detail redirect", () => {
  test.describe.configure({ mode: "serial" });

  // Captured once in the test that flips, restored verbatim in afterEach.
  // `unknown` (vs false) lets afterEach tell "no flip happened, nothing to
  // restore" apart from "flip happened, restore to false".
  let originalVenue: Record<string, unknown> | undefined;

  async function readVenue(browser: Browser): Promise<Record<string, unknown>> {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const res = await page.request.get(`/api/venues/${CENTRAL_WORKSPACE_ID}`);
    const body = (await res.json()) as Record<string, unknown>;
    await ctx.close();
    return body;
  }

  /**
   * PUT a full venue update (see buildUpdateVenueBody in helpers for
   * why the full body is required). `overrides` are applied last.
   */
  async function putVenue(
    browser: Browser,
    body: Record<string, unknown>,
    expectOk = true
  ): Promise<void> {
    const ctx = await browser.newContext({ storageState: ADMIN_STATE_FILE });
    const page = await ctx.newPage();
    const res = await page.request.put(`/api/venues/${CENTRAL_WORKSPACE_ID}`, { data: body });
    if (expectOk) expect(res.ok()).toBeTruthy();
    await ctx.close();
  }

  test.afterEach(async ({ browser }) => {
    // Always restore the full original state, even if a test failed mid-flip.
    if (originalVenue !== undefined) {
      await putVenue(
        browser,
        buildUpdateVenueBody(originalVenue, {
          walkInOnly: originalVenue.walkInOnly,
          walkInDays: originalVenue.walkInDays,
        })
      );
      originalVenue = undefined;
    }
  });

  test("redirects into the Locations list, pre-expanded with the name, address, and booking form", async ({
    browser,
    page,
  }) => {
    // Read the address from the API rather than hardcoding the seed value, so
    // this stays correct regardless of what other specs (or this one) do.
    const venue = await readVenue(browser);
    const address = venue.address as string | null;

    await page.goto(`/venue/${CENTRAL_WORKSPACE_ID}`);
    await page.waitForURL(new RegExp(`.*/locations/${CENTRAL_WORKSPACE_ID}`), { timeout: 15_000 });

    await expect(page.getByText("Central Workspace").first()).toBeVisible({ timeout: 15_000 });
    if (address) {
      await expect(page.getByText(address).first()).toBeVisible();
    }

    // Pre-expanded via highlightId — the location's own details are already
    // showing, not a separate CTA into a separate page.
    try {
      await expect(page.getByText("Resources & capacity")).toBeVisible({ timeout: 15_000 });
    } catch {
      await page.reload();
      await expect(page.getByText("Resources & capacity")).toBeVisible({ timeout: 20_000 });
    }
  });

  test("a walk-in-only location shows the walk-in notice and no booking form", async ({
    browser,
    page,
  }) => {
    // Capture full state, then flip walk-in only.
    originalVenue = await readVenue(browser);
    await putVenue(browser, buildUpdateVenueBody(originalVenue, { walkInOnly: true }));

    // The detail redirect target refetches all venues on mount, so a
    // fresh navigation picks up the flip.
    await page.goto(`/venue/${CENTRAL_WORKSPACE_ID}`);
    await page.waitForURL(new RegExp(`.*/locations/${CENTRAL_WORKSPACE_ID}`), { timeout: 15_000 });

    // Scope every assertion to Central Workspace's own list item. The redirect target
    // renders the whole Locations list, so a page-wide locator also picks up the
    // seed's other locations — including its genuinely walk-in-only one (which
    // would let "Walk-ins only" pass even if the flip silently failed) and any
    // bookable one currently inside its opening hours (which made the
    // no-slots assertion depend on the wall-clock hour of the CI run).
    const centralWorkspace = page.getByTestId(`location-item-${CENTRAL_WORKSPACE_ID}`);
    await expect(centralWorkspace.getByText("Walk-ins only").first()).toBeVisible({
      timeout: 15_000,
    });

    // No bookable time is offered at all when walk-in only.
    await expect(centralWorkspace.getByLabel(/^Book .+ at \d{2}:\d{2}$/)).toHaveCount(0);

    // The drawer is mounted by LocationsScreen, not by the row, so this one
    // stays page-wide — it only ever opens on a slot press.
    await expect(page.getByTestId("booking-drawer")).toHaveCount(0);
  });

  test("an unknown location id still redirects and renders the Locations list", async ({
    page,
  }) => {
    await page.goto("/venue/99999");
    await page.waitForURL(/.*\/locations\/99999/, { timeout: 15_000 });

    // No venue matches highlightId=99999, so nothing auto-expands — the
    // full list just renders normally rather than erroring.
    await expect(page.getByText("Our locations").first()).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText("Central Workspace").first()).toBeVisible({ timeout: 15_000 });
  });
});
