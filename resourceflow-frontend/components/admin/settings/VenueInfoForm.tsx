import { useState } from "react";
import { View } from "react-native";
import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import { ThemedText } from "@/components/themed-text";
import Input from "@/components/common/Input";
import Button from "@/components/common/Button";
import { useAppTheme } from "@/hooks/use-app-theme";
import { useAutosave } from "@/hooks/use-autosave";
import { usePersistedState } from "@/hooks/use-persisted-state";
import { AnimatedAccordion } from "@/components/common/AnimatedAccordion";
import { AccordionCardHeader } from "./AccordionCardHeader";
import { SaveStatus } from "./SaveStatus";
import {
  BookingRefFormat,
  DayHoursDto,
  VenueDto,
  DurationRuleDto,
  deleteGuideFile,
  updateVenue,
  uploadGuideFile,
} from "@/api/venues";
import { getHoursForDay, hasCustomHours, parseOpenDays } from "@/utils/openingHours";
import { isValidEmail, isValidUrl, WEB_SCHEMES } from "@/utils/validation";
import { parseWalkInDays } from "@/utils/walkIn";
import { hasDuplicateDurationRules } from "@/utils/durationRules";
import { theme } from "@/theme/theme";
import { isOvernight } from "./sectionHelpers";
import { OpeningHoursSection } from "./OpeningHoursSection";
import { WalkInPolicySection } from "./WalkInPolicySection";
import { LocationTagsSection } from "./LocationTagsSection";
import { DurationRulesField } from "./DurationRulesField";
import { styles as sharedStyles } from "./settings.styles";
import Select, { type SelectOption } from "@/components/common/Select";
import { styles } from "./VenueInfoForm.styles";
import { Icon } from "@/components/common/Icon";

const TIMEZONES = [
  "UTC",
  "Europe/London",
  "Europe/Paris",
  "Europe/Berlin",
  "Europe/Madrid",
  "Europe/Rome",
  "Europe/Amsterdam",
  "Europe/Brussels",
  "Europe/Zurich",
  "Europe/Vienna",
  "Europe/Warsaw",
  "Europe/Prague",
  "Europe/Budapest",
  "Europe/Athens",
  "Europe/Helsinki",
  "Europe/Stockholm",
  "Europe/Oslo",
  "Europe/Dublin",
  "Europe/Lisbon",
  "Europe/Moscow",
  "Europe/Istanbul",
  "America/New_York",
  "America/Chicago",
  "America/Denver",
  "America/Los_Angeles",
  "America/Toronto",
  "America/Vancouver",
  "America/Mexico_City",
  "America/Sao_Paulo",
  "America/Buenos_Aires",
  "America/Bogota",
  "Asia/Tokyo",
  "Asia/Shanghai",
  "Asia/Hong_Kong",
  "Asia/Singapore",
  "Asia/Seoul",
  "Asia/Kolkata",
  "Asia/Dubai",
  "Asia/Bangkok",
  "Asia/Jakarta",
  "Asia/Kuala_Lumpur",
  "Asia/Manila",
  "Asia/Taipei",
  "Australia/Sydney",
  "Australia/Melbourne",
  "Australia/Perth",
  "Australia/Brisbane",
  "Pacific/Auckland",
  "Africa/Johannesburg",
  "Africa/Cairo",
  "Africa/Lagos",
  "Africa/Nairobi",
];

const TIMEZONE_OPTIONS: SelectOption[] = TIMEZONES.map((tz) => ({
  value: tz,
  label: tz.replace(/_/g, " "),
}));

const DURATION_OPTIONS: SelectOption[] = [30, 60, 90, 120, 150, 180, 240, 300, 360, 420, 480].map(
  (minutes) => ({ value: minutes, label: formatDurationLabel(minutes) })
);

// Allowed start-time intervals — must match the server-side allow-list
// (VenueManagementService._allowedBookingSlotIntervalsMinutes). Kept small so
// availability slot generation can't be sent into a degenerate spin.
const SLOT_INTERVAL_OPTIONS: SelectOption[] = [15, 30, 60].map((minutes) => ({
  value: minutes,
  label: formatDurationLabel(minutes),
}));

// Max spare-capacity options for MaxSpareCapacity. The API models "unrestricted" as null,
// which a Select option value can't hold, so it travels through the picker as this sentinel.
// The cap rejects a resource when (resource.capacity - partySize) exceeds the selection.
const OVERSIZE_OFF = "off";

/**
 * `value` is the `MaxSpareCapacity` wire value (or the `OVERSIZE_OFF` sentinel for the
 * server's null) — only `label` localizes.
 * @see [VenueInfoForm.test.tsx](../../../tests/components/admin/settings/VenueInfoForm.test.tsx)
 * — pins the singular "+1 place" against the plural "+2 places".
 */
function getOversizeOptions(t: TFunction): SelectOption[] {
  return [
    { value: OVERSIZE_OFF, label: t("admin.settings.venueInfo.oversizeOff") },
    ...[0, 1, 2, 3, 4, 5, 6, 7, 8].map((capacity) => ({
      value: capacity,
      label: t("admin.settings.venueInfo.spareCapacity", { count: capacity }),
    })),
  ];
}

// Booking reference formats — values must match the backend BookingRefFormat member names,
// which is what the API accepts and returns.
function getBookingRefFormatOptions(t: TFunction): { value: BookingRefFormat; label: string }[] {
  return [
    { value: "AlphaNumeric", label: t("admin.settings.venueInfo.refFormatWords") },
    { value: "Numeric", label: t("admin.settings.venueInfo.refFormatNumbers") },
  ];
}

// Mirrors the backend ContactLimits caps so the admin sees the ceiling before the round-trip.
const MAX_PHONE_LENGTH = 32;
const MAX_EMAIL_LENGTH = 254;

// Mirrors the backend MediaController._maxGuideBytes cap. A file picker pre-check keeps the
// UX instantaneous for oversize uploads instead of waiting on the server's 400 response.
const MAX_GUIDE_BYTES = 10 * 1024 * 1024;

// A GuideUrl pointing at this instance's own /media/guide-<id>.pdf path means it's a file
// the admin uploaded through ResourceFlow (vs. an external link they pasted). Used to decide
// which affordance to show: "Remove uploaded file" for served files, or the link input only.
const isServedGuideFile = (url: string | null | undefined): boolean =>
  !!url && /^\/media\/guide-\d+\.pdf(\?|$)/.test(url);

function formatDurationLabel(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;
  if (hours === 0) return `${remainder}m`;
  if (remainder === 0) return `${hours}h`;
  return `${hours}h ${remainder}m`;
}

type WeekHours = Record<number, { open: string; close: string }>;

function initialWeekHours(venue: VenueDto): WeekHours {
  const week: WeekHours = {};
  for (let day = 1; day <= 7; day++) {
    week[day] = getHoursForDay(venue, day);
  }
  return week;
}

function buildOpenHoursPayload(
  customHours: boolean,
  weekHours: WeekHours,
  openTime: string,
  closeTime: string
): DayHoursDto[] {
  const payload: DayHoursDto[] = [];
  for (let day = 1; day <= 7; day++) {
    payload.push(
      customHours
        ? { day, open: weekHours[day].open, close: weekHours[day].close }
        : { day, open: openTime, close: closeTime }
    );
  }
  return payload;
}

/**
 * What changing a location's timezone does to the bookings it already holds.
 *
 * Every slot is stored in UTC, so the booking does not move: the guest is still expected at
 * the same real moment. What moves is the wall-clock time that moment reads as, which is the
 * time the confirmation email told them. Nothing re-sends, and the schedule-conflict read does
 * not catch it either, because the slot usually still lands inside the new local window.
 *
 * Deliberately a warning and not a gate or a rebase. Silently rewriting confirmed bookings as a
 * side effect of a settings edit is the failure mode this whole area exists to avoid.
 *
 * @see [VenueInfoForm.test.tsx](../../../tests/components/admin/settings/VenueInfoForm.test.tsx)
 * — pins that it stays out of the way until the location actually holds bookings.
 */
function TimezoneRebaseWarning({
  upcomingBookingsCount,
  warningColor,
}: {
  upcomingBookingsCount: number;
  warningColor: string;
}) {
  const { t } = useTranslation();
  if (upcomingBookingsCount <= 0) return null;

  return (
    <View testID="timezone-rebase-warning" style={styles.fieldWarning}>
      <Icon name="alert-circle-outline" size="sm" color={warningColor} />
      <ThemedText style={[styles.fieldHint, styles.fieldWarningText, { color: warningColor }]}>
        {t("admin.settings.venueInfo.timezoneRebaseWarning", {
          bookings: t("admin.settings.venueInfo.upcomingBookingsCount", {
            count: upcomingBookingsCount,
          }),
        })}
      </ThemedText>
    </View>
  );
}

export function VenueInfoForm({
  venue,
  onSaved,
  /**
   * Drives the timezone warning only. Zero means there is nothing a timezone change could
   * reinterpret, so the warning stays out of the way on a location with no bookings yet.
   */
  upcomingBookingsCount = 0,
}: {
  venue: VenueDto;
  onSaved: (patch: Partial<VenueDto>) => void;
  upcomingBookingsCount?: number;
}) {
  const { t } = useTranslation();
  const { colors, isDark, primaryColor } = useAppTheme();

  const mutedColor = colors.muted;
  const borderColor = colors.border;
  const surface2 = isDark ? "#252729" : "#f9fafb";

  const [name, setName] = useState(venue.name);
  const [address, setAddress] = useState(venue.address ?? "");
  const [description, setDescription] = useState(venue.description ?? "");
  const [guideUrl, setGuideUrl] = useState(venue.guideUrl ?? "");
  const [phoneNumber, setPhoneNumber] = useState(venue.phoneNumber ?? "");
  const [emailAddress, setEmailAddress] = useState(venue.emailAddress ?? "");
  const [openTime, setOpenTime] = useState(venue.openTime ?? "09:00");
  const [closeTime, setCloseTime] = useState(venue.closeTime ?? "22:00");
  const [customHours, setCustomHours] = useState(() => hasCustomHours(venue));
  const [weekHours, setWeekHours] = useState<WeekHours>(() => initialWeekHours(venue));
  const [openDays, setOpenDays] = useState<number[]>(parseOpenDays(venue.openDays));
  const [walkInOnly, setWalkInOnly] = useState(!!venue.walkInOnly);
  const [walkInDays, setWalkInDays] = useState<number[]>(() => parseWalkInDays(venue.walkInDays));
  const [timezone, setTimezone] = useState(venue.timezone ?? "UTC");
  const [defaultBookingDurationMinutes, setDefaultBookingDurationMinutes] = useState(
    venue.defaultBookingDurationMinutes ?? 60
  );
  const [durationRules, setDurationRules] = useState<DurationRuleDto[]>(venue.durationRules ?? []);
  const [bookingSlotIntervalMinutes, setBookingSlotIntervalMinutes] = useState(
    venue.bookingSlotIntervalMinutes ?? 30
  );
  // Held as typed so a half-cleared field doesn't snap back; blank is "no cap".
  const [maxGuestsText, setMaxGuestsText] = useState(
    venue.maxGuestsPerSlot == null ? "" : String(venue.maxGuestsPerSlot)
  );
  const maxGuestsPerSlot = maxGuestsText.trim() === "" ? null : Number(maxGuestsText.trim());
  const [maxSpareCapacity, setMaxSpareCapacity] = useState<number | null>(
    venue.maxSpareCapacity ?? null
  );
  const [bookingRefFormat, setBookingRefFormat] = useState<BookingRefFormat>(
    venue.bookingRefFormat ?? "AlphaNumeric"
  );
  const [tags, setTags] = useState<string[]>(venue.tags ?? []);
  const [tagInput, setTagInput] = useState("");
  const [guideUploading, setGuideUploading] = useState(false);
  const [guideMsg, setGuideMsg] = useState<{ text: string; ok: boolean } | null>(null);

  const addTag = (raw: string) => {
    const trimmed = raw.trim().replace(/,+$/, "");
    if (trimmed && !tags.includes(trimmed)) {
      setTags((prev) => [...prev, trimmed]);
    }
    setTagInput("");
  };

  const removeTag = (tag: string) => {
    setTags((prev) => prev.filter((t) => t !== tag));
  };

  const toggleDay = (day: number) => {
    setOpenDays((prev) =>
      prev.includes(day) ? prev.filter((d) => d !== day) : [...prev, day].sort()
    );
  };

  const toggleWalkInDay = (day: number) => {
    setWalkInDays((prev) =>
      prev.includes(day) ? prev.filter((d) => d !== day) : [...prev, day].sort()
    );
  };

  const setDayHours = (day: number, patch: Partial<{ open: string; close: string }>) => {
    setWeekHours((prev) => ({ ...prev, [day]: { ...prev[day], ...patch } }));
  };

  const copyHoursToAllDays = (day: number) => {
    const source = weekHours[day];
    setWeekHours(() => {
      const next: WeekHours = {};
      for (let d = 1; d <= 7; d++) {
        next[d] = { ...source };
      }
      return next;
    });
  };

  const openHoursPayload = buildOpenHoursPayload(customHours, weekHours, openTime, closeTime);
  const initialOpenHours = buildOpenHoursPayload(
    hasCustomHours(venue),
    initialWeekHours(venue),
    venue.openTime ?? "09:00",
    venue.closeTime ?? "22:00"
  );
  const guideUrlIsServedFile = isServedGuideFile(venue.guideUrl);

  const handlePickGuide = () => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "application/pdf";
    input.onchange = async () => {
      const file = input.files?.[0];
      if (!file) return;
      if (file.size > MAX_GUIDE_BYTES) {
        setGuideMsg({ text: t("admin.settings.venueInfo.guideTooLarge"), ok: false });
        return;
      }
      setGuideUploading(true);
      setGuideMsg(null);
      const url = await uploadGuideFile(venue.id, file);
      setGuideUploading(false);
      if (url) {
        // A served file supersedes any typed link; clear it locally so the link input
        // doesn't read as stale text next to the newly-uploaded file indicator.
        setGuideUrl("");
        onSaved({ guideUrl: url });
        setGuideMsg({ text: t("admin.settings.venueInfo.guideUploaded"), ok: true });
      } else {
        setGuideMsg({ text: t("admin.settings.venueInfo.guideUploadFailed"), ok: false });
      }
    };
    input.click();
  };

  const handleDeleteGuide = async () => {
    setGuideUploading(true);
    const ok = await deleteGuideFile(venue.id);
    setGuideUploading(false);
    if (ok) {
      onSaved({ guideUrl: null });
      setGuideMsg({ text: t("admin.settings.venueInfo.guideRemoved"), ok: true });
    } else {
      setGuideMsg({ text: t("admin.settings.venueInfo.guideRemoveFailed"), ok: false });
    }
  };

  // The payload as it would be sent right now, and what the server already holds. The autosave
  // hook compares the two, so a change anywhere in this form is a change to `values`.
  const values = {
    name: name.trim(),
    address: address.trim() || null,
    // Blank must go over the wire as "" — the backend's PATCH convention reads null as
    // "leave untouched", so sending null here made clearing an existing blurb a no-op.
    description: description.trim(),
    // Same "" clears / null leaves untouched convention as description, with one carve-out:
    // while a served file is the stored guide the text input isn't rendered and the local
    // guideUrl is deliberately blank (the upload flow clears it), so null keeps this save from
    // wiping the file. Otherwise blank must reach the server as "" to clear a pasted link.
    guideUrl: guideUrlIsServedFile ? null : guideUrl.trim(),
    phoneNumber: phoneNumber.trim(),
    emailAddress: emailAddress.trim(),
    openTime: customHours ? undefined : openTime,
    closeTime: customHours ? undefined : closeTime,
    openHours: openHoursPayload,
    openDays: openDays.join(","),
    walkInOnly,
    walkInDays: walkInDays.join(","),
    timezone,
    defaultBookingDurationMinutes,
    durationRules,
    bookingSlotIntervalMinutes,
    maxSpareCapacity,
    maxGuestsPerSlot,
    bookingRefFormat,
    tags: tags.join(","),
  };

  const saved = {
    name: venue.name,
    address: venue.address ?? null,
    description: venue.description ?? "",
    guideUrl: guideUrlIsServedFile ? null : (venue.guideUrl ?? ""),
    phoneNumber: venue.phoneNumber ?? "",
    emailAddress: venue.emailAddress ?? "",
    openTime: customHours ? undefined : (venue.openTime ?? "09:00"),
    closeTime: customHours ? undefined : (venue.closeTime ?? "22:00"),
    openHours: initialOpenHours,
    openDays: parseOpenDays(venue.openDays).join(","),
    walkInOnly: !!venue.walkInOnly,
    walkInDays: parseWalkInDays(venue.walkInDays).join(","),
    timezone: venue.timezone ?? "UTC",
    defaultBookingDurationMinutes: venue.defaultBookingDurationMinutes ?? 60,
    durationRules: venue.durationRules ?? [],
    bookingSlotIntervalMinutes: venue.bookingSlotIntervalMinutes ?? 30,
    maxSpareCapacity: venue.maxSpareCapacity ?? null,
    maxGuestsPerSlot: venue.maxGuestsPerSlot ?? null,
    bookingRefFormat: venue.bookingRefFormat ?? "AlphaNumeric",
    tags: (venue.tags ?? []).join(","),
  };

  /**
   * Why the save is paused, or null when it can go. These mirror the backend's own validators
   * (UrlValidator, ContactFields): with no Save button to disable there is nothing to grey out,
   * so the reason is stated in the footer instead of the write being silently withheld.
   */
  const blockedReason = ((): string | null => {
    if (!name.trim()) return t("admin.settings.venueInfo.blockedNoName");
    const trimmedGuideUrl = guideUrl.trim();
    if (
      trimmedGuideUrl &&
      !isServedGuideFile(trimmedGuideUrl) &&
      !isValidUrl(trimmedGuideUrl, WEB_SCHEMES)
    ) {
      return t("admin.settings.venueInfo.blockedInvalidGuideUrl");
    }
    if (phoneNumber.trim().length > MAX_PHONE_LENGTH) {
      return t("admin.settings.venueInfo.blockedPhoneTooLong", { max: MAX_PHONE_LENGTH });
    }
    if (emailAddress.trim() && !isValidEmail(emailAddress.trim())) {
      return t("admin.settings.venueInfo.blockedInvalidEmail");
    }
    if (hasDuplicateDurationRules(durationRules)) {
      return t("admin.settings.venueInfo.blockedDuplicateDurationRules");
    }
    if (
      maxGuestsPerSlot !== null &&
      !(Number.isInteger(maxGuestsPerSlot) && maxGuestsPerSlot >= 1)
    ) {
      return t("admin.settings.venueInfo.blockedInvalidMaxGuests");
    }
    return null;
  })();

  const { status, error, retry, undo } = useAutosave({
    values,
    saved,
    canSave: !blockedReason,
    save: async (payload) => {
      const result = await updateVenue(venue.id, payload);
      if (!result) return t("admin.settings.venueInfo.saveUnreachable");
      onSaved({
        name: result.name,
        address: result.address,
        description: result.description,
        guideUrl: result.guideUrl,
        phoneNumber: result.phoneNumber,
        emailAddress: result.emailAddress,
        openTime: result.openTime,
        closeTime: result.closeTime,
        openHours: result.openHours,
        openDays: result.openDays,
        walkInOnly: result.walkInOnly,
        walkInDays: result.walkInDays,
        timezone: result.timezone,
        defaultBookingDurationMinutes: result.defaultBookingDurationMinutes,
        durationRules: result.durationRules,
        bookingSlotIntervalMinutes: result.bookingSlotIntervalMinutes,
        maxSpareCapacity: result.maxSpareCapacity,
        maxGuestsPerSlot: result.maxGuestsPerSlot,
        bookingRefFormat: result.bookingRefFormat,
        tags: result.tags,
      });
      return null;
    },
    // The payload is derived from this form's state rather than mirroring it (days and tags are
    // joined into strings, hours into a 7-entry list), so putting one back means running that
    // derivation backwards. Anything the payload doesn't carry is left alone.
    onRestore: (previous) => {
      setName(previous.name);
      setAddress(previous.address ?? "");
      setDescription(previous.description);
      if (!guideUrlIsServedFile) setGuideUrl(previous.guideUrl ?? "");
      setPhoneNumber(previous.phoneNumber);
      setEmailAddress(previous.emailAddress);
      setOpenDays(previous.openDays ? previous.openDays.split(",").map(Number) : []);
      setWalkInOnly(previous.walkInOnly);
      setWalkInDays(previous.walkInDays ? previous.walkInDays.split(",").map(Number) : []);
      setTimezone(previous.timezone);
      setDefaultBookingDurationMinutes(previous.defaultBookingDurationMinutes);
      setDurationRules(previous.durationRules);
      setBookingSlotIntervalMinutes(previous.bookingSlotIntervalMinutes);
      setMaxSpareCapacity(previous.maxSpareCapacity);
      setMaxGuestsText(previous.maxGuestsPerSlot == null ? "" : String(previous.maxGuestsPerSlot));
      setBookingRefFormat(previous.bookingRefFormat);
      setTags(previous.tags ? previous.tags.split(",") : []);
      const restored: WeekHours = {};
      for (const entry of previous.openHours) {
        restored[entry.day] = { open: entry.open, close: entry.close };
      }
      setWeekHours(restored);
      const uniform = previous.openHours.every(
        (h) => h.open === previous.openHours[0].open && h.close === previous.openHours[0].close
      );
      setCustomHours(!uniform);
      if (uniform && previous.openHours[0]) {
        setOpenTime(previous.openHours[0].open);
        setCloseTime(previous.openHours[0].close);
      }
    },
  });

  const anyOvernight = customHours
    ? openDays.some((d) => isOvernight(weekHours[d].open, weekHours[d].close))
    : isOvernight(openTime, closeTime);

  const [basicInfoExpanded, setBasicInfoExpanded] = usePersistedState(
    "locations:basicInfo:expanded",
    true
  );
  const [guideExpanded, setGuideExpanded] = usePersistedState("locations:guide:expanded", true);
  const [contactExpanded, setContactExpanded] = usePersistedState(
    "locations:contact:expanded",
    true
  );
  const [bookingExpanded, setBookingExpanded] = usePersistedState(
    "locations:booking:expanded",
    true
  );

  const basicInfoSubtitle = [
    name.trim() || t("admin.settings.venueInfo.untitledLocation"),
    address.trim(),
  ]
    .filter(Boolean)
    .join(" · ");
  const guideSubtitle = guideUrlIsServedFile
    ? t("admin.settings.venueInfo.guidePdfUploaded")
    : guideUrl.trim()
      ? t("admin.settings.venueInfo.guideLinkSet")
      : t("admin.settings.venueInfo.guideNotSet");
  const contactSubtitle =
    [phoneNumber.trim(), emailAddress.trim()].filter(Boolean).join(" · ") ||
    t("admin.settings.venueInfo.contactUsesBrandWide");
  const bookingSubtitle = t("admin.settings.venueInfo.bookingSubtitle", {
    timezone: timezone.replace(/_/g, " "),
    duration: formatDurationLabel(defaultBookingDurationMinutes),
  });

  return (
    <>
      <View style={[sharedStyles.secCard, { backgroundColor: colors.card, borderColor }]}>
        <AccordionCardHeader
          icon="storefront-outline"
          title={t("admin.settings.venueInfo.basicInfoTitle")}
          subtitle={basicInfoSubtitle}
          expanded={basicInfoExpanded}
          onToggle={() => setBasicInfoExpanded((v) => !v)}
          primaryColor={primaryColor}
          mutedColor={mutedColor}
        />
        <AnimatedAccordion expanded={basicInfoExpanded}>
          <View style={[sharedStyles.secForm, { borderTopColor: borderColor }]}>
            <View style={styles.field}>
              <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                {t("admin.settings.venueInfo.nameLabel")}
              </ThemedText>
              <Input
                value={name}
                onChangeText={setName}
                placeholder={t("admin.settings.venueInfo.namePlaceholder")}
              />
            </View>

            <View style={styles.field}>
              <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                {t("admin.settings.venueInfo.addressLabel")}
              </ThemedText>
              <Input
                value={address}
                onChangeText={setAddress}
                placeholder={t("admin.settings.venueInfo.addressPlaceholder")}
              />
            </View>

            <View style={styles.field}>
              <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                {t("admin.settings.venueInfo.descriptionLabel")}
              </ThemedText>
              <Input
                value={description}
                onChangeText={setDescription}
                placeholder={t("admin.settings.venueInfo.descriptionPlaceholder")}
                multiline
                numberOfLines={4}
                style={styles.descriptionInput}
              />
              <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
                {t("admin.settings.venueInfo.descriptionHint")}
              </ThemedText>
            </View>
          </View>
        </AnimatedAccordion>
      </View>

      <View style={[sharedStyles.secCard, { backgroundColor: colors.card, borderColor }]}>
        <AccordionCardHeader
          icon="document-text-outline"
          title={t("admin.settings.venueInfo.guideTitle")}
          subtitle={guideSubtitle}
          expanded={guideExpanded}
          onToggle={() => setGuideExpanded((v) => !v)}
          primaryColor={primaryColor}
          mutedColor={mutedColor}
        />
        <AnimatedAccordion expanded={guideExpanded}>
          <View style={[sharedStyles.secForm, { borderTopColor: borderColor }]}>
            {guideUrlIsServedFile ? (
              <View style={[styles.guideFileRow, { borderColor, backgroundColor: surface2 }]}>
                <Icon name="document-text-outline" size="lg" color={primaryColor} />
                <ThemedText style={styles.guideFileName} numberOfLines={1}>
                  {t("admin.settings.venueInfo.uploadedGuidePdf")}
                </ThemedText>
                <Button
                  variant="secondary"
                  tone="danger"
                  size="md"
                  icon="trash-outline"
                  onPress={handleDeleteGuide}
                  disabled={guideUploading}
                  loading={guideUploading}
                  accessibilityLabel={t("admin.settings.venueInfo.removeGuidePdfLabel")}
                >
                  {guideUploading
                    ? t("admin.settings.venueInfo.removing")
                    : t("admin.settings.venueInfo.removeFile")}
                </Button>
              </View>
            ) : (
              <Input
                value={guideUrl}
                onChangeText={(v) => {
                  setGuideUrl(v);
                  if (guideMsg && !guideMsg.ok) setGuideMsg(null);
                }}
                placeholder="https://your-site.com/guide.pdf"
                autoCapitalize="none"
                autoCorrect={false}
                keyboardType="url"
              />
            )}
            <View style={styles.guideActions}>
              {!guideUrlIsServedFile && (
                <Button
                  variant="secondary"
                  size="md"
                  icon="cloud-upload-outline"
                  onPress={handlePickGuide}
                  disabled={guideUploading}
                  loading={guideUploading}
                  accessibilityLabel={t("admin.settings.venueInfo.uploadGuidePdfLabel")}
                >
                  {guideUploading
                    ? t("admin.settings.venueInfo.uploading")
                    : t("admin.settings.venueInfo.uploadPdf")}
                </Button>
              )}
              {guideMsg && (
                <ThemedText
                  style={[
                    styles.guideMsg,
                    { color: guideMsg.ok ? theme.colors.success : theme.colors.error },
                  ]}
                >
                  {guideMsg.text}
                </ThemedText>
              )}
            </View>
            <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
              {t("admin.settings.venueInfo.guideHint")}
            </ThemedText>
          </View>
        </AnimatedAccordion>
      </View>

      <View style={[sharedStyles.secCard, { backgroundColor: colors.card, borderColor }]}>
        <AccordionCardHeader
          icon="call-outline"
          title={t("admin.settings.venueInfo.contactTitle")}
          subtitle={contactSubtitle}
          expanded={contactExpanded}
          onToggle={() => setContactExpanded((v) => !v)}
          primaryColor={primaryColor}
          mutedColor={mutedColor}
        />
        <AnimatedAccordion expanded={contactExpanded}>
          <View style={[sharedStyles.secForm, { borderTopColor: borderColor }]}>
            <View style={styles.fieldGrid}>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.phoneLabel")}
                </ThemedText>
                <Input
                  value={phoneNumber}
                  onChangeText={setPhoneNumber}
                  placeholder={t("admin.settings.venueInfo.phonePlaceholder")}
                  autoCapitalize="none"
                  autoCorrect={false}
                  keyboardType="phone-pad"
                  maxLength={MAX_PHONE_LENGTH}
                />
              </View>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.emailLabel")}
                </ThemedText>
                <Input
                  value={emailAddress}
                  onChangeText={setEmailAddress}
                  placeholder={t("admin.settings.venueInfo.emailPlaceholder")}
                  autoCapitalize="none"
                  autoCorrect={false}
                  keyboardType="email-address"
                  maxLength={MAX_EMAIL_LENGTH}
                />
              </View>
            </View>
            <ThemedText style={[styles.contactHint, { color: mutedColor }]}>
              {t("admin.settings.venueInfo.contactHint")}
            </ThemedText>
          </View>
        </AnimatedAccordion>
      </View>

      <View style={[sharedStyles.secCard, { backgroundColor: colors.card, borderColor }]}>
        <AccordionCardHeader
          icon="options-outline"
          title={t("admin.settings.venueInfo.bookingSettingsTitle")}
          subtitle={bookingSubtitle}
          expanded={bookingExpanded}
          onToggle={() => setBookingExpanded((v) => !v)}
          primaryColor={primaryColor}
          mutedColor={mutedColor}
        />
        <AnimatedAccordion expanded={bookingExpanded}>
          <View style={[sharedStyles.secForm, { borderTopColor: borderColor }]}>
            <View style={styles.fieldGrid}>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.timezoneLabel")}
                </ThemedText>
                <Select
                  accessibilityLabel={t("admin.settings.venueInfo.timezoneLabel")}
                  options={TIMEZONE_OPTIONS}
                  selectedValue={timezone}
                  onSelect={(value) => setTimezone(String(value))}
                />
                <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.timezoneHint")}
                </ThemedText>
                <TimezoneRebaseWarning
                  upcomingBookingsCount={upcomingBookingsCount}
                  warningColor={colors.warning}
                />
              </View>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.durationLabel")}
                </ThemedText>
                <Select
                  accessibilityLabel={t("admin.settings.venueInfo.durationLabel")}
                  options={DURATION_OPTIONS}
                  selectedValue={defaultBookingDurationMinutes}
                  onSelect={(value) => setDefaultBookingDurationMinutes(Number(value))}
                />
                <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.durationHint")}
                </ThemedText>
              </View>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.slotIntervalLabel")}
                </ThemedText>
                <Select
                  accessibilityLabel={t("admin.settings.venueInfo.slotIntervalLabel")}
                  options={SLOT_INTERVAL_OPTIONS}
                  selectedValue={bookingSlotIntervalMinutes}
                  onSelect={(value) => setBookingSlotIntervalMinutes(Number(value))}
                />
                <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.slotIntervalHint")}
                </ThemedText>
              </View>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.oversizeLabel")}
                </ThemedText>
                <Select
                  accessibilityLabel={t("admin.settings.venueInfo.oversizeLabel")}
                  options={getOversizeOptions(t)}
                  selectedValue={maxSpareCapacity ?? OVERSIZE_OFF}
                  onSelect={(value) =>
                    setMaxSpareCapacity(value === OVERSIZE_OFF ? null : Number(value))
                  }
                />
                <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.oversizeHint")}
                </ThemedText>
              </View>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.maxGuestsLabel")}
                </ThemedText>
                <Input
                  testID="max-guests-input"
                  accessibilityLabel={t("admin.settings.venueInfo.maxGuestsLabel")}
                  value={maxGuestsText}
                  onChangeText={setMaxGuestsText}
                  keyboardType="number-pad"
                  placeholder={t("admin.settings.venueInfo.maxGuestsPlaceholder")}
                />
                <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.maxGuestsHint", {
                    minutes: bookingSlotIntervalMinutes,
                  })}
                </ThemedText>
              </View>
              <View style={styles.gridField}>
                <ThemedText style={[sharedStyles.fieldLabel, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.refFormatLabel")}
                </ThemedText>
                <Select
                  accessibilityLabel={t("admin.settings.venueInfo.refFormatLabel")}
                  options={getBookingRefFormatOptions(t)}
                  selectedValue={bookingRefFormat}
                  onSelect={(value) => setBookingRefFormat(value as BookingRefFormat)}
                />
                <ThemedText style={[styles.fieldHint, { color: mutedColor }]}>
                  {t("admin.settings.venueInfo.refFormatHint")}
                </ThemedText>
              </View>
            </View>
            <DurationRulesField
              rules={durationRules}
              onChange={setDurationRules}
              defaultMinutes={defaultBookingDurationMinutes}
              durationOptions={DURATION_OPTIONS}
              mutedColor={mutedColor}
            />
          </View>
        </AnimatedAccordion>
      </View>

      <OpeningHoursSection
        customHours={customHours}
        openTime={openTime}
        closeTime={closeTime}
        weekHours={weekHours}
        openDays={openDays}
        anyOvernight={anyOvernight}
        onSetCustomHours={setCustomHours}
        onSetOpenTime={setOpenTime}
        onSetCloseTime={setCloseTime}
        onSetDayHours={setDayHours}
        onCopyHoursToAllDays={copyHoursToAllDays}
        onToggleDay={toggleDay}
        borderColor={borderColor}
        mutedColor={mutedColor}
        primaryColor={primaryColor}
        cardBg={colors.card}
        textColor={colors.text}
        isDark={isDark}
      />

      <WalkInPolicySection
        walkInOnly={walkInOnly}
        walkInDays={walkInDays}
        openDays={openDays}
        onSetWalkInOnly={setWalkInOnly}
        onToggleWalkInDay={toggleWalkInDay}
        borderColor={borderColor}
        mutedColor={mutedColor}
        primaryColor={primaryColor}
        cardBg={colors.card}
        textColor={colors.text}
        isDark={isDark}
      />

      <LocationTagsSection
        tags={tags}
        tagInput={tagInput}
        onSetTagInput={setTagInput}
        onAddTag={addTag}
        onRemoveTag={removeTag}
        borderColor={borderColor}
        mutedColor={mutedColor}
        primaryColor={primaryColor}
        cardBg={colors.card}
        surface2={surface2}
      />

      <View style={[styles.statusBar, { backgroundColor: colors.page }]}>
        <SaveStatus
          status={status}
          error={error}
          onRetry={retry}
          onUndo={undo}
          mutedColor={mutedColor}
          blockedReason={blockedReason}
          testID="location-save-status"
        />
      </View>
    </>
  );
}
