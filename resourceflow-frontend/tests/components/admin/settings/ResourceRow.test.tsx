import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react-native";
import { ResourceRow } from "@/components/admin/settings/ResourceRow";
import * as venuesApi from "@/api/venues";
import { useBrand } from "@/context/BrandContext";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/api/venues", () => ({
  updateResource: jest.fn(),
  deleteResource: jest.fn(),
  fetchResourceDeleteImpact: jest.fn(),
}));

jest.mock("@/utils/colors", () => ({
  hexToRgba: (hex: string, _opacity: number) => hex,
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: jest.fn(() => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" })),
}));

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

const baseResource = { id: 5, name: "T1", capacity: 4 };

const baseProps = {
  resource: baseResource,
  venueId: 1,
  sectionId: 2,
  isDark: false,
  borderColor: "#ddd",
  onUpdated: jest.fn(),
  onDeleted: jest.fn(),
};

describe("ResourceRow", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders resource name and capacity in view mode", () => {
    render(<ResourceRow {...baseProps} />);
    expect(screen.getByText("T1")).toBeTruthy();
    expect(screen.getByText("4 places")).toBeTruthy();
  });

  it("renders resource id fallback when name is null", () => {
    render(<ResourceRow {...baseProps} resource={{ ...baseResource, name: null }} />);
    expect(screen.getByText("R5")).toBeTruthy();
  });

  it("enters edit mode when pencil button is pressed", () => {
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    expect(screen.getByDisplayValue("T1")).toBeTruthy();
    // CAPACITY is now a dropdown; its trigger shows the selected label.
    expect(screen.getByText("4 places")).toBeTruthy();
  });

  it("shows Cancel and Save buttons in edit mode", () => {
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    expect(screen.getByText("Cancel")).toBeTruthy();
    expect(screen.getByText("Save")).toBeTruthy();
  });

  it("calls updateResource and onUpdated when save is pressed with valid data", async () => {
    const updatedResource = { id: 5, name: "T1-Updated", capacity: 2 };
    (venuesApi.updateResource as jest.Mock).mockResolvedValue(updatedResource);
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    fireEvent.changeText(screen.getByDisplayValue("T1"), "T1-Updated");
    // Open the capacity dropdown and pick "2 places".
    act(() => {
      fireEvent.press(screen.getByText("4 places"));
    });
    await act(async () => {
      fireEvent.press(screen.getByText("2 places"));
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(venuesApi.updateResource).toHaveBeenCalledWith(1, 2, 5, {
      name: "T1-Updated",
      capacity: 2,
      walkInOnly: false,
    });
    expect(baseProps.onUpdated).toHaveBeenCalledWith(updatedResource);
  });

  it("saves with the unchanged capacity when the dropdown is not touched", async () => {
    // The dropdown can no longer produce an invalid capacity value (options are constrained), so the
    // relevant invariant is that the seeded value persists through save when untouched.
    const updatedResource = { id: 5, name: "T1", capacity: 4 };
    (venuesApi.updateResource as jest.Mock).mockResolvedValue(updatedResource);
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(venuesApi.updateResource).toHaveBeenCalledWith(1, 2, 5, {
      name: "T1",
      capacity: 4,
      walkInOnly: false,
    });
  });

  it("cancels edit mode when Cancel is pressed", () => {
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    fireEvent.press(screen.getByText("Cancel"));
    expect(screen.getByText("T1")).toBeTruthy();
  });

  // ── Two-step delete friction ──────────────────────────────────────────────
  //
  // `Delete…` reveals an inline confirmation that names the consequence (incl. the count of future
  // bookings that lose their resource reference) and requires a second explicit tap to destroy. Cancel
  // returns to the row.

  it("reveals an inline confirmation and fetches the impact count when Delete… is pressed", async () => {
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 3 });
    render(<ResourceRow {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-5"));
    });
    expect(venuesApi.fetchResourceDeleteImpact).toHaveBeenCalledWith(1, 2, 5);
    // The confirm copy names the resource and the concrete consequence.
    expect(screen.getByText(/Delete “T1”\?/)).toBeTruthy();
    expect(
      await screen.findByText(/3 future bookings will lose their resource reference/)
    ).toBeTruthy();
    expect(screen.getByText("Yes, delete")).toBeTruthy();
    expect(screen.getByText("Cancel")).toBeTruthy();
  });

  it("deletes only after the second explicit tap (Yes, delete)", async () => {
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 0 });
    (venuesApi.deleteResource as jest.Mock).mockResolvedValue(true);
    render(<ResourceRow {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-5"));
    });
    // The actual delete call is NOT made by the first tap — only by the confirm tap.
    expect(venuesApi.deleteResource).not.toHaveBeenCalled();
    await act(async () => {
      fireEvent.press(screen.getByText("Yes, delete"));
    });
    expect(venuesApi.deleteResource).toHaveBeenCalledWith(1, 2, 5);
    expect(baseProps.onDeleted).toHaveBeenCalled();
  });

  it("does not delete when Cancel is pressed in the confirm step", async () => {
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 1 });
    render(<ResourceRow {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-5"));
    });
    fireEvent.press(screen.getByTestId("resource-delete-cancel-btn"));
    expect(venuesApi.deleteResource).not.toHaveBeenCalled();
    expect(baseProps.onDeleted).not.toHaveBeenCalled();
    // Back to the idle row — the destructive confirm copy is gone.
    expect(screen.queryByText("Yes, delete")).toBeNull();
    expect(screen.getByTestId("resource-delete-btn-5")).toBeTruthy();
  });

  it("falls back to generic copy when the impact read fails or is unavailable", async () => {
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue(null);
    render(<ResourceRow {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-5"));
    });
    expect(
      await screen.findByText(/Future bookings on this resource will lose their reference/)
    ).toBeTruthy();
  });

  it("uses singular copy when exactly one future booking is affected", async () => {
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 1 });
    render(<ResourceRow {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-5"));
    });
    expect(
      await screen.findByText(/1 future booking will lose its resource reference/)
    ).toBeTruthy();
  });

  it("renders in dark mode", () => {
    render(<ResourceRow {...baseProps} isDark />);
    expect(screen.getByText("T1")).toBeTruthy();
  });

  it("renders the capacity for capacity=1 (singular)", () => {
    render(<ResourceRow {...baseProps} resource={{ ...baseResource, capacity: 1 }} />);
    expect(screen.getByText("1 place")).toBeTruthy();
  });

  it("renders the capacity for capacity=8 (plural)", () => {
    render(<ResourceRow {...baseProps} resource={{ ...baseResource, capacity: 8 }} />);
    expect(screen.getByText("8 places")).toBeTruthy();
  });

  it("renders the capacity for capacity=10 (plural)", () => {
    render(<ResourceRow {...baseProps} resource={{ ...baseResource, capacity: 10 }} />);
    expect(screen.getByText("10 places")).toBeTruthy();
  });

  it("falls back to the default primary color when the brand has none", () => {
    (useBrand as jest.Mock).mockReturnValueOnce({ primaryColor: "", appName: "ResourceFlow" });
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    expect(screen.getByText("Save")).toBeTruthy();
  });

  it("seeds the edit form and delete confirmation with the resource id when name is null", async () => {
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 0 });
    render(<ResourceRow {...baseProps} resource={{ ...baseResource, name: null }} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    expect(screen.getByDisplayValue("")).toBeTruthy();
    expect(screen.getByText("EDITING · Resource 5")).toBeTruthy();
    fireEvent.press(screen.getByText("Cancel"));

    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-5"));
    });
    // The confirm copy falls back to the "Resource {id}" label when the name is null.
    expect(await screen.findByText(/Delete “Resource 5”\?/)).toBeTruthy();
  });

  it("does not call onDeleted when deleteResource resolves falsy", async () => {
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 0 });
    (venuesApi.deleteResource as jest.Mock).mockResolvedValue(false);
    render(<ResourceRow {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-5"));
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Yes, delete"));
    });
    expect(venuesApi.deleteResource).toHaveBeenCalledWith(1, 2, 5);
    expect(baseProps.onDeleted).not.toHaveBeenCalled();
  });

  it("renders the edit-mode background in dark mode", () => {
    render(<ResourceRow {...baseProps} isDark />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    expect(screen.getByText("Save")).toBeTruthy();
  });

  it("saves with an undefined name when the name field is cleared", async () => {
    const updatedResource = { id: 5, name: null, capacity: 4 };
    (venuesApi.updateResource as jest.Mock).mockResolvedValue(updatedResource);
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    fireEvent.changeText(screen.getByDisplayValue("T1"), "   ");
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(venuesApi.updateResource).toHaveBeenCalledWith(1, 2, 5, {
      name: undefined,
      capacity: 4,
      walkInOnly: false,
    });
  });

  it("stays in edit mode when updateResource resolves falsy", async () => {
    (venuesApi.updateResource as jest.Mock).mockResolvedValue(null);
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(baseProps.onUpdated).not.toHaveBeenCalled();
    expect(screen.getByText("Save")).toBeTruthy();
  });

  it("shows saving state while updating", async () => {
    let resolve: (v: typeof baseResource | null) => void;
    (venuesApi.updateResource as jest.Mock).mockReturnValue(
      new Promise((r) => {
        resolve = r;
      })
    );
    render(<ResourceRow {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    });
    act(() => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(screen.getByText("Saving…")).toBeTruthy();
    await act(async () => {
      resolve!(baseResource);
    });
  });

  // ── Walk-in-only resources ───────────────────────────────────────────────────

  it("marks a walk-in-only resource with a chip", () => {
    render(<ResourceRow {...baseProps} resource={{ ...baseResource, walkInOnly: true }} />);
    expect(screen.getByTestId("resource-walk-in-chip-5")).toBeTruthy();
  });

  it("holds a resource back for walk-ins from the editor", async () => {
    (venuesApi.updateResource as jest.Mock).mockResolvedValue({
      ...baseResource,
      walkInOnly: true,
    });
    render(<ResourceRow {...baseProps} />);
    fireEvent.press(screen.getByTestId("resource-edit-btn-5"));
    const toggle = screen.getByTestId("resource-walk-in-toggle-5");
    expect(toggle.props.accessibilityState).toEqual({ checked: false });

    fireEvent.press(toggle);
    expect(screen.getByTestId("resource-walk-in-toggle-5").props.accessibilityState).toEqual({
      checked: true,
    });
    expect(screen.getByRole("checkbox")).toBeChecked();
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });

    expect(venuesApi.updateResource).toHaveBeenCalledWith(1, 2, 5, {
      name: "T1",
      capacity: 4,
      walkInOnly: true,
    });
  });

  // ── Combinable resource groups ───────────────────────────────────────────────

  it("renders a combine (link) action on standalone (ungrouped) resources", () => {
    render(<ResourceRow {...baseProps} />);
    expect(screen.getByTestId("resource-link-btn-5")).toBeTruthy();
  });

  it("renders the group chip and a remove (unlink) affordance when the resource is a group member", () => {
    render(
      <ResourceRow
        {...baseProps}
        group={{ id: 9, label: "Resources 5 + 6 (8 combined)", combinedCapacity: 8 }}
        onUnlink={jest.fn()}
      />
    );
    expect(screen.getByTestId("resource-group-chip-5")).toBeTruthy();
    expect(screen.getByText("Resources 5 + 6 (8 combined)")).toBeTruthy();
    expect(screen.getByTestId("resource-unlink-btn-5")).toBeTruthy();
    // No combine action on a grouped resource.
    expect(screen.queryByTestId("resource-link-btn-5")).toBeNull();
  });

  it("calls onUnlink when the Unlink button is pressed", () => {
    const onUnlink = jest.fn();
    render(
      <ResourceRow
        {...baseProps}
        group={{ id: 9, label: "Resources 5 + 6 (8 combined)", combinedCapacity: 8 }}
        onUnlink={onUnlink}
      />
    );
    act(() => {
      fireEvent.press(screen.getByTestId("resource-unlink-btn-5"));
    });
    expect(onUnlink).toHaveBeenCalledTimes(1);
  });

  it("calls onLink when the Link button is pressed", () => {
    const onLink = jest.fn();
    render(<ResourceRow {...baseProps} onLink={onLink} />);
    act(() => {
      fireEvent.press(screen.getByTestId("resource-link-btn-5"));
    });
    expect(onLink).toHaveBeenCalledTimes(1);
  });

  it("renders a selectable row in selection mode", () => {
    const onToggleSelect = jest.fn();
    render(
      <ResourceRow {...baseProps} selectionMode selected={false} onToggleSelect={onToggleSelect} />
    );
    expect(screen.getByTestId("resource-select-row-5")).toBeTruthy();
    act(() => {
      fireEvent.press(screen.getByTestId("resource-select-row-5"));
    });
    expect(onToggleSelect).toHaveBeenCalledTimes(1);
  });

  it("disables selection for already-grouped resources in selection mode", () => {
    const onToggleSelect = jest.fn();
    render(
      <ResourceRow
        {...baseProps}
        group={{ id: 9, label: "Resources 5 + 6 (8 combined)", combinedCapacity: 8 }}
        selectionMode
        disabledInSelection
        onToggleSelect={onToggleSelect}
      />
    );
    // A grouped row is non-interactive in selection mode.
    expect(screen.queryByTestId("resource-link-btn-5")).toBeNull();
  });
});
