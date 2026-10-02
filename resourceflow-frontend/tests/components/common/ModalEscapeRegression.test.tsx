// Regression test for Escape handling in modals.
//
// Every modal in this codebase is built on React Native's
// <Modal onRequestClose={...}>, and react-native-web's Modal implementation
// already attaches a document-level `keyup` listener that calls
// onRequestClose() when e.key === 'Escape' (see
// node_modules/react-native-web/dist/exports/Modal/ModalContent.js).
//
// A Jest test cannot exercise that DOM-level behavior directly. This repo's
// Jest config (resourceflow-frontend/package.json -> "jest") uses "jest-expo"'s
// default (native) preset, which resolves `react-native` to the real
// react-native package and renders through `react-test-renderer`, not
// react-dom. Only `jest-expo`'s dedicated web preset (unused here) aliases
// `react-native` to `react-native-web` and renders via jsdom+react-dom. Forcing
// that alias in a single test (`jest.mock("react-native", () =>
// require("react-native-web"))`) fails: react-test-renderer's host config
// cannot mount react-native-web's DOM-based output, so a simulated `keyup`
// never reaches react-native-web's listener. Escape-to-close is therefore
// covered by the Playwright E2E specs.
//
// This test verifies the part that is this codebase's responsibility and is
// observable via react-test-renderer: every existing modal wires
// `onRequestClose` to its close handler, which is the sole precondition for
// react-native-web's built-in Escape handling to fire in the browser.
import React from "react";
import TestRenderer from "react-test-renderer";
import { Modal } from "react-native";
import ConfirmModal from "@/components/common/ConfirmModal";
import AlertModal from "@/components/common/AlertModal";
import { BrandProvider } from "@/context/BrandContext";

global.fetch = jest.fn(() =>
  Promise.resolve({
    ok: true,
    json: () => Promise.resolve({ appName: "ResourceFlow", primaryColor: "#0a7ea4" }),
  })
) as jest.Mock;

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

describe("Existing modals wire onRequestClose (precondition for web Escape-to-close)", () => {
  it("wires ConfirmModal's onRequestClose to its onCancel handler", () => {
    const onCancel = jest.fn();
    let tree!: TestRenderer.ReactTestRenderer;
    TestRenderer.act(() => {
      tree = TestRenderer.create(
        <BrandProvider>
          <ConfirmModal
            visible
            message="Cancel booking?"
            onConfirm={jest.fn()}
            onCancel={onCancel}
          />
        </BrandProvider>
      );
    });

    const modal = tree.root.findByType(Modal);
    expect(modal.props.onRequestClose).toBe(onCancel);

    modal.props.onRequestClose();
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("wires AlertModal's onRequestClose to its onClose handler", () => {
    const onClose = jest.fn();
    let tree!: TestRenderer.ReactTestRenderer;
    TestRenderer.act(() => {
      tree = TestRenderer.create(
        <BrandProvider>
          <AlertModal visible message="Something happened" onClose={onClose} />
        </BrandProvider>
      );
    });

    const modal = tree.root.findByType(Modal);
    expect(modal.props.onRequestClose).toBe(onClose);

    modal.props.onRequestClose();
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
