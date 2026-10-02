import type { TFunction } from "i18next";
import { fmtDateTime } from "@/utils/formatters";

/**
 * Shared a11y/highlight helpers for bookings-list rows — kept identical between
 * the wide-resource and mobile-card layouts so keyboard-focus state can't silently
 * drift between the two renderings.
 */

/**
 * Accessibility + testID props for a bookings-list row.
 * `testID: booking-row-${id}` is asserted by the screen-level integration tests,
 * so keep this stable.
 *
 * Rows are composed of many small text nodes (name, email, date, party size, resource). Left
 * unnamed, a screen reader reads every one as separate content inside a button with no
 * name, so `label` collapses the row into a single announcement.
 */
export function rowA11yProps(id: number, focusedRowId: number | null, label?: string) {
  return {
    testID: `booking-row-${id}`,
    accessibilityRole: "button" as const,
    accessibilityLabel: label,
    accessibilityState: { selected: id === focusedRowId },
  };
}

/** One-line spoken summary of a booking row. */
export function describeBookingRow(
  b: {
    customerName?: string | null;
    customerEmail: string;
    date: string;
    partySize: number;
    resourceName?: string | null;
  },
  t: TFunction
): string {
  const name = b.customerName ?? b.customerEmail;
  const when = fmtDateTime(new Date(b.date));
  const partySize = t("booking.form.partySize", { count: b.partySize });
  return b.resourceName
    ? t("admin.bookings.rowSummaryWithResource", {
        name,
        when,
        partySize,
        resource: b.resourceName,
      })
    : t("admin.bookings.rowSummary", { name, when, partySize });
}

/**
 * Returns `undefined` when the row isn't focused (ignored in RN style arrays).
 */
export function focusedRowHighlight(
  id: number,
  focusedRowId: number | null,
  primaryColor: string
): { backgroundColor: string } | undefined {
  return id === focusedRowId ? { backgroundColor: `${primaryColor}0D` } : undefined;
}
