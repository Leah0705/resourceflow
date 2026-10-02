import React from "react";
import { screen } from "@testing-library/react-native";
import { DurationRulesField } from "@/components/admin/settings/DurationRulesField";
import { renderWithProviders } from "@/tests/helpers/renderWithProviders";

jest.mock("@/utils/haptics", () => ({
  haptics: { selection: jest.fn(), press: jest.fn(), outcome: jest.fn() },
}));

describe("DurationRulesField", () => {
  it("names each row's slot length by the participant count it starts at", () => {
    renderWithProviders(
      <DurationRulesField
        rules={[
          { minPartySize: 1, minutes: 60 },
          { minPartySize: 5, minutes: 120 },
        ]}
        onChange={jest.fn()}
        defaultMinutes={90}
        durationOptions={[
          { label: "60 min", value: 60 },
          { label: "120 min", value: 120 },
        ]}
        mutedColor="#666"
      />
    );

    expect(screen.getByLabelText(/^Slot length from 1, /)).toBeTruthy();
    expect(screen.getByLabelText(/^Slot length from 5, /)).toBeTruthy();
  });
});
