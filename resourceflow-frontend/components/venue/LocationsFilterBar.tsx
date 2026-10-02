import { View } from "react-native";
import { useTranslation } from "react-i18next";
import i18n from "@/i18n";
import { ThemedText } from "@/components/themed-text";
import Select, { type SelectOption } from "@/components/common/Select";
import DatePicker from "@/components/common/DatePicker";
import { useAppTheme } from "@/hooks/use-app-theme";
import { fmtDateString } from "@/utils/formatters";
import { styles } from "./LocationsFilterBar.styles";

/**
 * The local half-day the Locations list is filtered to. Mirrors `TimeSlotDto.category`
 * plus an "All" escape hatch, so the page-level bar and the in-drawer
 * `PopularTimesPicker` speak the same vocabulary.
 */
export type TimeWindow = "AM" | "PM" | "All";

function timeWindowOptions(compact: boolean): SelectOption[] {
  const all = compact ? i18n.t("venue.filterBar.all") : i18n.t("venue.filterBar.allTimes");
  return [
    { label: all, value: "All" },
    { label: "AM", value: "AM" },
    { label: "PM", value: "PM" },
  ];
}

function partySizeOptions(compact: boolean): SelectOption[] {
  return [...Array(10).keys()].map((i) => ({
    label: compact ? String(i + 1) : i18n.t("venue.filterBar.guestCount", { count: i + 1 }),
    value: i + 1,
  }));
}

export function formatBarDate(date: string, today: string, compact: boolean): string {
  const isToday = date === today;
  if (compact) return isToday ? i18n.t("venue.filterBar.today") : shortDate(date);
  return isToday ? i18n.t("venue.filterBar.todayDate", { date: shortDate(date) }) : shortDate(date);
}

function shortDate(date: string): string {
  return fmtDateString(date);
}

/** Page-level party/date/time-of-day bar for the Locations list. */
export default function LocationsFilterBar({
  partySize,
  onPartySizeChange,
  date,
  onDateChange,
  today,
  timeWindow,
  onTimeWindowChange,
  summary,
  compact,
  raised,
}: {
  partySize: number;
  onPartySizeChange: (partySize: number) => void;
  date: string;
  onDateChange: (date: string) => void;
  /** Today's date in the brand's timezone, for the "Today, …" trigger label. */
  today: string;
  timeWindow: TimeWindow;
  onTimeWindowChange: (timeWindow: TimeWindow) => void;
  /** e.g. "2 of 3 locations have resources". Hidden on the compact bar. */
  summary?: string | null;
  compact?: boolean;
  /** Set while the bar is pinned, where it floats over the list rather than slot in it. */
  raised?: boolean;
}) {
  const { colors } = useAppTheme();
  const { t } = useTranslation();

  return (
    <View
      testID="locations-filter-bar"
      style={[
        styles.bar,
        compact && styles.barCompact,
        { backgroundColor: colors.card },
        raised && styles.barRaised,
      ]}
    >
      <View
        testID="filter-control-party-size"
        style={compact ? styles.controlCompactPartySize : styles.controlPartySize}
      >
        <Select
          icon="people-outline"
          accessibilityLabel={t("venue.filterBar.numberOfGuests")}
          options={partySizeOptions(!!compact)}
          selectedValue={partySize}
          onSelect={(v) => onPartySizeChange(v as number)}
        />
      </View>

      <View style={compact ? styles.controlCompactWide : styles.controlDate}>
        <DatePicker
          icon={compact ? undefined : "calendar-outline"}
          triggerLabel={formatBarDate(date, today, !!compact)}
          selectedDate={date}
          onSelect={onDateChange}
        />
      </View>

      <View style={compact ? styles.controlCompactWide : styles.controlTimeWindow}>
        <Select
          icon={compact ? undefined : "time-outline"}
          accessibilityLabel={t("venue.filterBar.timeOfDay")}
          options={timeWindowOptions(!!compact)}
          selectedValue={timeWindow}
          onSelect={(v) => onTimeWindowChange(v as TimeWindow)}
        />
      </View>

      {!compact && summary ? (
        <ThemedText
          style={[styles.summary, { color: colors.muted }]}
          role="status"
          accessibilityLiveRegion="polite"
        >
          {summary}
        </ThemedText>
      ) : null}
    </View>
  );
}
