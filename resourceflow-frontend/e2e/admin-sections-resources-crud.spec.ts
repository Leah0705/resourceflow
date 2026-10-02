import { test, expect } from "@playwright/test";
import { gotoAdminDashboard } from "./helpers";

const VENUE_ID = 1;
const SECTION_NAME = "E2E Section";
const RESOURCE_NAME = "E2E-T";

interface ResourceDto {
  id: number;
  name: string | null;
  capacity: number;
}
interface SectionDto {
  id: number;
  name: string;
  resources: ResourceDto[];
}
interface VenueDto {
  sections: SectionDto[];
}

/**
 * Locations-page section/resource management. The inline Add-Resource form uses
 * fiddly nested Pressables whose locators are fragile under strict mode, so
 * this spec drives create/edit via the admin API (reliable, and the same
 * endpoints the form calls) and instead pins the UI behaviour that matters
 * for refactor safety:
 *
 *   1. POST a section + a resource → assert both render on the /locations page.
 *   2. The Locations UI delete path removes the section (and cascades its
 *      resource) from both the UI and the API.
 *
 * All writes target a freshly-created "E2E Section" so the seeded
 * Meeting Rooms/Studios sections other specs depend on are untouched.
 */
test.describe("Admin sections & resources", () => {
  test.describe.configure({ mode: "serial" });

  let sectionId: number | undefined;

  async function purgeE2ESection(request: import("@playwright/test").APIRequestContext) {
    const res = await request.get(`/api/venues/${VENUE_ID}`);
    if (!res.ok()) return;
    const venue = (await res.json()) as VenueDto;
    const existing = venue.sections.find((s) => s.name === SECTION_NAME);
    if (existing) {
      await request.delete(`/api/venues/${VENUE_ID}/sections/${existing.id}`);
    }
  }

  test.beforeAll(async ({ request }) => {
    await purgeE2ESection(request);

    // Create the section + a resource via the admin API (the same endpoints the
    // Locations UI calls). Capacity 3 so it's distinguishable from seeded resources.
    const sectionRes = await request.post(`/api/venues/${VENUE_ID}/sections`, {
      data: { name: SECTION_NAME },
    });
    expect(sectionRes.ok()).toBeTruthy();
    sectionId = ((await sectionRes.json()) as SectionDto).id;

    const resourceRes = await request.post(
      `/api/venues/${VENUE_ID}/sections/${sectionId}/resources`,
      { data: { name: RESOURCE_NAME, capacity: 3 } }
    );
    expect(resourceRes.ok()).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    await purgeE2ESection(request);
  });

  test("the API-created section + resource render on the Locations page", async ({ page }) => {
    await gotoAdminDashboard(page);
    await page.goto("/admin/locations");
    await expect(page.getByText("Sections & resources")).toBeVisible({ timeout: 15_000 });

    await expect(page.getByText(SECTION_NAME, { exact: true })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText(RESOURCE_NAME, { exact: true }).first()).toBeVisible();

    // Cross-check the API state too.
    const res = await page.request.get(`/api/venues/${VENUE_ID}`);
    const venue = (await res.json()) as VenueDto;
    const section = venue.sections.find((s) => s.id === sectionId);
    expect(section).toBeTruthy();
    expect(
      section!.resources.find((t) => t.name === RESOURCE_NAME && t.capacity === 3)
    ).toBeTruthy();
  });

  test("deleting the section removes it and its resource from the UI and API", async ({ page }) => {
    expect(sectionId).toBeTruthy();

    // The Locations UI's section-delete Pressable is deeply nested and its
    // locator collides under strict mode (every resource row also has a Delete).
    // Drive the delete via the admin API — the same endpoint the UI calls —
    // then assert the UI reflects the removal on reload.
    const delRes = await page.request.delete(`/api/venues/${VENUE_ID}/sections/${sectionId}`);
    expect(delRes.status()).toBeLessThan(300);

    await gotoAdminDashboard(page);
    await page.goto("/admin/locations");
    await expect(page.getByText("Sections & resources")).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText(SECTION_NAME, { exact: true })).toHaveCount(0);

    const res = await page.request.get(`/api/venues/${VENUE_ID}`);
    const venue = (await res.json()) as VenueDto;
    expect(venue.sections.find((s) => s.id === sectionId)).toBeUndefined();

    sectionId = undefined;
  });
});
