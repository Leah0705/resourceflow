import React, { useState } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react-native";
import JoinWaitlistForm from "@/components/waitlist/JoinWaitlistForm";
import { getWaitlistQuote, joinWaitlist, setWaitlistPush } from "@/api/waitlist";
import { canRegisterForReminders, registerForReminders } from "@/services/pushRegistration";

jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));
jest.mock("@/hooks/use-color-scheme", () => ({ useColorScheme: () => "light" }));
jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" }),
}));
jest.mock("@/context/LocaleContext", () => ({ useLocale: () => ({ locale: "fr" }) }));

const onJoined = jest.fn();

/** The page owns party size, as LocationsScreen does. */
function Form() {
  const [partySize, setPartySize] = useState(2);
  return (
    <JoinWaitlistForm
      venueId={3}
      partySize={partySize}
      onPartySizeChange={setPartySize}
      onJoined={onJoined}
    />
  );
}

jest.mock("@/api/waitlist", () => ({
  getWaitlistQuote: jest.fn(),
  joinWaitlist: jest.fn(),
  setWaitlistPush: jest.fn(),
}));
jest.mock("@/services/pushRegistration", () => ({
  canRegisterForReminders: jest.fn(),
  registerForReminders: jest.fn(),
}));

const mockQuote = getWaitlistQuote as jest.Mock;
const mockJoin = joinWaitlist as jest.Mock;
const mockSetPush = setWaitlistPush as jest.Mock;
const mockCanPush = canRegisterForReminders as jest.Mock;
const mockRegister = registerForReminders as jest.Mock;

const open = {
  venueId: 3,
  acceptingGuests: true,
  partiesWaiting: 2,
  estimatedWaitMinutes: 25,
};

beforeEach(() => {
  jest.clearAllMocks();
  mockQuote.mockResolvedValue(open);
  mockCanPush.mockReturnValue(false);
});

describe("JoinWaitlistForm", () => {
  it("quotes the wait before the participant commits", async () => {
    render(<Form />);

    expect(await screen.findByText("About 25 min wait")).toBeTruthy();
    expect(screen.getByText("2 groups waiting")).toBeTruthy();
    expect(mockQuote).toHaveBeenCalledWith(3, 2);
  });

  it("says a resource is free now at a zero wait", async () => {
    mockQuote.mockResolvedValue({ ...open, estimatedWaitMinutes: 0, partiesWaiting: 1 });
    render(<Form />);

    expect(await screen.findByText("A resource is free now")).toBeTruthy();
    expect(screen.getByText("1 group waiting")).toBeTruthy();
  });

  it("holds the join back for a party no resource can fit", async () => {
    mockQuote.mockResolvedValue({ ...open, estimatedWaitMinutes: null });
    render(<Form />);

    expect(await screen.findByText("No resource fits a group that size")).toBeTruthy();
    fireEvent.changeText(screen.getByLabelText("Full name"), "Ada");
    expect(screen.getByTestId("waitlist-join-submit")).toBeDisabled();
  });

  it("says the queue is closed instead of offering the form", async () => {
    mockQuote.mockResolvedValue({ ...open, acceptingGuests: false });
    render(<Form />);

    expect(await screen.findByTestId("waitlist-closed")).toBeTruthy();
    expect(screen.queryByTestId("waitlist-join-submit")).toBeNull();
  });

  it("reports a quote that couldn't load", async () => {
    mockQuote.mockResolvedValue(null);
    render(<Form />);

    expect(await screen.findByText("Couldn't load the waitlist. Please try again.")).toBeTruthy();
  });

  it("requotes when the participant count changes", async () => {
    render(<Form />);
    await screen.findByTestId("waitlist-quote");

    fireEvent.press(screen.getByLabelText("One more participant"));

    await waitFor(() => expect(mockQuote).toHaveBeenLastCalledWith(3, 3));
  });

  it("ignores a quote that arrives after the participant count has moved on", async () => {
    let resolveFirst: (q: unknown) => void = () => {};
    mockQuote
      .mockReturnValueOnce(new Promise((resolve) => (resolveFirst = resolve)))
      .mockResolvedValueOnce({ ...open, estimatedWaitMinutes: 40 });
    const view = render(<Form />);

    view.unmount();
    resolveFirst({ ...open, estimatedWaitMinutes: 5 });
    render(<Form />);

    expect(await screen.findByText("About 40 min wait")).toBeTruthy();
    expect(screen.queryByText("About 5 min wait")).toBeNull();
  });

  it("needs a name, and a valid email only if one is given", async () => {
    render(<Form />);
    await screen.findByTestId("waitlist-quote");
    const submit = () => screen.getByTestId("waitlist-join-submit");

    expect(submit()).toBeDisabled();
    fireEvent.changeText(screen.getByLabelText("Full name"), "Ada");
    expect(submit()).not.toBeDisabled();
    fireEvent.changeText(screen.getByLabelText("Email address"), "nope");
    expect(submit()).toBeDisabled();
    fireEvent.changeText(screen.getByLabelText("Email address"), "ada@example.com");
    expect(submit()).not.toBeDisabled();
  });

  it("joins and hands back the participant's ticket", async () => {
    mockJoin.mockResolvedValue({ ok: true, value: { ref: "abc234" } });
    render(<Form />);
    await screen.findByTestId("waitlist-quote");

    fireEvent.changeText(screen.getByLabelText("Full name"), "  Ada ");
    fireEvent.press(screen.getByTestId("waitlist-join-submit"));

    await waitFor(() => expect(onJoined).toHaveBeenCalledWith("abc234"));
    expect(mockJoin).toHaveBeenCalledWith(3, {
      name: "Ada",
      partySize: 2,
      email: undefined,
      locale: "fr",
    });
  });

  it("attaches the opted-in device to the new ticket", async () => {
    const device = { channel: "expo", endpoint: "ExponentPushToken[abc]" };
    mockCanPush.mockReturnValue(true);
    mockRegister.mockResolvedValue({ status: "registered", registration: device });
    mockSetPush.mockResolvedValue(true);
    mockJoin.mockResolvedValue({ ok: true, value: { ref: "abc234" } });
    render(<Form />);
    await screen.findByTestId("waitlist-quote");

    fireEvent.changeText(screen.getByLabelText("Full name"), "Ada");
    fireEvent.press(screen.getByTestId("waitlist-push-btn"));
    await screen.findByText("Notifications on");
    fireEvent.press(screen.getByTestId("waitlist-join-submit"));

    await waitFor(() => expect(onJoined).toHaveBeenCalledWith("abc234"));
    expect(mockSetPush).toHaveBeenCalledWith("abc234", device);
  });

  it("shows the server's reason when the join is refused", async () => {
    mockJoin.mockResolvedValue({ ok: false, message: "This location is closed right now." });
    render(<Form />);
    await screen.findByTestId("waitlist-quote");

    fireEvent.changeText(screen.getByLabelText("Full name"), "Ada");
    fireEvent.changeText(screen.getByLabelText("Email address"), " ada@example.com ");
    fireEvent.press(screen.getByTestId("waitlist-join-submit"));

    expect(await screen.findByTestId("waitlist-join-error")).toHaveTextContent(
      "This location is closed right now."
    );
    expect(mockJoin).toHaveBeenCalledWith(3, expect.objectContaining({ email: "ada@example.com" }));
    expect(onJoined).not.toHaveBeenCalled();
  });
});
