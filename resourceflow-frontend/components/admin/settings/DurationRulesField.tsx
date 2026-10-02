import { View } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import Button from "@/components/common/Button";
import { ButtonRow } from "@/components/common/ButtonRow";
import Select, { type SelectOption } from "@/components/common/Select";
import type { DurationRuleDto } from "@/api/venues";
import { theme } from "@/theme/theme";
import { MAX_PARTY_SIZE, MIN_PARTY_SIZE } from "@/utils/partySizeOptions";
import { durationRuleRanges } from "@/utils/durationRules";
import { RowIconButton } from "./RowIconButton";
import { styles as settingsStyles } from "./settings.styles";
import { styles } from "./DurationRulesField.styles";

/**
 * Slot lengths by party size, under the default booking duration. Rows are kept in party-size
 * order, so each one's range reads off the row below it.
 */
export function DurationRulesField({
  rules,
  onChange,
  defaultMinutes,
  durationOptions,
  mutedColor,
}: {
  rules: DurationRuleDto[];
  onChange: (rules: DurationRuleDto[]) => void;
  defaultMinutes: number;
  durationOptions: SelectOption[];
  mutedColor: string;
}) {
  const { t } = useTranslation();
  const ranges = durationRuleRanges(rules);

  const partySizeOptions: SelectOption[] = [];
  for (let partySize = MIN_PARTY_SIZE; partySize <= MAX_PARTY_SIZE; partySize++) {
    partySizeOptions.push({
      value: partySize,
      label: t("admin.settings.venueInfo.durationRulesFrom", { count: partySize }),
    });
  }

  const sortedWith = (next: DurationRuleDto[]) =>
    [...next].sort((a, b) => a.minPartySize - b.minPartySize);

  const update = (index: number, patch: Partial<DurationRuleDto>) =>
    onChange(
      sortedWith(ranges.map((r, i) => (i === index ? { ...toRule(r), ...patch } : toRule(r))))
    );

  const remove = (index: number) => onChange(ranges.filter((_, i) => i !== index).map(toRule));

  const add = () => {
    const last = ranges[ranges.length - 1];
    onChange([
      ...ranges.map(toRule),
      {
        minPartySize: last ? Math.min(last.minPartySize + 1, MAX_PARTY_SIZE) : MIN_PARTY_SIZE,
        minutes: last?.minutes ?? defaultMinutes,
      },
    ]);
  };

  return (
    <View style={styles.field} testID="duration-rules">
      <ThemedText style={[settingsStyles.fieldLabel, { color: mutedColor }]}>
        {t("admin.settings.venueInfo.durationRulesLabel")}
      </ThemedText>
      {ranges.map((range, index) => (
        <View key={index} style={styles.row} testID={`duration-rule-row-${index}`}>
          <View style={styles.partySizeSelect}>
            <Select
              accessibilityLabel={t("admin.settings.venueInfo.durationRulesPartySizeLabel")}
              options={partySizeOptions}
              selectedValue={range.minPartySize}
              onSelect={(value) => update(index, { minPartySize: Number(value) })}
            />
          </View>
          <View style={styles.minutesSelect}>
            <Select
              accessibilityLabel={t("admin.settings.venueInfo.durationRulesMinutesLabel", {
                partySize: range.minPartySize,
              })}
              options={durationOptions}
              selectedValue={range.minutes}
              onSelect={(value) => update(index, { minutes: Number(value) })}
            />
          </View>
          <ThemedText style={[styles.range, { color: mutedColor }]}>
            {range.maxPartySize === null
              ? t("admin.settings.venueInfo.durationRulesRangeOpen", {
                  partySize: range.minPartySize,
                })
              : range.maxPartySize <= range.minPartySize
                ? t("admin.settings.venueInfo.durationRulesRangeOne", {
                    partySize: range.minPartySize,
                  })
                : t("admin.settings.venueInfo.durationRulesRange", {
                    min: range.minPartySize,
                    max: range.maxPartySize,
                  })}
          </ThemedText>
          <RowIconButton
            name="trash-outline"
            color={theme.colors.error}
            onPress={() => remove(index)}
            accessibilityLabel={t("admin.settings.venueInfo.durationRulesRemove", {
              partySize: range.minPartySize,
            })}
          />
        </View>
      ))}
      <ThemedText style={[settingsStyles.fieldHint, { color: mutedColor }]}>
        {t("admin.settings.venueInfo.durationRulesHint")}
      </ThemedText>
      <ButtonRow align="start">
        <Button size="md" icon="add" onPress={add}>
          {t("admin.settings.venueInfo.durationRulesAdd")}
        </Button>
      </ButtonRow>
    </View>
  );
}

const toRule = ({ minPartySize, minutes }: DurationRuleDto): DurationRuleDto => ({
  minPartySize,
  minutes,
});
