/**
 * @jest-environment jsdom
 */
import React from "react";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react-native";
import { LocationCard } from "@/components/admin/settings/LocationCard";
import * as venuesApi from "@/api/venues";
import * as adminApi from "@/api/admin";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/api/venues", () => ({
  addSection: jest.fn(),
  uploadLocationImage: jest.fn(),
  deleteLocationImage: jest.fn(),
}));

jest.mock("@/api/admin", () => ({
  reorderSections: jest.fn(),
}));

jest.mock("@/context/BrandContext", () => {
  const brand = { primaryColor: "#0a7ea4", appName: "ResourceFlow" };
  return { useBrand: () => brand };
});

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

jest.mock("@/hooks/use-persisted-state", () => ({
  usePersistedState: (_key: string, defaultValue: unknown) => {
    const { useState } = require("react");
    return useState(defaultValue);
  },
}));

jest.mock("@/components/admin/settings/VenueInfoForm", () => ({
  VenueInfoForm: ({ venue }: { venue: { name: string } }) => {
    const { Text } = require("react-native");
    return <Text testID="venue-info-form">{venue.name}</Text>;
  },
}));

jest.mock("@/components/admin/settings/SectionBlock", () => ({
  SectionBlock: ({
    section,
    onSectionRenamed,
    onSectionDeleted,
    onResourceAdded,
    onResourceUpdated,
    onResourceDeleted,
    isFirst,
    isLast,
    onMoveUp,
    onMoveDown,
  }: {
    section: {
      id: number;
      name: string;
      resources: { id: number; name: string; capacity: number }[];
    };
    onSectionRenamed: (name: string) => void;
    onSectionDeleted: () => void;
    onResourceAdded: (t: { id: number; name: string; capacity: number }) => void;
    onResourceUpdated: (t: { id: number; name: string; capacity: number }) => void;
    onResourceDeleted: (id: number) => void;
    isFirst: boolean;
    isLast: boolean;
    onMoveUp: () => void;
    onMoveDown: () => void;
  }) => {
    const { View, Text, Pressable } = require("react-native");
    return (
      <View testID={`section-${section.name}`}>
        <Text>{section.name}</Text>
        <Pressable
          testID={`rename-section-${section.id}`}
          onPress={() => onSectionRenamed("Renamed Section")}
        >
          <Text>Rename</Text>
        </Pressable>
        <Pressable testID={`delete-section-${section.id}`} onPress={onSectionDeleted}>
          <Text>Delete Section</Text>
        </Pressable>
        <Pressable
          testID={`add-resource-${section.id}`}
          onPress={() => onResourceAdded({ id: 999, name: "New Resource", capacity: 2 })}
        >
          <Text>Add Resource</Text>
        </Pressable>
        <Pressable
          testID={`update-resource-${section.id}`}
          onPress={() =>
            onResourceUpdated({
              id: section.resources[0]?.id ?? 100,
              name: "Updated Resource",
              capacity: 6,
            })
          }
        >
          <Text>Update Resource</Text>
        </Pressable>
        <Pressable
          testID={`delete-resource-${section.id}`}
          onPress={() => onResourceDeleted(section.resources[0]?.id ?? 100)}
        >
          <Text>Delete Resource</Text>
        </Pressable>
        <Text testID={`is-first-${section.id}`}>{String(isFirst)}</Text>
        <Text testID={`is-last-${section.id}`}>{String(isLast)}</Text>
        <Pressable testID={`move-up-${section.id}`} onPress={onMoveUp}>
          <Text>Move Up</Text>
        </Pressable>
        <Pressable testID={`move-down-${section.id}`} onPress={onMoveDown}>
          <Text>Move Down</Text>
        </Pressable>
      </View>
    );
  },
}));

jest.mock("@/components/admin/settings/AddRow", () => ({
  AddRow: ({
    label,
    onAdd,
  }: {
    label: string;
    placeholder: string;
    onAdd: (name: string) => Promise<void>;
  }) => {
    const { Text, Pressable } = require("react-native");
    return (
      <Pressable testID="add-row-btn" onPress={() => onAdd("New Section")}>
        <Text>{label}</Text>
      </Pressable>
    );
  },
}));

const baseVenue = {
  id: 1,
  name: "Test Location",
  address: "123 Main St",
  openTime: "09:00",
  closeTime: "22:00",
  openDays: "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
  timezone: "America/New_York",
  imageUrl: null as string | null,
  sections: [
    {
      id: 10,
      name: "Lounge",
      resources: [
        { id: 100, name: "Resource 1", capacity: 4 },
        { id: 101, name: "Resource 2", capacity: 2 },
      ],
    },
    {
      id: 11,
      name: "Annex",
      resources: [],
    },
  ],
};

const baseProps = {
  venue: baseVenue,
  onSaved: jest.fn(),
  isDark: false,
  borderColor: "#ddd",
  mutedColor: "#888",
  cardBg: "#fff",
};

describe("LocationCard", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders location name", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getAllByText("Test Location").length).toBeGreaterThan(0);
  });

  it("shows section and resource counts as stats", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByText("Sections")).toBeTruthy();
    expect(screen.getByText("Resources")).toBeTruthy();
    expect(screen.getByText("Capacity")).toBeTruthy();
  });

  it("renders VenueInfoForm", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByTestId("venue-info-form")).toBeTruthy();
  });

  it("renders sections list", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByTestId("section-Lounge")).toBeTruthy();
    expect(screen.getByTestId("section-Annex")).toBeTruthy();
  });

  it("shows empty sections message when no sections", () => {
    render(<LocationCard {...baseProps} venue={{ ...baseVenue, sections: [] }} />);
    expect(screen.getByText("No sections yet.")).toBeTruthy();
  });

  it("shows Sections & Resources section header", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByText("Sections & Resources")).toBeTruthy();
  });

  it("shows Add Section button", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByText("Add Section")).toBeTruthy();
  });

  it("shows image section when no imageUrl", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByText("No image set")).toBeTruthy();
  });

  it("shows Upload image button when no imageUrl", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByText("Upload image")).toBeTruthy();
  });

  it("shows Change image and Remove buttons when imageUrl is set", () => {
    render(
      <LocationCard
        {...baseProps}
        venue={{ ...baseVenue, imageUrl: "http://example.com/img.jpg" }}
      />
    );
    expect(screen.getByText("Change image")).toBeTruthy();
    expect(screen.getByText("Remove")).toBeTruthy();
  });

  it("calls deleteLocationImage when Remove is pressed", async () => {
    (venuesApi.deleteLocationImage as jest.Mock).mockResolvedValue(true);
    render(
      <LocationCard
        {...baseProps}
        venue={{ ...baseVenue, imageUrl: "http://example.com/img.jpg" }}
      />
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Remove"));
    });
    expect(venuesApi.deleteLocationImage).toHaveBeenCalledWith(1);
  });

  it("calls onSaved with null imageUrl after removing image", async () => {
    (venuesApi.deleteLocationImage as jest.Mock).mockResolvedValue(true);
    render(
      <LocationCard
        {...baseProps}
        venue={{ ...baseVenue, imageUrl: "http://example.com/img.jpg" }}
      />
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Remove"));
    });
    expect(baseProps.onSaved).toHaveBeenCalledWith({ imageUrl: null });
  });

  it("shows Image removed message after removing image", async () => {
    (venuesApi.deleteLocationImage as jest.Mock).mockResolvedValue(true);
    render(
      <LocationCard
        {...baseProps}
        venue={{ ...baseVenue, imageUrl: "http://example.com/img.jpg" }}
      />
    );
    await act(async () => {
      fireEvent.press(screen.getByText("Remove"));
    });
    await waitFor(() => {
      expect(screen.getByText("Image removed.")).toBeTruthy();
    });
  });

  it("shows Uploading… during image upload", async () => {
    let resolve: (url: string) => void;
    (venuesApi.uploadLocationImage as jest.Mock).mockReturnValue(
      new Promise((r) => {
        resolve = r;
      })
    );
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [new File(["content"], "photo.jpg", { type: "image/jpeg" })],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<LocationCard {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload image"));
    });
    act(() => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(screen.getByText("Uploading…")).toBeTruthy();
    });
    await act(async () => {
      resolve!("http://example.com/new.jpg");
    });
  });

  it("calls uploadLocationImage when file is selected", async () => {
    (venuesApi.uploadLocationImage as jest.Mock).mockResolvedValue("http://example.com/new.jpg");
    const mockFile = new File(["content"], "photo.jpg", { type: "image/jpeg" });
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [mockFile],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<LocationCard {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload image"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(venuesApi.uploadLocationImage).toHaveBeenCalledWith(1, mockFile);
    });
  });

  it("calls onSaved with new imageUrl after successful upload", async () => {
    (venuesApi.uploadLocationImage as jest.Mock).mockResolvedValue("http://example.com/new.jpg");
    const mockFile = new File(["content"], "photo.jpg", { type: "image/jpeg" });
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [mockFile],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<LocationCard {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload image"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(baseProps.onSaved).toHaveBeenCalledWith({
        imageUrl: "http://example.com/new.jpg",
      });
    });
  });

  it("shows Image uploaded message after successful upload", async () => {
    (venuesApi.uploadLocationImage as jest.Mock).mockResolvedValue("http://example.com/new.jpg");
    const mockFile = new File(["content"], "photo.jpg", { type: "image/jpeg" });
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [mockFile],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<LocationCard {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload image"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(screen.getByText("Image uploaded.")).toBeTruthy();
    });
  });

  it("shows error when upload fails", async () => {
    (venuesApi.uploadLocationImage as jest.Mock).mockResolvedValue(null);
    const mockFile = new File(["content"], "photo.jpg", { type: "image/jpeg" });
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [mockFile],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<LocationCard {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload image"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(screen.getByText("Failed to upload image.")).toBeTruthy();
    });
  });

  it("shows size error when file is too large", async () => {
    const largeFile = new File(["x".repeat(3 * 1024 * 1024)], "big.jpg", { type: "image/jpeg" });
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [largeFile],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<LocationCard {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload image"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    await waitFor(() => {
      expect(screen.getByText("Image must be under 2 MB.")).toBeTruthy();
    });
    expect(venuesApi.uploadLocationImage).not.toHaveBeenCalled();
  });

  it("does nothing when no file is selected", async () => {
    const mockInput = {
      type: "",
      accept: "",
      onchange: null as ((e: Event) => void) | null,
      click: jest.fn(),
      files: [],
    };
    jest.spyOn(document, "createElement").mockReturnValueOnce(mockInput as unknown as HTMLElement);
    render(<LocationCard {...baseProps} />);
    act(() => {
      fireEvent.press(screen.getByText("Upload image"));
    });
    await act(async () => {
      mockInput.onchange?.({} as Event);
    });
    expect(venuesApi.uploadLocationImage).not.toHaveBeenCalled();
  });

  it("calls addSection when Add Section is pressed", async () => {
    (venuesApi.addSection as jest.Mock).mockResolvedValue({
      id: 99,
      name: "New Section",
    });
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("add-row-btn"));
    });
    expect(venuesApi.addSection).toHaveBeenCalledWith(1, "New Section");
  });

  it("calls onSaved with new section after adding", async () => {
    (venuesApi.addSection as jest.Mock).mockResolvedValue({
      id: 99,
      name: "New Section",
    });
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("add-row-btn"));
    });
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: expect.arrayContaining([
          expect.objectContaining({ id: 99, name: "New Section" }),
        ]),
      })
    );
  });

  it("does not call onSaved when addSection returns null", async () => {
    (venuesApi.addSection as jest.Mock).mockResolvedValue(null);
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("add-row-btn"));
    });
    expect(baseProps.onSaved).not.toHaveBeenCalled();
  });

  it("renders Location Image section label", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByText("Location Image")).toBeTruthy();
  });

  it("renders location without address", () => {
    render(
      <LocationCard
        {...baseProps}
        venue={{ ...baseVenue, address: undefined as unknown as string }}
      />
    );
    expect(screen.getAllByText("Test Location").length).toBeGreaterThan(0);
  });

  it("renders location without timezone", () => {
    render(
      <LocationCard
        {...baseProps}
        venue={{ ...baseVenue, timezone: undefined as unknown as string }}
      />
    );
    expect(screen.getAllByText("Test Location").length).toBeGreaterThan(0);
  });

  it("renders in dark mode without error", () => {
    render(<LocationCard {...baseProps} isDark />);
    expect(screen.getAllByText("Test Location").length).toBeGreaterThan(0);
  });

  it("calls onSaved with updated sections when section is renamed", () => {
    render(<LocationCard {...baseProps} />);
    fireEvent.press(screen.getByTestId("rename-section-10"));
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: expect.arrayContaining([
          expect.objectContaining({ id: 10, name: "Renamed Section" }),
        ]),
      })
    );
  });

  it("calls onSaved with filtered sections when section is deleted", () => {
    render(<LocationCard {...baseProps} />);
    fireEvent.press(screen.getByTestId("delete-section-10"));
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: expect.not.arrayContaining([expect.objectContaining({ id: 10 })]),
      })
    );
  });

  it("calls onSaved with added resource when resource is added to section", () => {
    render(<LocationCard {...baseProps} />);
    fireEvent.press(screen.getByTestId("add-resource-10"));
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: expect.arrayContaining([
          expect.objectContaining({
            id: 10,
            resources: expect.arrayContaining([
              expect.objectContaining({ id: 999, name: "New Resource" }),
            ]),
          }),
        ]),
      })
    );
  });

  it("calls onSaved with updated resource when resource is updated", () => {
    render(<LocationCard {...baseProps} />);
    fireEvent.press(screen.getByTestId("update-resource-10"));
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: expect.arrayContaining([
          expect.objectContaining({
            id: 10,
            resources: expect.arrayContaining([
              expect.objectContaining({ id: 100, name: "Updated Resource", capacity: 6 }),
            ]),
          }),
        ]),
      })
    );
  });

  it("calls onSaved with filtered resources when resource is deleted", () => {
    render(<LocationCard {...baseProps} />);
    fireEvent.press(screen.getByTestId("delete-resource-10"));
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: expect.arrayContaining([
          expect.objectContaining({
            id: 10,
            resources: expect.not.arrayContaining([expect.objectContaining({ id: 100 })]),
          }),
        ]),
      })
    );
  });

  // ── Reorder up/down move buttons ─────────────────────────────────────────

  it("marks the first section as isFirst and the last as isLast", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByTestId("is-first-10").props.children).toBe("true");
    expect(screen.getByTestId("is-last-10").props.children).toBe("false");
    expect(screen.getByTestId("is-first-11").props.children).toBe("false");
    expect(screen.getByTestId("is-last-11").props.children).toBe("true");
  });

  it("calls reorderSections and onSaved with swapped order when moving a section down", async () => {
    (adminApi.reorderSections as jest.Mock).mockResolvedValue(true);
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("move-down-10"));
    });
    expect(adminApi.reorderSections).toHaveBeenCalledWith(1, [11, 10]);
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: [expect.objectContaining({ id: 11 }), expect.objectContaining({ id: 10 })],
      })
    );
  });

  it("calls reorderSections and onSaved with swapped order when moving a section up", async () => {
    (adminApi.reorderSections as jest.Mock).mockResolvedValue(true);
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("move-up-11"));
    });
    expect(adminApi.reorderSections).toHaveBeenCalledWith(1, [11, 10]);
    expect(baseProps.onSaved).toHaveBeenCalledWith(
      expect.objectContaining({
        sections: [expect.objectContaining({ id: 11 }), expect.objectContaining({ id: 10 })],
      })
    );
  });

  it("does not call onSaved when reorderSections fails", async () => {
    (adminApi.reorderSections as jest.Mock).mockResolvedValue(false);
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("move-down-10"));
    });
    expect(adminApi.reorderSections).toHaveBeenCalled();
    expect(baseProps.onSaved).not.toHaveBeenCalled();
  });

  it("ignores a second move while the first reorder request is still in flight", async () => {
    // Regression guard: handleMove had no in-flight guard, so two rapid
    // move clicks before the first PATCH resolves both computed their swap from the same
    // stale `venue.sections` closure, letting the second overwrite the first.
    let resolveFirst!: (value: boolean) => void;
    (adminApi.reorderSections as jest.Mock).mockImplementation(
      () =>
        new Promise<boolean>((resolve) => {
          resolveFirst = resolve;
        })
    );
    render(<LocationCard {...baseProps} />);

    fireEvent.press(screen.getByTestId("move-down-10"));
    fireEvent.press(screen.getByTestId("move-up-11"));

    expect(adminApi.reorderSections).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveFirst(true);
    });

    expect(adminApi.reorderSections).toHaveBeenCalledTimes(1);
    expect(baseProps.onSaved).toHaveBeenCalledTimes(1);
  });

  it("does not call reorderSections when moving the first section up (out of bounds)", async () => {
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("move-up-10"));
    });
    expect(adminApi.reorderSections).not.toHaveBeenCalled();
    expect(baseProps.onSaved).not.toHaveBeenCalled();
  });

  it("does not call reorderSections when moving the last section down (out of bounds)", async () => {
    render(<LocationCard {...baseProps} />);
    await act(async () => {
      fireEvent.press(screen.getByTestId("move-down-11"));
    });
    expect(adminApi.reorderSections).not.toHaveBeenCalled();
    expect(baseProps.onSaved).not.toHaveBeenCalled();
  });

  it("does not render move buttons' bound checks incorrectly with a single section", async () => {
    const singleSectionVenue = {
      ...baseVenue,
      sections: [baseVenue.sections[0]],
    };
    render(<LocationCard {...baseProps} venue={singleSectionVenue} />);
    expect(screen.getByTestId("is-first-10").props.children).toBe("true");
    expect(screen.getByTestId("is-last-10").props.children).toBe("true");
    await act(async () => {
      fireEvent.press(screen.getByTestId("move-up-10"));
    });
    await act(async () => {
      fireEvent.press(screen.getByTestId("move-down-10"));
    });
    expect(adminApi.reorderSections).not.toHaveBeenCalled();
  });

  it("collapses the Location Image card when its header is pressed", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByText("Upload image")).toBeTruthy();
    fireEvent.press(screen.getByText("Location Image"));
    expect(screen.queryByText("Upload image")).toBeNull();
  });

  it("collapses the Sections & Resources card when its header is pressed", () => {
    render(<LocationCard {...baseProps} />);
    expect(screen.getByTestId("section-Lounge")).toBeTruthy();
    fireEvent.press(screen.getByText("Sections & Resources"));
    expect(screen.queryByTestId("section-Lounge")).toBeNull();
  });
});
