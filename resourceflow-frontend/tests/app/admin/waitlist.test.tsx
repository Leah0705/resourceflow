/**
 * @jest-environment jsdom
 */
import React from "react";
import { act, fireEvent, screen, waitFor, within } from "@testing-library/react-native";
import { Platform } from "react-native";
import WaitlistScreen, { BOARD_POLL_MS } from "@/app/admin/waitlist";
import * as venuesApi from "@/api/venues";
import * as waitlistApi from "@/api/waitlist";
import { renderWithProviders } from "@/tests/helpers/renderWithProviders";

jest.mock("expo-router", () => {
  const Screen = () => null;
  Screen.displayName = "Screen";
  return { Stack: { Screen } };
});
jest.mock("@/utils/haptics", () => ({
  haptics: { selection: jest.fn(), press: jest.fn(), outcome: jest.fn() },
}));
jest.mock("@/api/venues", () => ({ fetchVenues: jest.fn() }));
jest.mock("@/components/common/ConfirmModal", () => require("../../../jest-mocks/ConfirmModal"));
jest.mock("@/api/waitlist", () => ({
  getWaitlistBoard: jest.fn(),
  addWaitlistParty: jest.fn(),
  actOnWaitlistEntry: jest.fn(),
}));

const mockVenues = venuesApi.fetchVenues as jest.Mock;
const mockBoard = waitlistApi.getWaitlistBoard as jest.Mock;
const mockAdd = waitlistApi.addWaitlistParty as jest.Mock;
const mockAct = waitlistApi.actOnWaitlistEntry as jest.Mock;

const party = (over: Partial<waitlistApi.WaitlistEntry> = {}): waitlistApi.WaitlistEntry => ({
  id: 4,
  number: 7,
  name: "Ada",
  email: null,
  partySize: 2,
  status: "waiting",
  joinedAt: new Date().toISOString(),
  notifiedAt: null,
  partiesAhead: 0,
  estimatedWaitMinutes: 0,
  canAssignNow: true,
  skipsNumber: null,
  ...over,
});

const board = (entries: waitlistApi.WaitlistEntry[], acceptingGuests = true) => ({
  venueId: 1,
  acceptingGuests,
  entries,
});

beforeEach(() => {
  jest.clearAllMocks();
  localStorage.clear();
  mockVenues.mockResolvedValue([
    { id: 1, name: "Central Workspace" },
    { id: 2, name: "Harbour Studio" },
  ]);
  mockBoard.mockResolvedValue(board([party()]));
});

describe("admin waitlist", () => {
  it("opens on the first location and lists its queue", async () => {
    renderWithProviders(<WaitlistScreen />);

    // The first render in the file pays for loading every module, so it gets a longer window.
    expect(await screen.findByTestId("waitlist-row-4", {}, { timeout: 5000 })).toBeTruthy();
    expect(mockBoard).toHaveBeenCalledWith(1);
    expect(screen.getByTestId("waitlist-accepting")).toHaveTextContent(
      "Participants can join from the site right now."
    );
  });

  it("says when participants can't join from the site", async () => {
    mockBoard.mockResolvedValue(board([], false));
    renderWithProviders(<WaitlistScreen />);

    expect(await screen.findByText("No one is waiting")).toBeTruthy();
    expect(screen.getByTestId("waitlist-accepting")).toHaveTextContent(/only while/);
  });

  it("switches location", async () => {
    renderWithProviders(<WaitlistScreen />);
    await screen.findByTestId("waitlist-row-4");

    fireEvent.press(screen.getByRole("radio", { name: "Harbour Studio" }));

    await waitFor(() => expect(mockBoard).toHaveBeenLastCalledWith(2));
  });

  it("asks for a location first when there are none", async () => {
    mockVenues.mockResolvedValue([]);
    renderWithProviders(<WaitlistScreen />);

    expect(await screen.findByText("Add a location to start a waitlist.")).toBeTruthy();
  });

  it("reports a board that couldn't load", async () => {
    mockBoard.mockResolvedValue(null);
    renderWithProviders(<WaitlistScreen />);

    expect(await screen.findByText("Couldn't load the waitlist.")).toBeTruthy();
  });

  it("re-reads the board on a timer, so participants joining online appear", async () => {
    const interval = jest.spyOn(global, "setInterval");
    renderWithProviders(<WaitlistScreen />);
    await screen.findByTestId("waitlist-row-4");

    const poll = interval.mock.calls.filter(([, ms]) => ms === BOARD_POLL_MS).at(-1);
    await act(async () => {
      (poll![0] as () => void)();
    });

    expect(mockBoard).toHaveBeenCalledTimes(2);
    interval.mockRestore();
  });

  it("adds a party and clears the form", async () => {
    mockAdd.mockResolvedValue({ ok: true, value: party({ id: 5 }) });
    renderWithProviders(<WaitlistScreen />);
    await screen.findByTestId("waitlist-row-4");

    expect(screen.getByTestId("waitlist-add-submit")).toBeDisabled();
    fireEvent.changeText(screen.getByTestId("waitlist-add-name"), " Bo ");
    fireEvent.changeText(screen.getByTestId("waitlist-add-email"), "bad");
    expect(screen.getByTestId("waitlist-add-submit")).toBeDisabled();
    fireEvent.changeText(screen.getByTestId("waitlist-add-email"), "bo@example.com");
    fireEvent.press(screen.getByLabelText(/^Participants, /));
    fireEvent.press(screen.getByRole("option", { name: "4 participants" }));
    fireEvent.press(screen.getByTestId("waitlist-add-submit"));

    await waitFor(() =>
      expect(mockAdd).toHaveBeenCalledWith(1, { name: "Bo", partySize: 4, email: "bo@example.com" })
    );
    await waitFor(() => expect(screen.getByTestId("waitlist-add-name").props.value).toBe(""));
  });

  it("shows why an add was refused", async () => {
    mockAdd.mockResolvedValue({ ok: false, message: "No resource here can fit a party of 12." });
    renderWithProviders(<WaitlistScreen />);
    await screen.findByTestId("waitlist-row-4");

    fireEvent.changeText(screen.getByTestId("waitlist-add-name"), "Bo");
    fireEvent.press(screen.getByTestId("waitlist-add-submit"));

    expect(await screen.findByTestId("waitlist-error")).toHaveTextContent(
      "No resource here can fit a party of 12."
    );
    expect(mockAdd).toHaveBeenCalledWith(1, { name: "Bo", partySize: 2, email: undefined });
  });

  it("acts on a party and refreshes the board", async () => {
    mockAct.mockResolvedValue({ ok: true, value: null });
    renderWithProviders(<WaitlistScreen />);
    await screen.findByTestId("waitlist-row-4");

    fireEvent.press(screen.getByTestId("waitlist-assign-4"));

    await waitFor(() => expect(mockAct).toHaveBeenCalledWith(4, "assign"));
    await waitFor(() => expect(mockBoard).toHaveBeenCalledTimes(2));
  });

  it("removes a party only once the removal is confirmed", async () => {
    mockAct.mockResolvedValue({ ok: true, value: null });
    renderWithProviders(<WaitlistScreen />);
    await screen.findByTestId("waitlist-row-4");

    fireEvent.press(screen.getByTestId("waitlist-remove-4"));
    expect(screen.getByText("Remove Ada from the waitlist?")).toBeTruthy();
    expect(mockAct).not.toHaveBeenCalled();

    fireEvent.press(screen.getByText("Cancel"));
    expect(screen.queryByTestId("confirm-modal")).toBeNull();
    expect(mockAct).not.toHaveBeenCalled();

    fireEvent.press(screen.getByTestId("waitlist-remove-4"));
    fireEvent.press(within(screen.getByTestId("confirm-modal")).getByText("Remove"));

    await waitFor(() => expect(mockAct).toHaveBeenCalledWith(4, "remove"));
    expect(screen.queryByTestId("confirm-modal")).toBeNull();
  });

  it("shows why an action was refused", async () => {
    mockAct.mockResolvedValue({
      ok: false,
      message: "No free resource can accommodate this group right now.",
    });
    renderWithProviders(<WaitlistScreen />);
    await screen.findByTestId("waitlist-row-4");

    fireEvent.press(screen.getByTestId("waitlist-call-4"));

    expect(await screen.findByTestId("waitlist-error")).toHaveTextContent(
      "No free resource can accommodate this group right now."
    );
  });

  it("titles the native header off web", async () => {
    const original = Platform.OS;
    Object.defineProperty(Platform, "OS", { value: "ios", configurable: true });
    renderWithProviders(<WaitlistScreen />);

    expect(await screen.findByTestId("waitlist-row-4")).toBeTruthy();
    Object.defineProperty(Platform, "OS", { value: original, configurable: true });
  });
});
