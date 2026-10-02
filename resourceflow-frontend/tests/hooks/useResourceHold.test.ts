import { renderHook, act } from "@testing-library/react-native";
import { useResourceHold, UseResourceHoldParams } from "@/components/booking/useResourceHold";

const mockCreateHold = jest.fn();
const mockReleaseHold = jest.fn();
const mockScheduleNotice = jest.fn();
const mockCancelNotice = jest.fn();

jest.mock("@/api/holds", () => ({
  createHold: (...args: unknown[]) => mockCreateHold(...args),
  releaseHold: (...args: unknown[]) => mockReleaseHold(...args),
}));

// Inert on web, so the hook's own wiring is what these tests observe.
jest.mock("@/services/holdExpiryNotice", () => ({
  scheduleHoldExpiryNotice: (...args: unknown[]) => mockScheduleNotice(...args),
  cancelHoldExpiryNotice: (...args: unknown[]) => mockCancelNotice(...args),
}));

beforeEach(() => {
  mockCreateHold.mockReset();
  mockReleaseHold.mockReset();
  mockScheduleNotice.mockReset().mockResolvedValue("notice-1");
  mockCancelNotice.mockReset().mockResolvedValue(undefined);
  jest.useFakeTimers();
  jest.spyOn(console, "error").mockImplementation();
});

afterEach(() => {
  jest.useRealTimers();
});

const defaultParams = {
  venueId: 1,
  sections: [{ id: 10, resources: [{ id: 100 }] }],
  resourceId: undefined as number | undefined,
  date: "2026-06-15",
  time: "19:00",
  email: "test@example.com",
};

describe("useResourceHold", () => {
  it("starts with idle status", () => {
    const { result } = renderHook(() => useResourceHold(defaultParams));

    expect(result.current.holdStatus).toBe("idle");
    expect(result.current.hold).toBeNull();
    expect(result.current.secondsLeft).toBe(0);
  });

  it("remains idle when resourceId is undefined", () => {
    const { result } = renderHook(() => useResourceHold(defaultParams));

    // Advance past debounce
    act(() => {
      jest.advanceTimersByTime(3000);
    });

    expect(result.current.holdStatus).toBe("idle");
    expect(mockCreateHold).not.toHaveBeenCalled();
  });

  it("transitions to pending then held after debounce when resourceId is set", async () => {
    const expiresAt = new Date(Date.now() + 120_000).toISOString();
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-1",
        expiresAt,
        secondsRemaining: 120,
      },
    });

    const params = { ...defaultParams, resourceId: 100 };
    const { result } = renderHook(() => useResourceHold(params));

    // Should be pending before debounce fires
    expect(result.current.holdStatus).toBe("pending");

    // Advance past the 2000ms debounce
    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(mockCreateHold).toHaveBeenCalledWith(
      expect.objectContaining({
        venueId: 1,
        resourceId: 100,
        sectionId: 10,
      })
    );
    expect(result.current.holdStatus).toBe("held");
    expect(result.current.hold).not.toBeNull();
    expect(result.current.hold?.holdId).toBe("hold-1");
  });

  it("sends the participant count with an explicit resource, so the hold blocks for its duration rule", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: { holdId: "hold-1", expiresAt: new Date(Date.now() + 120_000).toISOString() },
    });

    renderHook(() => useResourceHold({ ...defaultParams, resourceId: 100, partySize: 5 }));
    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(mockCreateHold).toHaveBeenCalledWith(
      expect.objectContaining({ resourceId: 100, sectionId: 10, partySize: 5 })
    );
  });

  it("sets unavailable status when createHold fails", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: false,
      message: "Cannot hold a resource for a past time.",
    });

    const params = { ...defaultParams, resourceId: 100 };
    const { result } = renderHook(() => useResourceHold(params));

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("unavailable");
    expect(result.current.holdMessage).toBe("Cannot hold a resource for a past time.");
  });

  it("countdown decreases secondsLeft and expires to idle", async () => {
    // Hold expires in 3 seconds for fast test
    const expiresAt = new Date(Date.now() + 3_000).toISOString();
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-2",
        expiresAt,
        secondsRemaining: 3,
      },
    });

    const params = { ...defaultParams, resourceId: 100 };
    const { result } = renderHook(() => useResourceHold(params));

    await act(async () => {
      jest.advanceTimersByTime(2000); // debounce
    });

    expect(result.current.holdStatus).toBe("held");
    expect(result.current.secondsLeft).toBeGreaterThanOrEqual(0);

    // Advance past expiry
    act(() => {
      jest.advanceTimersByTime(4000);
    });

    expect(result.current.holdStatus).toBe("expired");
    expect(result.current.hold).toBeNull();
  });

  it("releases hold on unmount", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-cleanup",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    const params = { ...defaultParams, resourceId: 100 };
    const { result, unmount } = renderHook(() => useResourceHold(params));

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");

    unmount();

    expect(mockReleaseHold).toHaveBeenCalledWith("hold-cleanup");
  });

  // ── Placement changes drop the hold they invalidated ───────────────────────
  //
  // The placement pick reaches this hook only as params, so these cover the whole contract that
  // BookingForm used to enforce with an explicit releaseCurrentHold() call: a pick that stops
  // being holdable releases outright, and a pick that resolves elsewhere is replaced atomically.

  it("releases the hold when the placement pick is cleared", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-manual",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    const { result, rerender } = renderHook(
      (props: UseResourceHoldParams) => useResourceHold(props),
      {
        initialProps: { ...defaultParams, resourceId: 100 },
      }
    );

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");

    // Switching to a section with no fitting resource leaves the form with no pick at all.
    rerender({ ...defaultParams, resourceId: undefined });

    expect(mockReleaseHold).toHaveBeenCalledWith("hold-manual");
    expect(result.current.holdStatus).toBe("idle");
    expect(result.current.hold).toBeNull();
    expect(result.current.holdId).toBeNull();
  });

  it("keeps the hold when a param outside the held unit changes", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-kept",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    const { result, rerender } = renderHook(
      (props: UseResourceHoldParams) => useResourceHold(props),
      {
        initialProps: { ...defaultParams, resourceId: 100 },
      }
    );

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");

    // The email is a trigger for the effect but not part of the held unit, so correcting a typo
    // must not churn a hold that is still valid.
    rerender({ ...defaultParams, resourceId: 100, email: "corrected@example.com" });

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");
    expect(result.current.hold?.holdId).toBe("hold-kept");
    expect(mockCreateHold).toHaveBeenCalledTimes(1);
    expect(mockReleaseHold).not.toHaveBeenCalled();
  });

  it("replaces the hold when the placement pick moves to another resource", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-resource-100",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    const sections = [
      { id: 10, resources: [{ id: 100 }] },
      { id: 20, resources: [{ id: 200 }] },
    ];
    const { result, rerender } = renderHook(
      (props: UseResourceHoldParams) => useResourceHold(props),
      {
        initialProps: { ...defaultParams, sections, resourceId: 100 },
      }
    );

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");

    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-resource-200",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    // Picking a resource in another section: the old hold stops standing the moment the pick changes.
    rerender({ ...defaultParams, sections, resourceId: 200 });
    expect(result.current.holdStatus).toBe("pending");

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(mockCreateHold).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        resourceId: 200,
        sectionId: 20,
        currentHoldId: "hold-resource-100",
      })
    );
    expect(result.current.hold?.holdId).toBe("hold-resource-200");
  });

  it("replaces a resource hold when the pick switches to a combinable group", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-resource",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    const { result, rerender } = renderHook(
      (props: UseResourceHoldParams) => useResourceHold(props),
      {
        initialProps: { ...defaultParams, resourceId: 100, partySize: 4 },
      }
    );

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");

    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-group",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
        resourceGroupId: 5,
      },
    });

    // resourceId and resourceGroupId are mutually exclusive in the dropdown, so the group pick arrives
    // with the resource cleared.
    rerender({ ...defaultParams, resourceId: undefined, resourceGroupId: 5, partySize: 4 });

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(mockCreateHold).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        resourceId: null,
        sectionId: null,
        resourceGroupId: 5,
        partySize: 4,
        currentHoldId: "hold-resource",
      })
    );
    expect(result.current.resolvedGroupId).toBe(5);
  });

  it("re-triggers hold when date changes, passing currentHoldId for atomic replace", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-date-1",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    const { result, rerender } = renderHook(
      (props: UseResourceHoldParams) => useResourceHold(props),
      {
        initialProps: { ...defaultParams, resourceId: 100 },
      }
    );

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");
    expect(mockCreateHold).toHaveBeenCalledTimes(1);

    // Change date
    const newProps = { ...defaultParams, resourceId: 100, date: "2026-06-16" };

    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-date-2",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    rerender(newProps);

    expect(result.current.holdStatus).toBe("pending");

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    // Backend handles the release atomically — no explicit releaseHold call on success
    expect(mockReleaseHold).not.toHaveBeenCalled();
    // currentHoldId forwarded so backend can replace atomically
    expect(mockCreateHold).toHaveBeenCalledTimes(2);
    expect(mockCreateHold).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({ currentHoldId: "hold-date-1" })
    );
    expect(result.current.hold?.holdId).toBe("hold-date-2");
  });

  it("releases previous hold explicitly when createHold fails", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "hold-fail-1",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
      },
    });

    const { result, rerender } = renderHook(
      (props: UseResourceHoldParams) => useResourceHold(props),
      {
        initialProps: { ...defaultParams, resourceId: 100 },
      }
    );

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("held");

    // Second attempt fails — resource taken by someone else
    mockCreateHold.mockResolvedValueOnce({
      ok: false,
      message: "This resource is already held by another user.",
    });
    rerender({ ...defaultParams, resourceId: 100, date: "2026-06-16" });

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    // Previous hold must be explicitly released since backend didn't consume it
    expect(mockReleaseHold).toHaveBeenCalledWith("hold-fail-1");
    expect(result.current.holdStatus).toBe("unavailable");
    expect(result.current.hold).toBeNull();
  });

  // ── Auto-assign ("Any section") mode ───────────────────────────────────────

  it("auto-assign mode fires hold without a resourceId and adopts the server-resolved resource", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: {
        holdId: "auto-hold-1",
        expiresAt: new Date(Date.now() + 120_000).toISOString(),
        secondsRemaining: 120,
        resourceId: 42, // server-resolved
        sectionId: 7,
      },
    });

    // autoAssign + party size; resourceId left undefined.
    const params: UseResourceHoldParams = {
      ...defaultParams,
      autoAssign: true,
      partySize: 2,
    };
    const { result } = renderHook(() => useResourceHold(params));

    expect(result.current.holdStatus).toBe("pending");

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    // Must send null resource/section + party size so the server picks the resource.
    expect(mockCreateHold).toHaveBeenCalledWith(
      expect.objectContaining({
        venueId: 1,
        resourceId: null,
        sectionId: null,
        partySize: 2,
      })
    );
    expect(result.current.holdStatus).toBe("held");
    expect(result.current.resolvedResourceId).toBe(42);
    expect(result.current.resolvedSectionId).toBe(7);
  });

  it("auto-assign mode surfaces unavailable when server rejects", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: false,
      message: "No resources are available for the requested time and participant count.",
    });

    const params: UseResourceHoldParams = {
      ...defaultParams,
      autoAssign: true,
      partySize: 99,
    };
    const { result } = renderHook(() => useResourceHold(params));

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(result.current.holdStatus).toBe("unavailable");
    expect(result.current.resolvedResourceId).toBeNull();
    expect(result.current.resolvedSectionId).toBeNull();
  });

  it("places no hold on a day the location takes no online bookings", async () => {
    // The walk-in/closed-day guard: the server would reject this hold anyway, and the
    // rejection would surface as an error on a form that has already explained itself.
    const params: UseResourceHoldParams = {
      ...defaultParams,
      autoAssign: true,
      partySize: 2,
      enabled: false,
    };
    const { result } = renderHook(() => useResourceHold(params));

    await act(async () => {
      jest.advanceTimersByTime(3000);
    });

    expect(mockCreateHold).not.toHaveBeenCalled();
    expect(result.current.holdStatus).toBe("idle");
  });

  it("releases a held resource when the selected day stops taking online bookings", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: { holdId: "hold-9", expiresAt: new Date(Date.now() + 120_000).toISOString() },
    });

    const { result, rerender } = renderHook(
      (props: UseResourceHoldParams) => useResourceHold(props),
      {
        initialProps: { ...defaultParams, resourceId: 100, enabled: true } as UseResourceHoldParams,
      }
    );

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });
    expect(result.current.holdStatus).toBe("held");

    await act(async () => {
      rerender({ ...defaultParams, resourceId: 100, enabled: false } as UseResourceHoldParams);
    });

    expect(mockReleaseHold).toHaveBeenCalledWith("hold-9");
    expect(result.current.holdStatus).toBe("idle");
  });
});

/**
 * The warning is scheduled against the hold that raised it, so every path that
 * ends a hold has to withdraw it — otherwise a guest who moved on gets told a resource is about
 * to lapse that was released minutes ago.
 */
describe("useResourceHold expiry warning", () => {
  const heldHold = (holdId: string) => ({
    ok: true,
    hold: { holdId, expiresAt: new Date(Date.now() + 300_000).toISOString() },
  });

  const holdSomething = async (props?: Partial<UseResourceHoldParams>) => {
    mockCreateHold.mockResolvedValueOnce(heldHold("hold-n1"));
    const view = renderHook((p: UseResourceHoldParams) => useResourceHold(p), {
      initialProps: { ...defaultParams, resourceId: 100, ...props } as UseResourceHoldParams,
    });
    await act(async () => {
      jest.advanceTimersByTime(2000);
    });
    return view;
  };

  it("schedules a warning against the hold the server placed", async () => {
    const { result } = await holdSomething();

    expect(result.current.holdStatus).toBe("held");
    expect(mockScheduleNotice).toHaveBeenCalledWith(result.current.hold!.expiresAt);
  });

  it("withdraws the previous warning when the replacement hold is refused", async () => {
    const { rerender } = await holdSomething();
    mockCancelNotice.mockClear();
    mockCreateHold.mockResolvedValueOnce({ ok: false, message: "Resource not available." });

    await act(async () => {
      rerender({ ...defaultParams, resourceId: 101 } as UseResourceHoldParams);
    });
    await act(async () => {
      jest.advanceTimersByTime(2000);
    });
    expect(mockCancelNotice).toHaveBeenCalledWith("notice-1");
    expect(mockScheduleNotice).toHaveBeenCalledTimes(1);
  });

  it("schedules nothing for a hold that was never placed", async () => {
    mockCreateHold.mockResolvedValueOnce({ ok: false, message: "Resource not available." });
    renderHook(() => useResourceHold({ ...defaultParams, resourceId: 100 }));

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    expect(mockScheduleNotice).not.toHaveBeenCalled();
    expect(mockCancelNotice).not.toHaveBeenCalled();
  });

  it("withdraws the warning when the selection stops being holdable", async () => {
    const { rerender } = await holdSomething();
    mockCancelNotice.mockClear();

    await act(async () => {
      rerender({ ...defaultParams, resourceId: 100, enabled: false } as UseResourceHoldParams);
    });

    expect(mockCancelNotice).toHaveBeenCalledWith("notice-1");
  });

  it("withdraws the warning when the form goes away with a live hold", async () => {
    const { unmount } = await holdSomething();
    mockCancelNotice.mockClear();

    unmount();

    expect(mockCancelNotice).toHaveBeenCalledWith("notice-1");
  });

  it("withdraws the warning when the countdown reaches expiry", async () => {
    mockCreateHold.mockResolvedValueOnce({
      ok: true,
      hold: { holdId: "hold-n2", expiresAt: new Date(Date.now() + 3_000).toISOString() },
    });
    const { result } = renderHook(() => useResourceHold({ ...defaultParams, resourceId: 100 }));

    await act(async () => {
      jest.advanceTimersByTime(2000);
    });
    mockCancelNotice.mockClear();

    await act(async () => {
      jest.advanceTimersByTime(4000);
    });

    expect(result.current.holdStatus).toBe("expired");
    expect(mockCancelNotice).toHaveBeenCalledWith("notice-1");
  });

  // The permission prompt can outlive the hold that raised it: the guest reads the dialog,
  // the selection changes underneath, and the id arrives for a hold that is already gone.
  it("withdraws a warning that only arrives after its hold has ended", async () => {
    let grantPrompt: (id: string) => void = () => {};
    mockScheduleNotice.mockImplementationOnce(
      () => new Promise<string>((resolve) => (grantPrompt = resolve))
    );

    mockCreateHold.mockResolvedValueOnce(heldHold("hold-n3"));
    const { unmount } = renderHook(() => useResourceHold({ ...defaultParams, resourceId: 100 }));
    await act(async () => {
      jest.advanceTimersByTime(2000);
    });

    unmount();
    mockCancelNotice.mockClear();

    await act(async () => {
      grantPrompt("late-notice");
    });

    expect(mockCancelNotice).toHaveBeenCalledWith("late-notice");
  });
});
