import { test, expect } from "@playwright/test";
import { gotoAdminDashboard, expectVisibleWithReload } from "./helpers";

// Seeded venue structure (see admin-extend.spec.ts for the same reference resource):
//   Central Workspace (id=1)
//     Meeting Rooms (sectionId=1)
//     Studios       (sectionId=2)
const VENUE_ID = 1;
const MEETING_ROOMS_SECTION_ID = 1;
const STUDIOS_SECTION_ID = 2;

/**
 * Reorderable sections per location.
 *
 * True end-to-end coverage through the real UI: navigates to the admin
 * "Locations" settings screen, clicks the move-down button on the first
 * section block (Meeting Rooms), and verifies the on-screen order flips and that
 * the new order survives a full page reload (i.e. it was actually persisted
 * through the real HTTP pipeline + database, not just local component state).
 *
 * Mirrors the existing admin-pause.spec.ts pattern of combining real UI
 * interaction with `page.request` for setup/teardown against the seeded
 * "Central Workspace" venue shared by other admin specs.
 */
test.describe("Admin reorder sections", () => {
  test.describe.configure({ mode: "serial" });

  test.afterEach(async ({ page }) => {
    // Always restore the seeded order (Meeting Rooms, Studios) so other specs relying
    // on Central Workspace's section/resource layout aren't affected by this spec.
    await page.request.patch(`/api/admin/venues/${VENUE_ID}/sections/reorder`, {
      data: { sectionIds: [MEETING_ROOMS_SECTION_ID, STUDIOS_SECTION_ID] },
    });
  });

  test("moving a section down via the settings UI persists across reload", async ({ page }) => {
    await gotoAdminDashboard(page);
    await page.goto("/admin/locations");
    // /locations hydrates from rate-limited admin fetches; reload (cool-down
    // first) if the page hasn't rendered within the window.
    await expectVisibleWithReload(page, page.getByText("Sections & resources"), {
      timeout: 20_000,
    });

    // Sanity check the seeded starting order: Meeting Rooms above Studios.
    const meetingRoomsBefore = await page.getByText("Meeting Rooms", { exact: true }).boundingBox();
    const studiosBefore = await page.getByText("Studios", { exact: true }).boundingBox();
    expect(meetingRoomsBefore).not.toBeNull();
    expect(studiosBefore).not.toBeNull();
    expect(meetingRoomsBefore!.y).toBeLessThan(studiosBefore!.y);

    // Click "move down" on the first section block (Meeting Rooms) — swaps it with Studios.
    await page.getByTestId("section-move-down-btn").first().click();

    // The on-screen order should flip immediately (optimistic UI update after the
    // PATCH resolves): Studios now above Meeting Rooms.
    await expect(async () => {
      const meetingRoomsAfter = await page
        .getByText("Meeting Rooms", { exact: true })
        .boundingBox();
      const studiosAfter = await page.getByText("Studios", { exact: true }).boundingBox();
      expect(studiosAfter!.y).toBeLessThan(meetingRoomsAfter!.y);
    }).toPass({ timeout: 10_000 });

    // Reload the page — this refetches from the API, proving the new order was
    // actually persisted server-side rather than only held in client state.
    await page.reload();
    await expect(page.getByText("Sections & resources")).toBeVisible({ timeout: 20_000 });
    const meetingRoomsAfterReload = await page
      .getByText("Meeting Rooms", { exact: true })
      .boundingBox();
    const studiosAfterReload = await page.getByText("Studios", { exact: true }).boundingBox();
    expect(studiosAfterReload!.y).toBeLessThan(meetingRoomsAfterReload!.y);

    // Cross-check via the public, unauthenticated venue endpoint too — the
    // same one the customer booking flow reads from.
    const publicRes = await page.request.get(`/api/venues/${VENUE_ID}`);
    expect(publicRes.ok()).toBeTruthy();
    const body = await publicRes.json();
    const names = (body.sections as { name: string }[]).map((s) => s.name);
    expect(names).toEqual(["Studios", "Meeting Rooms"]);
  });

  test("move-up button is disabled for the first section, move-down disabled for the last", async ({
    page,
  }) => {
    await gotoAdminDashboard(page);
    await page.goto("/admin/locations");
    // /locations hydrates from rate-limited admin fetches; reload (cool-down
    // first) if the page hasn't rendered within the window.
    await expectVisibleWithReload(page, page.getByText("Sections & resources"), {
      timeout: 20_000,
    });

    const moveUpButtons = page.getByTestId("section-move-up-btn");
    const moveDownButtons = page.getByTestId("section-move-down-btn");

    // react-native-web renders Pressable's `disabled` prop as `aria-disabled="true"`
    // on a plain <div> (not a native form control), so Playwright's toBeDisabled()
    // matcher (which only recognizes native disableable elements) doesn't apply —
    // assert on the aria-disabled attribute directly instead.
    // Meeting Rooms is first → its move-up button must be disabled.
    await expect(moveUpButtons.first()).toHaveAttribute("aria-disabled", "true");
    // Studios is last → its move-down button must be disabled.
    await expect(moveDownButtons.last()).toHaveAttribute("aria-disabled", "true");
  });
});
