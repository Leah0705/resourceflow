import { useEffect, useState } from "react";
import {
  ActivityIndicator,
  Modal,
  Pressable,
  ScrollView,
  TouchableWithoutFeedback,
  View,
} from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import Input from "@/components/common/Input";
import Select from "@/components/common/Select";
import DatePicker from "@/components/common/DatePicker";
import TimePicker from "@/components/common/TimePicker";
import { getHoursForDate } from "@/utils/openingHours";
import Button from "@/components/common/Button";
import ConfirmModal from "@/components/common/ConfirmModal";
import { fetchVenues, VenueDto, SectionDto } from "@/api/venues";
import { isValidEmail } from "@/utils/validation";
import { adminCreateBooking } from "@/api/admin";
import { useAppTheme } from "@/hooks/use-app-theme";
import { styles } from "./NewBookingModal.styles";
import { Icon } from "@/components/common/Icon";

function todayDate() {
  return new Date().toISOString().split("T")[0];
}

function nextSlotTime(openTime = "09:00", closeTime = "22:00") {
  const now = new Date();
  let h = now.getHours();
  const min = now.getMinutes();
  const m = min < 15 ? 15 : min < 30 ? 30 : min < 45 ? 45 : 0;
  if (m === 0) h += 1;
  const [openH] = openTime.split(":").map(Number);
  const [closeH] = closeTime.split(":").map(Number);
  if (h < openH || h >= closeH) return `${(openH + 1).toString().padStart(2, "0")}:00`;
  return `${h.toString().padStart(2, "0")}:${m.toString().padStart(2, "0")}`;
}

interface NewBookingModalProps {
  visible: boolean;
  onClose: () => void;
  onCreated: (bookingId: number) => void;
}

export function NewBookingModal({ visible, onClose, onCreated }: NewBookingModalProps) {
  const { t } = useTranslation();
  const { colors, primaryColor: PRIMARY } = useAppTheme();
  const borderColor = colors.border;
  const mutedColor = colors.muted;

  const [venues, setVenues] = useState<VenueDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [capacityWarning, setCapacityWarning] = useState<string | null>(null);

  const [venueId, setVenueId] = useState<number | undefined>();
  const [sectionId, setSectionId] = useState<number | undefined>();
  const [resourceId, setResourceId] = useState<number | undefined>();
  const [date, setDate] = useState(todayDate());
  const [time, setTime] = useState(() => nextSlotTime());
  const [partySize, setPartySize] = useState(2);
  const [email, setEmail] = useState("");
  const [guestName, setGuestName] = useState("");

  useEffect(() => {
    if (!visible) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    fetchVenues().then((data) => {
      setVenues(data);
      if (data.length > 0) {
        const r = data[0];
        setVenueId(r.id);
        const todayHours = getHoursForDate(r, todayDate());
        setTime(nextSlotTime(todayHours.open, todayHours.close));
        const firstSection = r.sections[0];
        if (firstSection) {
          setSectionId(firstSection.id);
          setResourceId(firstSection.resources[0]?.id);
        }
      }
      setLoading(false);
    });
  }, [visible]);

  useEffect(() => {
    if (!visible) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setError(null);
      setCapacityWarning(null);
      setEmail("");
      setGuestName("");
      setPartySize(2);
      setDate(todayDate());
    }
  }, [visible]);

  const selectedVenue = venues.find((r) => r.id === venueId);
  const sections: SectionDto[] = selectedVenue?.sections ?? [];
  const selectedSection = sections.find((s) => s.id === sectionId);
  const resources = selectedSection?.resources ?? [];

  const handleVenueChange = (id: string | number) => {
    setVenueId(id as number);
    const r = venues.find((x) => x.id === id);
    const sec = r?.sections[0];
    setSectionId(sec?.id);
    setResourceId(sec?.resources[0]?.id);
  };

  const handleSectionChange = (id: string | number) => {
    setSectionId(id as number);
    const sec = sections.find((s) => s.id === id);
    setResourceId(sec?.resources[0]?.id);
  };

  // Admins may back-date. A past date cannot reuse "next future slot" (that only
  // makes sense for today), so fall back to the venue's opening hour for the
  // chosen day. Today and future dates keep their existing time — no behaviour
  // change for the default (today) flow.
  const handleDateSelect = (newDate: string) => {
    setDate(newDate);
    if (newDate < todayDate()) {
      const hours = getHoursForDate(selectedVenue ?? {}, newDate);
      setTime(hours.open || "12:00");
    }
  };

  const isValid =
    !!venueId && !!sectionId && !!resourceId && isValidEmail(email) && !!date && !!time;

  const doSubmit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const isoDate = new Date(`${date}T${time}:00`).toISOString();
      const result = await adminCreateBooking({
        venueId: venueId!,
        sectionId: sectionId!,
        resourceId: resourceId!,
        date: isoDate,
        customerEmail: email,
        customerName: guestName.trim() || undefined,
        partySize,
      });
      if (result) {
        onCreated(result.id);
        onClose();
      }
    } catch (err) {
      setError(
        err instanceof Error ? err.message : t("admin.bookings.newBookingModal.createError")
      );
      setSubmitting(false);
    }
  };

  const handleSubmit = () => {
    if (!isValid) return;
    const resource = resources.find((tbl) => tbl.id === resourceId);
    if (resource && partySize > resource.capacity) {
      setCapacityWarning(
        t("admin.bookings.newBookingModal.overCapacityMessage", {
          resourceCapacity: resource.capacity,
          guests: t("booking.form.partySize", { count: partySize }),
        })
      );
      return;
    }
    doSubmit();
  };

  const venueOptions = venues.map((r) => ({ label: r.name, value: r.id }));
  const sectionOptions = sections.map((s) => ({ label: s.name, value: s.id }));
  const resourceOptions = resources.map((tbl) => ({
    label: t("admin.bookings.form.resourceOptionLabel", {
      name: tbl.name ?? t("admin.bookings.form.resourceFallbackName", { id: tbl.id }),
      capacity: t("booking.form.capacityCount", { count: tbl.capacity }),
    }),
    value: tbl.id,
  }));
  const partySizeOptions = [...Array(10).keys()].map((i) => ({
    label: t("booking.form.partySize", { count: i + 1 }),
    value: i + 1,
  }));

  return (
    <>
      <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
        <Pressable
          style={styles.backdrop}
          onPress={onClose}
          accessibilityRole="button"
          accessibilityLabel={t("admin.bookings.newBookingModal.closeFormLabel")}
        >
          <TouchableWithoutFeedback>
            <View
              role="dialog"
              aria-modal
              accessibilityViewIsModal
              accessibilityLabel={t("admin.bookings.newBookingModal.dialogLabel")}
              style={[styles.sheet, { backgroundColor: colors.card, borderColor }]}
            >
              <View style={[styles.header, { borderBottomColor: borderColor }]}>
                <ThemedText style={styles.title} accessibilityRole="header">
                  {t("admin.bookings.newBooking")}
                </ThemedText>
                <Pressable
                  onPress={onClose}
                  style={styles.closeBtn}
                  accessibilityRole="button"
                  accessibilityLabel={t("common.actions.close")}
                  hitSlop={{ top: 11, bottom: 11, left: 11, right: 11 }}
                >
                  <Icon name="close" size={22} color={mutedColor} />
                </Pressable>
              </View>

              {loading ? (
                <View style={styles.loadingContainer}>
                  <ActivityIndicator
                    size="large"
                    color={PRIMARY}
                    accessibilityLabel={t("admin.bookings.newBookingModal.loadingForm")}
                  />
                </View>
              ) : (
                <ScrollView
                  style={styles.body}
                  contentContainerStyle={styles.bodyContent}
                  keyboardShouldPersistTaps="handled"
                >
                  {error && (
                    <View style={styles.errorBanner}>
                      <ThemedText style={styles.errorText}>{error}</ThemedText>
                    </View>
                  )}

                  <View style={styles.field}>
                    <ThemedText style={styles.label}>{t("admin.bookings.form.venue")}</ThemedText>
                    <Select
                      selectedValue={venueId}
                      onSelect={handleVenueChange}
                      options={venueOptions}
                    />
                  </View>

                  <View style={styles.fieldRow}>
                    <View style={[styles.fieldHalf, styles.field]}>
                      <ThemedText style={styles.label}>{t("booking.form.sectionLabel")}</ThemedText>
                      <Select
                        selectedValue={sectionId}
                        onSelect={handleSectionChange}
                        options={sectionOptions}
                      />
                    </View>
                    <View style={[styles.fieldHalf, styles.field]}>
                      <ThemedText style={styles.label}>
                        {t("booking.form.resourceLabel")}
                      </ThemedText>
                      <Select
                        selectedValue={resourceId}
                        onSelect={(v) => setResourceId(v as number)}
                        options={resourceOptions}
                      />
                    </View>
                  </View>

                  <View style={styles.fieldRow}>
                    <View style={[styles.fieldHalf, styles.field]}>
                      <ThemedText style={styles.label}>{t("booking.form.dateLabel")}</ThemedText>
                      <DatePicker selectedDate={date} onSelect={handleDateSelect} allowPast />
                    </View>
                    <View style={[styles.fieldHalf, styles.field]}>
                      <ThemedText style={styles.label}>{t("booking.form.timeLabel")}</ThemedText>
                      <TimePicker
                        selectedTime={time}
                        onSelect={setTime}
                        minTime={getHoursForDate(selectedVenue ?? {}, date).open}
                        maxTime={getHoursForDate(selectedVenue ?? {}, date).close}
                      />
                    </View>
                  </View>

                  <View style={styles.fieldRow}>
                    <View style={[styles.fieldHalf, styles.field]}>
                      <ThemedText style={styles.label}>{t("booking.form.guestsLabel")}</ThemedText>
                      <Select
                        selectedValue={partySize}
                        onSelect={(v) => setPartySize(v as number)}
                        options={partySizeOptions}
                      />
                    </View>
                    <View style={styles.fieldHalf}>
                      <ThemedText style={styles.label}>
                        {t("admin.bookings.form.guestNameOptional")}
                      </ThemedText>
                      <Input
                        placeholder={t("admin.bookings.form.namePlaceholder")}
                        value={guestName}
                        onChangeText={setGuestName}
                        autoCapitalize="words"
                      />
                    </View>
                  </View>

                  <View style={styles.field}>
                    <ThemedText style={styles.label}>
                      {t("admin.bookings.form.guestEmail")}
                    </ThemedText>
                    <Input
                      placeholder={t("admin.bookings.form.emailPlaceholder")}
                      value={email}
                      onChangeText={setEmail}
                      keyboardType="email-address"
                      autoCapitalize="none"
                    />
                  </View>

                  <Button
                    size="md"
                    fullWidth
                    style={styles.submitBtn}
                    onPress={handleSubmit}
                    disabled={!isValid || submitting}
                    loading={submitting}
                  >
                    {submitting
                      ? t("admin.bookings.newBookingModal.creating")
                      : t("admin.bookings.newBookingModal.submit")}
                  </Button>
                </ScrollView>
              )}
            </View>
          </TouchableWithoutFeedback>
        </Pressable>
      </Modal>

      <ConfirmModal
        visible={!!capacityWarning}
        title={t("admin.bookings.newBookingModal.overCapacityTitle")}
        message={capacityWarning ?? ""}
        confirmLabel={t("admin.bookings.newBookingModal.bookAnyway")}
        cancelLabel={t("admin.bookings.goBack")}
        onConfirm={() => {
          setCapacityWarning(null);
          doSubmit();
        }}
        onCancel={() => setCapacityWarning(null)}
      />
    </>
  );
}
