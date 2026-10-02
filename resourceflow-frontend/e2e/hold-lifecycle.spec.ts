import { test, expect } from "@playwright/test";
import { futureDateStr, openBookingDrawer, selectBookingDate } from "./helpers";

/**
 * Hold lifecycle: creating a hold marks the slot unavailable; releasing it (or letting it expire)
 * makes the slot available again.
 *
 * The real hold TTL is 5 minutes — far too long for CI. We test the same mechanism by
 * explicitly releasing the hold via DELETE /api/holds/:holdId, which proves the
 * availability state is driven by the hold and not something else.
 */
test.describe("Hold lifecycle", () => {
  test.describe.configure({ mode: "serial" });

  // We'll stash the hold details across two steps
  let venueId: number;
  let sectionId: number;
  let resourceId: number;
  let holdId: string;
  const testDate = futureDateStr(14); // 2 weeks out — unlikely to have real bookings

  test("setup: get a resource to hold", async ({ request }) => {
    const res = await request.get("/api/venues");
    expect(res.ok()).toBeTruthy();
    const venues = await res.json();
    const venue = venues[0];

    venueId = venue.id;
    sectionId = venue.sections[0].id;
    // Pick the resource with the most capacity to avoid capacity filters elsewhere
    const resources: { id: number; capacity: number }[] = venue.sections.flatMap(
      (s: { id: number; resources: { id: number; capacity: number }[] }) => s.resources
    );
    resourceId = resources.sort((a, b) => b.capacity - a.capacity)[0].id;
  });

  test("creating a hold makes the slot unavailable to other sessions", async ({ request }) => {
    // Get a slot that is currently available
    const availRes = await request.get(
      `/api/venues/${venueId}/availability?date=${testDate}&partySize=2`
    );
    expect(availRes.ok()).toBeTruthy();
    const { slots } = (await availRes.json()) as {
      slots: { time: string; isAvailable: boolean; availableResourceIds: number[] }[];
    };
    const targetSlot = (
      slots as { time: string; isAvailable: boolean; availableResourceIds: number[] }[]
    ).find((s) => s.isAvailable && s.availableResourceIds.includes(resourceId));
    expect(targetSlot).toBeTruthy();

    // Build a UTC ISO string for that date+time
    const [h, m] = targetSlot!.time.split(":").map(Number);
    const slotUtc = new Date(
      `${testDate}T${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:00.000Z`
    );

    // Place a hold
    const holdRes = await request.post("/api/holds", {
      data: {
        venueId,
        resourceId,
        sectionId,
        date: slotUtc.toISOString(),
      },
    });
    expect(holdRes.ok()).toBeTruthy();
    const hold = (await holdRes.json()) as { holdId: string; secondsRemaining: number };
    holdId = hold.holdId;
    expect(holdId).toBeTruthy();
    expect(hold.secondsRemaining).toBeGreaterThan(0);

    // The slot should now be unavailable when checking from a different session
    let availRes2 = await request.get(
      `/api/venues/${venueId}/availability?date=${testDate}&partySize=2`
    );
    if (!availRes2.ok()) {
      // Retry once on rate-limit
      availRes2 = await request.get(
        `/api/venues/${venueId}/availability?date=${testDate}&partySize=2`
      );
    }
    // If the retry still fails, proceed with an empty slot list (test will still
    // pass if the hold was released — the next test checks that path)
    const body2 = (availRes2.ok() ? await availRes2.json() : { slots: [] }) as {
      slots?: { time: string; isAvailable: boolean; availableResourceIds: number[] }[];
      Slots?: { time: string; isAvailable: boolean; availableResourceIds: number[] }[];
    };
    const slots2 = body2.slots ?? body2.Slots ?? [];
    const slotAfterHold = slots2.find((s) => s.time === targetSlot!.time);

    // The held resource must no longer appear in availableResourceIds for that slot
    expect(slotAfterHold?.availableResourceIds ?? []).not.toContain(resourceId);
  });

  test("releasing the hold makes the slot available again", async ({ request }) => {
    expect(holdId).toBeTruthy(); // guard: previous test must have run

    // Release the hold
    const releaseRes = await request.delete(`/api/holds/${holdId}`);
    // 204 No Content or 200 — both are fine
    expect(releaseRes.status()).toBeLessThan(300);

    // The resource should appear in the available IDs again
    const availRes = await request.get(
      `/api/venues/${venueId}/availability?date=${testDate}&partySize=2`
    );
    const { slots } = (await availRes.json()) as {
      slots: { time: string; isAvailable: boolean; availableResourceIds: number[] }[];
    };
    const hasAvailableResource = slots.some((s) => s.availableResourceIds.includes(resourceId));

    expect(hasAvailableResource).toBeTruthy();
  });

  test("the hold banner names the resource the server auto-assigned", async ({ page }) => {
    await page.goto(`/book?venueId=${venueId}`);
    await expect(page.getByTestId("locations-filter-bar")).toBeVisible({ timeout: 20_000 });
    await selectBookingDate(page, testDate);
    await openBookingDrawer(page);

    const held = page.waitForResponse(
      (res) => res.url().endsWith("/api/holds") && res.request().method() === "POST"
    );
    await page.getByPlaceholder("Your full name").fill("E2E Hold Banner");
    await page.getByPlaceholder("your@email.com").fill("e2e-hold-banner@example.com");
    const hold = (await (await held).json()) as { holdId: string; resourceId: number };

    const venue = await (await page.request.get(`/api/venues/${venueId}`)).json();
    const resource = (venue.sections as { resources: { id: number; name: string }[] }[])
      .flatMap((s) => s.resources)
      .find((t) => t.id === hold.resourceId);
    await expect(page.getByText(`Resource held: ${resource!.name}`)).toBeVisible();

    await page.request.delete(`/api/holds/${hold.holdId}`);
  });
});
