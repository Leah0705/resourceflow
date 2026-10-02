import { VenueDto } from "@/api/venues";
import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import Button from "../common/Button";
import { ThemedText } from "../themed-text";
import { Platform, View, ActivityIndicator, useWindowDimensions } from "react-native";
import { useResourceHold } from "./useResourceHold";
import HoldStatusBanner from "./HoldStatusBanner";
import { useBookingDock, useHasBookingDock, usePublishBookingDock } from "./BookingDockContext";
import PopularTimesPicker from "./PopularTimesPicker";
import { useAppTheme } from "@/hooks/use-app-theme";
import { getNowInTimezone, formatCurrentTimeInTimezone, isViewerInTimezone } from "@/utils/date";
import { isValidEmail } from "@/utils/validation";
import { groupDisplayName } from "@/utils/resourceGroups";
import { confirm } from "@/utils/confirm";
import { getHoursForDate } from "@/utils/openingHours";
import { getVenueDate } from "@/utils/venueTime";
import { isWalkInOnlyOnDay, isWalkInOnlyOnDate, walkInDaysLabel } from "@/utils/walkIn";
import WalkInNotice from "./WalkInNotice";
import LargePartyNotice from "./LargePartyNotice";
import LargePartyNoticeModal from "./LargePartyNoticeModal";
import { MOBILE_BREAKPOINT } from "@/constants/breakpoints";
import { suggestDate, suggestTime } from "./bookingSuggestions";
import { useBookingAvailability } from "./useBookingAvailability";
import { groupSelectValue, useBookingPlacement } from "./useBookingPlacement";
import {
  DateField,
  EmailField,
  GuestsField,
  NameField,
  RequestsField,
  SectionField,
  ResourceField,
  TimeField,
} from "./BookingFormFields";
import {
  BookingFormDrawerLayout,
  BookingFormInlineLayout,
  type BookingFormParts,
} from "./BookingFormLayouts";
import { styles } from "./BookingForm.styles";

const isWeb = Platform.OS === "web";

export interface BookingFormData {
  customerEmail: string;
  customerName: string;
  partySize: number;
  /** null when "Any section" is selected (server auto-assigns the resource). */
  resourceId: number | null;
  /** null when "Any section" is selected. */
  sectionId: number | null;
  /**
   * Combinable-resource group id when the guest selected a combined group; null otherwise.
   * Mutually exclusive with resourceId.
   */
  resourceGroupId: number | null;
  date: string;
  time: string;
  holdId: string | null;
  specialRequests: string;
}

/** Booking form used inline on a location page or as the body of the booking drawer. */
export type BookingFormLayout = "inline" | "drawer";

export default function BookingForm({
  venue,
  onSubmit,
  onRefresh,
  initialTime,
  initialPartySize,
  layout = "inline",
  partySize: controlledPartySize,
  onPartySizeChange,
  date: controlledDate,
  onDateChange,
  onJoinWaitlist,
}: {
  venue: VenueDto;
  onSubmit: (data: BookingFormData) => Promise<void> | void;
  onRefresh?: () => void;
  initialTime?: string;
  initialPartySize?: number;
  layout?: BookingFormLayout;
  /** When set, party size is owned by the caller — changing it here reports back up. */
  partySize?: number;
  onPartySizeChange?: (partySize: number) => void;
  /** When set, the date is owned by the caller — changing it here reports back up. */
  date?: string;
  onDateChange?: (date: string) => void;
  /** Offered on the walk-in notice when today is a walk-in day. */
  onJoinWaitlist?: () => void;
}) {
  const { t } = useTranslation();
  const { colors, primaryColor: PRIMARY } = useAppTheme();
  const { width } = useWindowDimensions();
  const isDrawer = layout === "drawer";
  const isTwoColumn = isWeb && width >= MOBILE_BREAKPOINT && !isDrawer;

  const [customerEmail, setCustomerEmail] = useState("");
  const [customerName, setCustomerName] = useState("");
  const [specialRequests, setSpecialRequests] = useState("");
  const [partySizeState, setPartySize] = useState(initialPartySize ?? 2);
  const partySize = controlledPartySize ?? partySizeState;
  const changePartySize = (value: number) =>
    onPartySizeChange ? onPartySizeChange(value) : setPartySize(value);
  const [submitting, setSubmitting] = useState(false);
  const [placementOpen, setPlacementOpen] = useState(false);
  const [largePartyNoticeOpen, setLargePartyNoticeOpen] = useState(false);

  const timezone = venue.timezone || "UTC";

  const [dateState, setDate] = useState<string>(() => suggestDate(venue, timezone));
  const date = controlledDate ?? dateState;
  const changeDate = (value: string) => (onDateChange ? onDateChange(value) : setDate(value));
  const [time, setTime] = useState<string>(() => initialTime ?? suggestTime(venue, timezone));

  const openDaysList = venue.openDays?.split(",").map(Number) ?? [1, 2, 3, 4, 5, 6, 7];
  const selectedJsDay = date ? new Date(date + "T12:00:00").getDay() : -1;
  const selectedIsoDay = selectedJsDay === 0 ? 7 : selectedJsDay;
  const isClosedDay = date ? !openDaysList.includes(selectedIsoDay) : false;
  const isWalkInDay = date ? isWalkInOnlyOnDate(venue, date) : false;
  // Walk-in days stay in the date picker, greyed out and labelled: the location is open on
  // them, so "you can't book, but you can walk in" is the useful thing to say. Dropping them
  // outright also left the picker with nothing to show for a walk-in-only location, and left
  // the trigger unable to name the day the guest was already on.
  const walkInIsoDays = openDaysList.filter((day) => isWalkInOnlyOnDay(venue, day));
  const bookingBlocked = isClosedDay || isWalkInDay;

  const [venueCurrentTime, setVenueCurrentTime] = useState(() =>
    formatCurrentTimeInTimezone(timezone)
  );
  useEffect(() => {
    /* istanbul ignore next */
    const id = setInterval(
      () => setVenueCurrentTime(formatCurrentTimeInTimezone(timezone)),
      60_000
    );
    return () => clearInterval(id);
  }, [timezone]);

  const { availabilitySlots, loadingAvailability } = useBookingAvailability({
    venue,
    date,
    partySize,
    time,
    onTimeCorrected: setTime,
  });

  const currentSlot = availabilitySlots.find((s) => s.time === time);

  const {
    allResources,
    allGroups,
    sectionId,
    setSectionId,
    resourceId,
    resourceGroupId,
    selectPlacementUnit,
    isAutoAssign,
    sectionOptions,
    resourceOptions,
    maxResourceCapacity,
    partyTooLarge,
  } = useBookingPlacement({ venue, partySize, currentSlot });

  // The resource pick is the only thing that flows between these two hooks, and it flows one way:
  // useResourceHold releases or replaces its hold whenever these params change, so nothing has to
  // reach back into it. The call order is therefore pinned by the resourceId/isAutoAssign reads
  // below rather than by effect timing — swapping the hooks fails to compile instead of silently
  // changing behaviour.
  const {
    holdStatus,
    holdMessage,
    secondsLeft,
    holdId,
    resolvedResourceId,
    resolvedGroupId,
    setHoldStatus,
  } = useResourceHold({
    venueId: venue.id,
    sections: venue.sections,
    resourceId,
    date,
    time,
    email: customerEmail,
    autoAssign: isAutoAssign,
    partySize,
    resourceGroupId,
    enabled: !bookingBlocked,
  });

  /**
   * What the banner names as held. An auto-assigned or group hold is only known once the server
   * answers, so its resolved ids win over the form's own pick.
   */
  const heldGroup = allGroups.find((g) => g.id === (resolvedGroupId ?? resourceGroupId));
  const heldResource = allResources.find((tbl) => tbl.id === (resolvedResourceId ?? resourceId));
  const heldResourceName = heldGroup
    ? groupDisplayName(heldGroup)
    : heldResource
      ? (heldResource.name ?? t("booking.placement.resourceFallbackName", { id: heldResource.id }))
      : null;

  // When the party size exceeds the largest resource, surface the contact-venue
  // notice so the user knows why booking is blocked. Re-opens on each over-capacity
  // change (e.g. bumping from one too-big value to another).
  useEffect(() => {
    if (partyTooLarge) setLargePartyNoticeOpen(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [partyTooLarge, maxResourceCapacity]);

  const partySizeOptions = [...Array(10).keys()].map((i) => ({
    label: isDrawer
      ? t("booking.form.partySize", { count: i + 1 })
      : t("booking.form.capacityCount", { count: i + 1 }),
    value: i + 1,
  }));

  const selectedDayHours = getHoursForDate(venue, date || getNowInTimezone(timezone).dateStr);
  const minPickerTime = selectedDayHours.open;
  // A close time at or before the open time means opening hours that run past midnight, so the
  // picker runs to the end of the day rather than to a close that reads as earlier than the open.
  const maxPickerTime =
    selectedDayHours.close <= selectedDayHours.open ? "23:45" : selectedDayHours.close;

  const isValid =
    !partyTooLarge &&
    (isAutoAssign || !!resourceId || !!resourceGroupId) &&
    !!date &&
    !!time &&
    customerName.trim().length > 0 &&
    isValidEmail(customerEmail) &&
    holdStatus === "held" &&
    !bookingBlocked;

  /**
   * Which bookable unit the booking reserves. Null resource and section ids route the create call
   * into auto-assign, where the server adopts whatever the hold already reserved; a group id
   * routes it into the group branch. The three are mutually exclusive by construction.
   */
  const placementPayload = () => {
    if (resourceGroupId) return { resourceId: null, sectionId: null, resourceGroupId };
    if (isAutoAssign) return { resourceId: null, sectionId: null, resourceGroupId: null };
    return { resourceId: resourceId ?? null, sectionId, resourceGroupId: null };
  };

  const handleSubmit = async () => {
    if (!isValid || submitting) return;

    const selectedResource = allResources.find((tbl) => tbl.id === resourceId);
    if (selectedResource && partySize > selectedResource.capacity) {
      const confirmed = await confirm(
        t("booking.form.oversizeConfirm", {
          resourceCapacity: t("booking.form.capacityCount", { count: selectedResource.capacity }),
          guests: t("booking.form.partySize", { count: partySize }),
        })
      );
      if (!confirmed) return;
    }

    setSubmitting(true);
    try {
      await onSubmit({
        customerEmail,
        customerName,
        partySize,
        ...placementPayload(),
        date,
        time,
        holdId,
        specialRequests,
      });
    } finally {
      setSubmitting(false);
    }
  };

  /**
   * The docked confirm lives outside this form, in the sheet that hosts it, so what it presses
   * has to survive the form re-rendering under it — hence the ref rather than `handleSubmit`
   * itself, whose identity changes on every keystroke.
   */
  const submitRef = useRef(handleSubmit);
  useEffect(() => {
    submitRef.current = handleSubmit;
  });
  const dockedSubmit = useCallback(() => {
    void submitRef.current();
  }, []);

  /**
   * A name and an email are what the guest has to type either way, and entering them is also
   * what takes the hold — so the confirm arrives docked at the same moment the resource is held,
   * and the sheet keeps its full height while they are still choosing a time.
   */
  const hasDock = useHasBookingDock();
  const shouldDock = hasDock && customerName.trim().length > 0 && isValidEmail(customerEmail);

  usePublishBookingDock(
    shouldDock
      ? {
          holdStatus,
          secondsLeft,
          hasSelection: (isAutoAssign || !!resourceId) && !!date && !!time,
          holdMessage,
          heldResourceName,
          disabled: !isValid || submitting,
          submitting,
          onSubmit: dockedSubmit,
          onRefresh,
        }
      : null
  );

  /**
   * What the footer is actually showing, not what this render thinks it should show. The
   * publish lands in an effect, so the footer arrives one commit later — dropping the inline
   * confirm on the earlier commit leaves a frame with neither, and the scroll shifts under the
   * guest's thumb as they finish typing their email and then shifts back.
   */
  const docked = useBookingDock() !== null;

  /** Editing the resource choice invalidates a resolved hold, so it drops back to idle. */
  const clearSettledHold = () => {
    if (holdStatus === "held" || holdStatus === "expired") setHoldStatus("idle");
  };

  const holdBanner = (
    <HoldStatusBanner
      holdStatus={holdStatus}
      secondsLeft={secondsLeft}
      hasSelection={(isAutoAssign || !!resourceId) && !!date && !!time}
      holdMessage={holdMessage}
      resourceName={heldResourceName}
      onRefresh={onRefresh}
    />
  );

  const timezoneHint =
    venue.timezone && !isViewerInTimezone(timezone) ? (
      <ThemedText
        style={[
          styles.timezoneHint,
          isDrawer && styles.timezoneHintDrawer,
          { color: colors.muted },
        ]}
      >
        {t("booking.form.timezoneHint", {
          timezone: timezone.replace(/_/g, " "),
          time: venueCurrentTime,
        })}
      </ThemedText>
    ) : null;

  const guestsField = (label = t("booking.form.guestsLabel")) => (
    <GuestsField
      label={label}
      partySize={partySize}
      options={partySizeOptions}
      onChange={changePartySize}
    />
  );

  const dateField = (
    <DateField
      date={date}
      openDays={openDaysList}
      walkInDays={walkInIsoDays}
      onChange={changeDate}
    />
  );

  const timePickerField = (label = t("booking.form.timeLabel")) => (
    <TimeField
      label={label}
      time={time}
      minTime={minPickerTime}
      maxTime={maxPickerTime}
      onChange={setTime}
    />
  );

  const sectionField = (
    <SectionField
      sectionId={sectionId}
      options={sectionOptions}
      onChange={(val) => {
        clearSettledHold();
        setSectionId(val);
      }}
    />
  );

  const resourceField = (
    <ResourceField
      isAutoAssign={isAutoAssign}
      resolvedResourceId={resolvedResourceId}
      options={resourceOptions}
      selectedValue={resourceGroupId ? groupSelectValue(resourceGroupId) : resourceId}
      partySize={partySize}
      mutedColor={colors.muted}
      onChange={(val) => {
        clearSettledHold();
        selectPlacementUnit(val);
      }}
    />
  );

  const nameField = <NameField value={customerName} onChange={setCustomerName} />;
  const emailField = <EmailField value={customerEmail} onChange={setCustomerEmail} />;
  const requestsField = (label: string) => (
    <RequestsField label={label} value={specialRequests} onChange={setSpecialRequests} />
  );

  const gdprText = t("booking.form.gdprText");

  const confirmButton = (
    <Button
      onPress={handleSubmit}
      disabled={!isValid || submitting}
      loading={submitting}
      accessibilityLabel={t("booking.form.confirmBookingLabel")}
    >
      {submitting ? t("booking.form.confirmingLabel") : t("booking.form.confirmLabel")}
    </Button>
  );

  const holdRequiredHint = !submitting &&
    holdStatus !== "held" &&
    (isAutoAssign || resourceId) &&
    date &&
    time && (
      <ThemedText
        style={[styles.hint, { color: colors.muted }]}
        role="status"
        accessibilityLiveRegion="polite"
      >
        {t("booking.form.holdRequiredHint")}
      </ThemedText>
    );

  const largePartyModal = (
    <LargePartyNoticeModal
      visible={largePartyNoticeOpen}
      maxCapacity={maxResourceCapacity}
      venue={venue}
      onClose={() => setLargePartyNoticeOpen(false)}
    />
  );

  const largePartyBanner = partyTooLarge && (
    <LargePartyNotice
      maxCapacity={maxResourceCapacity}
      onContact={() => setLargePartyNoticeOpen(true)}
    />
  );

  const timesContent = isClosedDay ? (
    <ThemedText style={[styles.closedDayNotice, { color: colors.error }]}>
      {t("booking.form.closedDayNotice")}
    </ThemedText>
  ) : isWalkInDay ? (
    <WalkInNotice
      scope="day"
      daysLabel={walkInDaysLabel(venue) ?? undefined}
      onJoinWaitlist={date === getVenueDate(timezone) ? onJoinWaitlist : undefined}
    />
  ) : (
    <PopularTimesPicker
      slots={availabilitySlots}
      selectedTime={time}
      onSelectTime={setTime}
      selectedDate={date}
      timezone={timezone}
      wrap={isDrawer}
    />
  );

  const timesBlock = (label: string) => (
    <View style={styles.availabilityHeader} aria-busy={loadingAvailability}>
      <View style={styles.availabilityLabelRow}>
        <ThemedText style={styles.label}>{label}</ThemedText>
        {loadingAvailability && (
          <ActivityIndicator
            size="small"
            color={PRIMARY}
            accessibilityLabel={t("booking.form.loadingTimesLabel")}
          />
        )}
      </View>
      {timesContent}
    </View>
  );

  const parts: BookingFormParts = {
    guestsField,
    dateField,
    timePickerField,
    sectionField,
    resourceField,
    nameField,
    emailField,
    requestsField,
    timesContent,
    timesBlock,
    timezoneHint,
    largePartyBanner,
    largePartyModal,
    // Docked, these three are rendered by the sheet's footer instead. Leaving them here as
    // well would put a second Confirm in the scroll behind the first.
    holdBanner: docked ? null : holdBanner,
    holdRequiredHint: docked ? null : holdRequiredHint,
    confirmButton: docked ? null : confirmButton,
    gdprText,
  };

  if (isDrawer) {
    return (
      <BookingFormDrawerLayout
        venue={venue}
        parts={parts}
        bookingBlocked={bookingBlocked}
        mutedColor={colors.muted}
        primaryColor={PRIMARY}
        borderColor={colors.border}
        loadingAvailability={loadingAvailability}
        placementOpen={placementOpen}
        onTogglePlacement={() => setPlacementOpen((o) => !o)}
      />
    );
  }

  return (
    <BookingFormInlineLayout
      venue={venue}
      parts={parts}
      bookingBlocked={bookingBlocked}
      mutedColor={colors.muted}
      isTwoColumn={isTwoColumn}
    />
  );
}
