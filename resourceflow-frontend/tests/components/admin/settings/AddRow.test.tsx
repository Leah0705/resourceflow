import React from "react";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react-native";
import { AddRow } from "@/components/admin/settings/AddRow";

jest.mock("@expo/vector-icons", () => ({
  Ionicons: () => null,
}));

jest.mock("@/context/BrandContext", () => ({
  useBrand: () => ({ primaryColor: "#0a7ea4", appName: "ResourceFlow" }),
}));

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

describe("AddRow", () => {
  const onAdd = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders the add button with label", () => {
    render(<AddRow label="Add Item" onAdd={onAdd} />);
    expect(screen.getByText("Add Item")).toBeTruthy();
  });

  it("opens the form when button is pressed", () => {
    render(<AddRow label="Add Item" placeholder="Item name" onAdd={onAdd} />);
    fireEvent.press(screen.getByText("Add Item"));
    expect(screen.getByPlaceholderText("Item name")).toBeTruthy();
  });

  it("uses default placeholder 'Name' when none provided", () => {
    render(<AddRow label="Add Item" onAdd={onAdd} />);
    fireEvent.press(screen.getByText("Add Item"));
    expect(screen.getByPlaceholderText("Name")).toBeTruthy();
  });

  it("shows extra input when extraPlaceholder is provided", () => {
    render(
      <AddRow
        label="Add Resource"
        placeholder="Resource name"
        extraPlaceholder="Capacity"
        onAdd={onAdd}
      />
    );
    fireEvent.press(screen.getByText("Add Resource"));
    expect(screen.getByPlaceholderText("Resource name")).toBeTruthy();
    expect(screen.getByPlaceholderText("Capacity")).toBeTruthy();
  });

  it("does not show extra input without extraPlaceholder", () => {
    render(<AddRow label="Add Item" placeholder="Item name" onAdd={onAdd} />);
    fireEvent.press(screen.getByText("Add Item"));
    expect(screen.queryByPlaceholderText("Capacity")).toBeNull();
  });

  it("calls onAdd with trimmed name when Add is pressed", async () => {
    onAdd.mockResolvedValue(undefined);
    render(<AddRow label="Add Item" placeholder="Item name" onAdd={onAdd} />);
    fireEvent.press(screen.getByText("Add Item"));
    fireEvent.changeText(screen.getByPlaceholderText("Item name"), "  My Item  ");
    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });
    expect(onAdd).toHaveBeenCalledWith("My Item", undefined);
  });

  it("calls onAdd with name and extra when both provided", async () => {
    onAdd.mockResolvedValue(undefined);
    render(
      <AddRow
        label="Add Resource"
        placeholder="Resource name"
        extraPlaceholder="Capacity"
        onAdd={onAdd}
      />
    );
    fireEvent.press(screen.getByText("Add Resource"));
    fireEvent.changeText(screen.getByPlaceholderText("Resource name"), "T1");
    fireEvent.changeText(screen.getByPlaceholderText("Capacity"), "4");
    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });
    expect(onAdd).toHaveBeenCalledWith("T1", "4");
  });

  it("does not call onAdd when name is empty", async () => {
    render(<AddRow label="Add Item" placeholder="Item name" onAdd={onAdd} />);
    fireEvent.press(screen.getByText("Add Item"));
    await act(async () => {
      fireEvent.press(screen.getByText("Add"));
    });
    expect(onAdd).not.toHaveBeenCalled();
  });

  it("resets and closes form when close button is pressed", async () => {
    render(<AddRow label="Add Item" placeholder="Item name" onAdd={onAdd} />);
    fireEvent.press(screen.getByText("Add Item"));
    fireEvent.changeText(screen.getByPlaceholderText("Item name"), "Something");
    // The close button is the last accessible Pressable in the form (Ionicons close-outline)
    const accessible = screen.UNSAFE_getAllByProps({ accessible: true });
    fireEvent.press(accessible[accessible.length - 1]);
    await waitFor(() => {
      expect(screen.getByText("Add Item")).toBeTruthy();
    });
    expect(screen.queryByPlaceholderText("Item name")).toBeNull();
  });

  it("shows 'Adding…' text while saving", async () => {
    let resolveAdd: () => void;
    const slowAdd = jest.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveAdd = resolve;
        })
    );
    render(<AddRow label="Add Item" placeholder="Item name" onAdd={slowAdd} />);
    fireEvent.press(screen.getByText("Add Item"));
    fireEvent.changeText(screen.getByPlaceholderText("Item name"), "Test");
    act(() => {
      fireEvent.press(screen.getByText("Add"));
    });
    expect(screen.getByText("Adding…")).toBeTruthy();
    await act(async () => {
      resolveAdd!();
    });
  });
});
