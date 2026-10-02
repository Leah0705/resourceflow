import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react-native";
import { SectionBlock } from "@/components/admin/settings/SectionBlock";
import * as venuesApi from "@/api/venues";

jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

jest.mock("@/api/venues", () => ({
  updateSection: jest.fn(),
  deleteSection: jest.fn(),
  addResource: jest.fn(),
  deleteResource: jest.fn(),
  fetchSectionDeleteImpact: jest.fn(),
  fetchResourceDeleteImpact: jest.fn(),
  createResourceGroup: jest.fn(),
  updateResourceGroup: jest.fn(),
  deleteResourceGroup: jest.fn(),
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" }),
}));

jest.mock("@/hooks/use-color-scheme", () => ({ useColorScheme: () => "light" }));

/**
 * Unlinking a member from a group of three or more shrinks the group rather than dissolving it.
 * That path has to re-clamp `combinedCapacity`: the departing resource's capacity is gone, so a figure
 * the admin set earlier can now exceed what the remaining resources actually fit, and the server
 * rejects anything outside (largest member, sum of members]. Getting the clamp wrong here means
 * the group silently keeps advertising capacity it can't fit.
 */
const baseProps = {
  section: {
    id: 1,
    name: "Lounge",
    resources: [
      { id: 10, name: "T1", capacity: 4 },
      { id: 11, name: "T2", capacity: 2 },
      { id: 12, name: "T3", capacity: 2 },
    ],
  },
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

const threeMemberGroup = (combinedCapacity: number): venuesApi.ResourceGroupDto => ({
  id: 1,
  name: "Window run",
  combinedCapacity,
  members: [
    { id: 10, name: "T1", capacity: 4 },
    { id: 11, name: "T2", capacity: 2 },
    { id: 12, name: "T3", capacity: 2 },
  ],
});

describe("SectionBlock unlink from a group with more than two members", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("shrinks the group instead of dissolving it", async () => {
    const groups = [threeMemberGroup(8)];
    const updated = { ...groups[0], members: groups[0].members.slice(1), combinedCapacity: 4 };
    (venuesApi.updateResourceGroup as jest.Mock).mockResolvedValue(updated);
    const onGroupsChanged = jest.fn();
    render(<SectionBlock {...baseProps} groups={groups} onGroupsChanged={onGroupsChanged} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-unlink-btn-10"));
    });

    expect(venuesApi.deleteResourceGroup).not.toHaveBeenCalled();
    expect(venuesApi.updateResourceGroup).toHaveBeenCalledWith(42, 1, {
      name: "Window run",
      members: [11, 12],
      // T2 + T3 remain (2 + 2), so the accepted window is (2, 4]. The stored 8 clamps to 4.
      combinedCapacity: 4,
    });
    expect(onGroupsChanged).toHaveBeenCalledWith([updated]);
  });

  it("keeps a combined capacity that is still inside the new window", async () => {
    // Removing T2 leaves T1 (4) + T3 (2) → window (4, 6]. The stored 5 is still valid.
    const groups = [threeMemberGroup(5)];
    const updated = { ...groups[0], combinedCapacity: 5 };
    (venuesApi.updateResourceGroup as jest.Mock).mockResolvedValue(updated);
    render(<SectionBlock {...baseProps} groups={groups} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-unlink-btn-11"));
    });

    expect(venuesApi.updateResourceGroup).toHaveBeenCalledWith(
      42,
      1,
      expect.objectContaining({ members: [10, 12], combinedCapacity: 5 })
    );
  });

  it("raises a combined capacity that has fallen below the new floor", async () => {
    // Removing T3 leaves T1 (4) + T2 (2) → window (4, 6]. A stored 3 is below the floor.
    const groups = [threeMemberGroup(3)];
    (venuesApi.updateResourceGroup as jest.Mock).mockResolvedValue(threeMemberGroup(5));
    render(<SectionBlock {...baseProps} groups={groups} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-unlink-btn-12"));
    });

    expect(venuesApi.updateResourceGroup).toHaveBeenCalledWith(
      42,
      1,
      expect.objectContaining({ members: [10, 11], combinedCapacity: 5 })
    );
  });

  it("leaves the group list untouched when the update call fails", async () => {
    const groups = [threeMemberGroup(8)];
    (venuesApi.updateResourceGroup as jest.Mock).mockResolvedValue(null);
    const onGroupsChanged = jest.fn();
    render(<SectionBlock {...baseProps} groups={groups} onGroupsChanged={onGroupsChanged} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-unlink-btn-10"));
    });

    expect(venuesApi.updateResourceGroup).toHaveBeenCalled();
    expect(onGroupsChanged).not.toHaveBeenCalled();
  });

  it("closes the combined-capacity editor without saving when Cancel is pressed", async () => {
    const groups = [threeMemberGroup(8)];
    render(<SectionBlock {...baseProps} groups={groups} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("group-edit-btn-1"));
    });
    expect(screen.getByTestId("group-save-combined-btn")).toBeTruthy();

    await act(async () => {
      fireEvent.press(screen.getByText("Cancel"));
    });

    expect(screen.queryByTestId("group-save-combined-btn")).toBeNull();
    expect(venuesApi.updateResourceGroup).not.toHaveBeenCalled();
  });

  it("reports the deleted resource id up to the parent", async () => {
    const onResourceDeleted = jest.fn();
    (venuesApi.fetchSectionDeleteImpact as jest.Mock).mockResolvedValue(null);
    (venuesApi.fetchResourceDeleteImpact as jest.Mock).mockResolvedValue(null);
    (venuesApi.deleteResource as jest.Mock).mockResolvedValue(true);
    render(<SectionBlock {...baseProps} onResourceDeleted={onResourceDeleted} />);

    // ResourceRow owns the confirm flow; the block only forwards the id, which is what's asserted here.
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-btn-11"));
    });
    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-delete-confirm-btn"));
    });

    expect(onResourceDeleted).toHaveBeenCalledWith(11);
  });

  it("leaves the group list untouched when a dissolve call fails", async () => {
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
    (venuesApi.deleteResourceGroup as jest.Mock).mockResolvedValue(false);
    const onGroupsChanged = jest.fn();
    render(<SectionBlock {...baseProps} groups={groups} onGroupsChanged={onGroupsChanged} />);

    await act(async () => {
      fireEvent.press(screen.getByTestId("resource-unlink-btn-10"));
    });

    expect(venuesApi.deleteResourceGroup).toHaveBeenCalledWith(42, 1);
    expect(onGroupsChanged).not.toHaveBeenCalled();
  });
});
