import { post, del } from "./client";
import { apiErrorMessage } from "@/api/errors";

export interface HoldRequest {
  venueId: number;
  /** Omit (or null) for "Any section" auto-assign — the server picks the best resource. */
  resourceId: number | null;
  /** Omit (or null) for "Any section" auto-assign. */
  sectionId: number | null;
  /**
   * Combinable-resource group to hold. When set, the server reserves all the group's member
   * resources as one hold. Mutually exclusive with resourceId.
   */
  resourceGroupId?: number | null;
  /** Required for auto-assign and group holds so the server can validate capacity. */
  partySize?: number;
  date: string; // ISO 8601
  currentHoldId?: string;
}

export interface HoldResponse {
  holdId: string;
  expiresAt: string; // ISO 8601
  secondsRemaining: number;
  /** Resolved resource id for auto-assigned holds; null for explicit-resource holds. */
  resourceId?: number | null;
  /** Resolved section id for auto-assigned holds; null for explicit-resource holds. */
  sectionId?: number | null;
  /** Resolved combinable-resource group for group holds; null otherwise. */
  resourceGroupId?: number | null;
}

/** Successful hold. */
export type HoldSuccess = { ok: true; hold: HoldResponse };
/** Hold rejected (409 already-held, or 400 past-time / paused / closed / walk-in). */
export type HoldFailure = { ok: false; message: string };
export type HoldResult = HoldSuccess | HoldFailure;

const GENERIC_UNAVAILABLE = "Resource not available for this date. Please choose another.";

export async function createHold(request: HoldRequest): Promise<HoldResult> {
  try {
    const res = await post("/holds", request);

    if (res.ok) {
      return { ok: true, hold: await res.json() };
    }

    // 409 (already held/booked) and 400 (past time, paused, closed, walk-in)
    // both carry a descriptive { message } from the backend — surface it
    // instead of discarding it so the customer sees the real reason.
    const body = await res.json().catch(() => ({}));
    return { ok: false, message: apiErrorMessage(body, GENERIC_UNAVAILABLE) };
  } catch (err) {
    // Network failure or non-JSON body — fall back to a generic message.
    console.error("createHold error:", err);
    return { ok: false, message: GENERIC_UNAVAILABLE };
  }
}

export async function releaseHold(holdId: string): Promise<void> {
  try {
    await del(`/holds/${holdId}`);
  } catch {
    // Hold release failed — it will expire on its own
  }
}
