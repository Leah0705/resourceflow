import { View, ActivityIndicator } from "react-native";
import { useTranslation } from "react-i18next";
import { ThemedText } from "@/components/themed-text";
import { theme } from "@/theme/theme";
import Input from "@/components/common/Input";
import Select from "@/components/common/Select";
import DatePicker from "@/components/common/DatePicker";
import TimePicker from "@/components/common/TimePicker";
import { bookingDetailStyles as styles } from "./booking-detail.styles";
import { getHoursForDate } from "@/utils/openingHours";

interface VenueDto {
  openTime?: string;
  closeTime?: string;
  openHours?: { day: number; open: string; close: string }[];
}

interface EditBookingFormProps {
  borderColor: string;
  loadingVenues: boolean;
  venueOptions: { label: string; value: number }[];
  sectionOptions: { label: string; value: number }[];
  resourceOptions: { label: string; value: number }[];
  partySizeOptions: { label: string; value: number }[];
  editVenueId: number | null;
  editSectionId: number | null;
  editResourceId: number | null;
  editPartySize: string;
  editEmail: string;
  editCustomerName: string;
  editSpecialRequests: string;
  editDate: string;
  editTime: string;
  selectedVenue: VenueDto | null;
  setEditResourceId: (id: number) => void;
  setEditPartySize: (s: string) => void;
  setEditEmail: (e: string) => void;
  setEditCustomerName: (n: string) => void;
  setEditSpecialRequests: (s: string) => void;
  setEditDate: (d: string) => void;
  setEditTime: (t: string) => void;
  handleVenueChange: (v: string | number) => void;
  handleSectionChange: (v: string | number) => void;
}

export function EditBookingForm({
  borderColor,
  loadingVenues,
  venueOptions,
  sectionOptions,
  resourceOptions,
  partySizeOptions,
  editVenueId,
  editSectionId,
  editResourceId,
  editPartySize,
  editEmail,
  editCustomerName,
  editSpecialRequests,
  editDate,
  editTime,
  selectedVenue,
  setEditResourceId,
  setEditPartySize,
  setEditEmail,
  setEditCustomerName,
  setEditSpecialRequests,
  setEditDate,
  setEditTime,
  handleVenueChange,
  handleSectionChange,
}: EditBookingFormProps) {
  const { t } = useTranslation();
  return (
    <View style={[styles.section, { borderColor }]}>
      {loadingVenues ? (
        <ActivityIndicator size="small" color={theme.colors.primary} />
      ) : (
        <>
          <ThemedText style={styles.label}>{t("admin.bookings.form.venue")}</ThemedText>
          <Select
            selectedValue={editVenueId ?? undefined}
            onSelect={handleVenueChange}
            options={venueOptions}
          />

          <View style={styles.fieldRow}>
            <View style={styles.fieldHalf}>
              <ThemedText style={styles.label}>{t("booking.form.sectionLabel")}</ThemedText>
              <Select
                selectedValue={editSectionId ?? undefined}
                onSelect={handleSectionChange}
                options={sectionOptions}
              />
            </View>
            <View style={styles.fieldHalf}>
              <ThemedText style={styles.label}>{t("booking.form.resourceLabel")}</ThemedText>
              <Select
                selectedValue={editResourceId ?? undefined}
                onSelect={(v) => setEditResourceId(v as number)}
                options={resourceOptions}
              />
            </View>
          </View>

          <View style={styles.fieldRow}>
            <View style={styles.fieldHalf}>
              <ThemedText style={styles.label}>{t("booking.form.dateLabel")}</ThemedText>
              <DatePicker selectedDate={editDate} onSelect={setEditDate} />
            </View>
            <View style={styles.fieldHalf}>
              <ThemedText style={styles.label}>{t("booking.form.timeLabel")}</ThemedText>
              <TimePicker
                selectedTime={editTime}
                onSelect={setEditTime}
                minTime={getHoursForDate(selectedVenue ?? {}, editDate).open}
                maxTime={getHoursForDate(selectedVenue ?? {}, editDate).close}
              />
            </View>
          </View>

          <View style={styles.fieldRow}>
            <View style={styles.fieldHalf}>
              <ThemedText style={styles.label}>{t("booking.form.guestsLabel")}</ThemedText>
              <Select
                selectedValue={Number(editPartySize)}
                onSelect={(v) => setEditPartySize(String(v))}
                options={partySizeOptions}
              />
            </View>
            <View style={styles.fieldHalf}>
              <ThemedText style={styles.label}>{t("admin.bookings.form.guestEmail")}</ThemedText>
              <Input
                placeholder={t("admin.bookings.form.emailPlaceholder")}
                value={editEmail}
                onChangeText={setEditEmail}
                keyboardType="email-address"
                autoCapitalize="none"
              />
            </View>
          </View>

          <View style={styles.fieldRow}>
            <View style={styles.fieldHalf}>
              <ThemedText style={styles.label}>{t("admin.bookings.form.guestName")}</ThemedText>
              <Input
                placeholder={t("admin.bookings.form.namePlaceholder")}
                value={editCustomerName}
                onChangeText={setEditCustomerName}
                autoCapitalize="words"
              />
            </View>
          </View>

          <ThemedText style={styles.label}>{t("admin.bookings.form.specialRequests")}</ThemedText>
          <Input
            placeholder={t("admin.bookings.form.specialRequestsPlaceholder")}
            value={editSpecialRequests}
            onChangeText={setEditSpecialRequests}
          />
        </>
      )}
    </View>
  );
}
