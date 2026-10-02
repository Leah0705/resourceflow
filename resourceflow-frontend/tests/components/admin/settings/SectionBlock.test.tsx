import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react-native";
import { SectionBlock } from "@/components/admin/settings/SectionBlock";
import * as venuesApi from "@/api/venues";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/api/venues", () => ({
  updateSection: jest.fn(),
  deleteSection: jest.fn(),
  addResource: jest.fn(),
  fetchSectionDeleteImpact: jest.fn(),
  createResourceGroup: jest.fn(),
  updateResourceGroup: jest.fn(),
  deleteResourceGroup: jest.fn(),
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" }),
}));

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

const mockSection = {
  id: 1,
  name: "Lounge",
  resources: [
    { id: 10, name: "T1", capacity: 4 },
    { id: 11, name: "T2", capacity: 2 },
  ],
};

const baseProps = {
  section: mockSection,
  venueId: 42,
  isDark: false,
  borderColor: "#ddd",
  mutedColor: "#888",
  groups: [] as venuesApi.ResourceGroupDto[],
  onSectionRenamed: jest.fn(),
  onSectionDeleted: jest.fn(),
  onResourceAdded: jest.fn(),
  onResourceUpdated: jest.fn(),
  onResourceDeleted: jest.fn(),
  onGroupsChanged: jest.fn(),
  isFirst: false,
  isLast: false,
  onMoveUp: jest.fn(),
  onMoveDown: jest.fn(),
};

describe("SectionBlock", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders section name", () => {
    render(<SectionBlock {...baseProps} />);
    expect(screen.getByText("Lounge")).toBeTruthy();
  });

  it("shows resource count and total capacity", () => {
    render(<SectionBlock {...baseProps} />);
    expect(screen.getByText("2 resources · 6 places")).toBeTruthy();
  });

  it("renders empty note when no resources", () => {
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    expect(screen.getByText("No resources yet.")).toBeTruthy();
  });

  it("shows Edit button for the section", () => {
    render(<SectionBlock {...baseProps} />);
    expect(screen.getByTestId("section-edit-btn")).toBeTruthy();
  });

  it("switches to editing mode when Edit is pressed", () => {
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByTestId("section-edit-btn"));
    expect(screen.getByDisplayValue("Lounge")).toBeTruthy();
    expect(screen.getByText("Save")).toBeTruthy();
  });

  it("calls updateSection and onSectionRenamed when save is pressed", async () => {
    (venuesApi.updateSection as jest.Mock).mockResolvedValue({
      id: 1,
      name: "Annex",
      resources: [],
    });
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByTestId("section-edit-btn"));
    fireEvent.changeText(screen.getByDisplayValue("Lounge"), "Annex");
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(venuesApi.updateSection).toHaveBeenCalledWith(42, 1, "Annex");
    expect(baseProps.onSectionRenamed).toHaveBeenCalledWith("Annex");
  });

  it("does not save when draft is empty", async () => {
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByTestId("section-edit-btn"));
    fireEvent.changeText(screen.getByDisplayValue("Lounge"), "");
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(venuesApi.updateSection).not.toHaveBeenCalled();
  });

  it("cancels edit mode", () => {
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    fireEvent.press(screen.getByTestId("section-edit-btn"));
    expect(screen.getByText("Save")).toBeTruthy();
    act(() => {
      fireEvent.press(screen.getByText("Cancel"));
    });
    expect(screen.getByText("Lounge")).toBeTruthy();
  });

  // ── Two-step delete friction ──────────────────────────────────────────────
  //
  // `Delete…` reveals an inline confirmation naming the section and the consequence (incl. the count
  // of future bookings that would lose their reference); a second explicit tap destroys. Cancel
  // returns to the header.

  it("reveals an inline confirmation and fetches the impact count when Delete… is pressed", async () => {
    (venuesApi.fetchSectionDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 2 });
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-delete-btn"));
    });
    expect(venuesApi.fetchSectionDeleteImpact).toHaveBeenCalledWith(42, 1);
    expect(await screen.findByText(/Delete section “Lounge” and all its resources\?/)).toBeTruthy();
    expect(screen.getByText(/2 future bookings affected/)).toBeTruthy();
    expect(screen.getByText("Yes, delete")).toBeTruthy();
  });

  it("deletes only after the second explicit tap (Yes, delete)", async () => {
    (venuesApi.fetchSectionDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 0 });
    (venuesApi.deleteSection as jest.Mock).mockResolvedValue(true);
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-delete-btn"));
    });
    expect(venuesApi.deleteSection).not.toHaveBeenCalled();
    await act(async () => {
      fireEvent.press(screen.getByText("Yes, delete"));
    });
    expect(venuesApi.deleteSection).toHaveBeenCalledWith(42, 1);
    expect(baseProps.onSectionDeleted).toHaveBeenCalled();
  });

  it("does not delete when Cancel is pressed in the confirm step", async () => {
    (venuesApi.fetchSectionDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 1 });
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-delete-btn"));
    });
    fireEvent.press(screen.getByTestId("section-delete-cancel-btn"));
    expect(venuesApi.deleteSection).not.toHaveBeenCalled();
    expect(baseProps.onSectionDeleted).not.toHaveBeenCalled();
    expect(screen.queryByText("Yes, delete")).toBeNull();
  });

  it("falls back to generic copy when the impact read fails or is unavailable", async () => {
    (venuesApi.fetchSectionDeleteImpact as jest.Mock).mockResolvedValue(null);
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-delete-btn"));
    });
    expect(
      await screen.findByText(/Future bookings in this section will lose their reference/)
    ).toBeTruthy();
  });

  it("uses singular copy when exactly one future booking is affected", async () => {
    (venuesApi.fetchSectionDeleteImpact as jest.Mock).mockResolvedValue({ bookings: 1 });
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-delete-btn"));
    });
    expect(await screen.findByText(/1 future booking affected/)).toBeTruthy();
  });

  it("renders Add Resource button", () => {
    render(<SectionBlock {...baseProps} />);
    expect(screen.getByText("Add Resource")).toBeTruthy();
  });

  it("opens AddRow form, submits, and calls onResourceAdded", async () => {
    const newResource = { id: 20, name: "T3", capacity: 4 };
    (venuesApi.addResource as jest.Mock).mockResolvedValue(newResource);

    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByText("Add Resource"));

    fireEvent.changeText(screen.getByPlaceholderText("Resource name (e.g. T1, Studio 1)"), "T3");
    // Capacity is now a dropdown — open it (placeholder "Capacity") and pick "4 places". The option list
    // renders inside the modal; a resource tile subtitle may also show "4 places", so pick the modal
    // option (the last match, since the modal appends after the tiles).
    fireEvent.press(screen.getByText("Capacity"));
    await act(async () => {
      fireEvent.press(screen.getAllByText("4 places").pop()!);
    });

    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });

    expect(venuesApi.addResource).toHaveBeenCalledWith(42, 1, { name: "T3", capacity: 4 });
    expect(baseProps.onResourceAdded).toHaveBeenCalledWith(newResource);
  });

  it("uses default capacity 2 when the capacity dropdown is left untouched", async () => {
    const newResource = { id: 21, name: "T4", capacity: 2 };
    (venuesApi.addResource as jest.Mock).mockResolvedValue(newResource);

    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByText("Add Resource"));
    fireEvent.changeText(screen.getByPlaceholderText("Resource name (e.g. T1, Studio 1)"), "T4");

    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });

    expect(venuesApi.addResource).toHaveBeenCalledWith(42, 1, { name: "T4", capacity: 2 });
  });

  it("renders in dark mode", () => {
    render(<SectionBlock {...baseProps} isDark />);
    expect(screen.getByText("Lounge")).toBeTruthy();
  });

  it("shows 'No resources yet.' when section has no resources", () => {
    render(<SectionBlock {...baseProps} section={{ ...mockSection, resources: [] }} />);
    expect(screen.getByText("No resources yet.")).toBeTruthy();
  });

  it("does not call onSectionRenamed when updateSection returns null", async () => {
    (venuesApi.updateSection as jest.Mock).mockResolvedValue(null);
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByTestId("section-edit-btn"));
    fireEvent.changeText(screen.getByDisplayValue("Lounge"), "Updated");
    await act(async () => {
      fireEvent.press(screen.getByText("Save"));
    });
    expect(venuesApi.updateSection).toHaveBeenCalled();
    expect(baseProps.onSectionRenamed).not.toHaveBeenCalled();
  });

  it("calls addResource and onResourceAdded when AddRow form is submitted", async () => {
    const newResource = { id: 20, name: "T3", capacity: 4 };
    (venuesApi.addResource as jest.Mock).mockResolvedValue(newResource);
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByText("Add Resource"));
    fireEvent.changeText(screen.getByPlaceholderText("Resource name (e.g. T1, Studio 1)"), "T3");
    // Capacity dropdown — pick "4 places" (last match = the modal option; a resource tile may also show it).
    fireEvent.press(screen.getByText("Capacity"));
    await act(async () => {
      fireEvent.press(screen.getAllByText("4 places").pop()!);
    });
    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });
    expect(venuesApi.addResource).toHaveBeenCalledWith(
      42,
      1,
      expect.objectContaining({ name: "T3", capacity: 4 })
    );
    expect(baseProps.onResourceAdded).toHaveBeenCalledWith(newResource);
  });

  it("does not call onResourceAdded when addResource returns null", async () => {
    (venuesApi.addResource as jest.Mock).mockResolvedValue(null);
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByText("Add Resource"));
    fireEvent.changeText(screen.getByPlaceholderText("Resource name (e.g. T1, Studio 1)"), "T4");
    // Capacity left at default (2) — no selection needed.
    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });
    expect(venuesApi.addResource).toHaveBeenCalled();
    expect(baseProps.onResourceAdded).not.toHaveBeenCalled();
  });

  it("uses default capacity of 2 when the capacity dropdown is left untouched", async () => {
    const newResource = { id: 21, name: "T5", capacity: 2 };
    (venuesApi.addResource as jest.Mock).mockResolvedValue(newResource);
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByText("Add Resource"));
    fireEvent.changeText(screen.getByPlaceholderText("Resource name (e.g. T1, Studio 1)"), "T5");
    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });
    expect(venuesApi.addResource).toHaveBeenCalledWith(42, 1, { name: "T5", capacity: 2 });
  });

  // ── Reorder up/down move buttons ─────────────────────────────────────────

  it("renders move-up and move-down buttons", () => {
    render(<SectionBlock {...baseProps} />);
    expect(screen.getByTestId("section-move-up-btn")).toBeTruthy();
    expect(screen.getByTestId("section-move-down-btn")).toBeTruthy();
  });

  it("calls onMoveUp when move-up is pressed", () => {
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByTestId("section-move-up-btn"));
    expect(baseProps.onMoveUp).toHaveBeenCalled();
  });

  it("calls onMoveDown when move-down is pressed", () => {
    render(<SectionBlock {...baseProps} />);
    fireEvent.press(screen.getByTestId("section-move-down-btn"));
    expect(baseProps.onMoveDown).toHaveBeenCalled();
  });

  it("disables move-up when isFirst is true", () => {
    render(<SectionBlock {...baseProps} isFirst />);
    expect(screen.getByTestId("section-move-up-btn").props.accessibilityState?.disabled).toBe(true);
  });

  it("disables move-down when isLast is true", () => {
    render(<SectionBlock {...baseProps} isLast />);
    expect(screen.getByTestId("section-move-down-btn").props.accessibilityState?.disabled).toBe(
      true
    );
  });

  it("does not call onMoveUp when disabled at first position", () => {
    render(<SectionBlock {...baseProps} isFirst />);
    fireEvent.press(screen.getByTestId("section-move-up-btn"));
    expect(baseProps.onMoveUp).not.toHaveBeenCalled();
  });

  it("does not call onMoveDown when disabled at last position", () => {
    render(<SectionBlock {...baseProps} isLast />);
    fireEvent.press(screen.getByTestId("section-move-down-btn"));
    expect(baseProps.onMoveDown).not.toHaveBeenCalled();
  });

  // ── Combinable resource groups ───────────────────────────────────────────────

  it("renders a group count in the section header when groups exist", () => {
    const groups: venuesApi.ResourceGroupDto[] = [
      {
        id: 1,
        name: null,
        combinedCapacity: 6,
        members: [
          { id: 10, name: "T1", capacity: 4 },
          { id: 11, name: "T2", capacity: 2 },
        ],
      },
    ];
    render(<SectionBlock {...baseProps} groups={groups} />);
    expect(screen.getByText(/2 resources · 6 places · 1 combinable group/)).toBeTruthy();
  });

  it("enters selection mode and creates a group when Combine is pressed", async () => {
    const created: venuesApi.ResourceGroupDto = {
      id: 1,
      name: null,
      combinedCapacity: 6,
      members: [
        { id: 10, name: "T1", capacity: 4 },
        { id: 11, name: "T2", capacity: 2 },
      ],
    };
    (venuesApi.createResourceGroup as jest.Mock).mockResolvedValue(created);
    const onGroupsChanged = jest.fn();
    render(<SectionBlock {...baseProps} groups={[]} onGroupsChanged={onGroupsChanged} />);

    // Tap Link on T1 to enter selection mode.
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-link-btn-10"));
    });
    // Select T2.
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-select-row-11"));
    });
    // Combine.
    await act(async () => {
      fireEvent.press(screen.getByTestId("section-combine-btn"));
    });

    expect(venuesApi.createResourceGroup).toHaveBeenCalledWith(42, {
      members: expect.arrayContaining([10, 11]),
      combinedCapacity: 6,
    });
    expect(onGroupsChanged).toHaveBeenCalledWith([created]);
  });

  it("dissolves a group via Unlink when down to one member", async () => {
    const groups: venuesApi.ResourceGroupDto[] = [
      {
        id: 1,
        name: null,
        combinedCapacity: 6,
        members: [
          { id: 10, name: "T1", capacity: 4 },
          { id: 11, name: "T2", capacity: 2 },
        ],
      },
    ];
    (venuesApi.deleteResourceGroup as jest.Mock).mockResolvedValue(true);
    const onGroupsChanged = jest.fn();
    render(<SectionBlock {...baseProps} groups={groups} onGroupsChanged={onGroupsChanged} />);

    // Unlink T10 → group drops to one member → dissolve.
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-unlink-btn-10"));
    });

    expect(venuesApi.deleteResourceGroup).toHaveBeenCalledWith(42, 1);
    expect(onGroupsChanged).toHaveBeenCalledWith([]);
  });

  it("edits combined capacity via the group edit affordance", async () => {
    const groups: venuesApi.ResourceGroupDto[] = [
      {
        id: 1,
        name: null,
        combinedCapacity: 6,
        members: [
          { id: 10, name: "T1", capacity: 4 },
          { id: 11, name: "T2", capacity: 2 },
        ],
      },
    ];
    const updated: venuesApi.ResourceGroupDto = { ...groups[0], combinedCapacity: 5 };
    (venuesApi.updateResourceGroup as jest.Mock).mockResolvedValue(updated);
    const onGroupsChanged = jest.fn();
    render(<SectionBlock {...baseProps} groups={groups} onGroupsChanged={onGroupsChanged} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("group-edit-btn-1"));
    });
    // Combined capacity is a dropdown seeded with the current value (6), offering only the window the
    // server accepts for a 4-place + 2-place pair: more than the largest member (4) up to their sum (6).
    fireEvent.press(screen.getByText("6 places"));
    expect(screen.queryByText("8 places")).toBeNull();
    await act(async () => {
      fireEvent.press(screen.getByText("5 places"));
    });
    await act(async () => {
      fireEvent.press(screen.getByTestId("group-save-combined-btn"));
    });

    expect(venuesApi.updateResourceGroup).toHaveBeenCalledWith(42, 1, {
      name: null,
      members: [10, 11],
      combinedCapacity: 5,
    });
    expect(onGroupsChanged).toHaveBeenCalledWith([updated]);
  });
});
