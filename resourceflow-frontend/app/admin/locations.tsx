import { useEffect, useState } from "react";
import { ActivityIndicator, ScrollView, View, Platform } from "react-native";
import { Stack, useLocalSearchParams } from "expo-router";
import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import { ThemedText } from "@/components/themed-text";
import { ThemedView } from "@/components/themed-view";
import Button from "@/components/common/Button";
import { fetchVenues, createVenue, VenueDto } from "@/api/venues";
import {
  adminGetVenues,
  adminSetVenueArchived,
  pauseVenueBookings,
  unpauseVenueBookings,
  extendVenueBookings,
  AdminVenueSummary,
  BookingDetailDto,
} from "@/api/admin";
import { useAppTheme } from "@/hooks/use-app-theme";
import { usePersistedState } from "@/hooks/use-persisted-state";
import { useCan } from "@/context/AuthContext";

import { LocationCard } from "@/components/admin/settings/LocationCard";
import { AddLocationForm } from "@/components/admin/locations/AddLocationForm";
import { LocationPills } from "@/components/admin/locations/LocationPills";
import { ArchiveLocationRow } from "@/components/admin/locations/ArchiveLocationRow";
import { ArchivedLocationPanel } from "@/components/admin/locations/ArchivedLocationPanel";
import { ScheduleConflictsPanel } from "@/components/admin/locations/ScheduleConflictsPanel";
import { BookingDetailPopup } from "@/components/admin/bookings/BookingDetailPopup";
import { styles } from "@/components/admin/settings/settings.styles";
import { Icon } from "@/components/common/Icon";
import { fmtTime } from "@/utils/formatters";

function locationCountSummary(activeCount: number, archivedCount: number, t: TFunction): string {
  if (activeCount + archivedCount === 0) return t("admin.locations.summary.none");
  if (archivedCount > 0) {
    return t("admin.locations.summary.withArchived", {
      active: activeCount,
      archived: archivedCount,
    });
  }
  return t("admin.locations.summary.activeOnly", { count: activeCount });
}

export default function AdminLocationsScreen() {
  const { t } = useTranslation();
  const { location } = useLocalSearchParams<{ location?: string }>();
  const requestedId = location ? Number(location) : null;
  const [venues, setVenues] = useState<VenueDto[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [persistedSelectedId, setPersistedSelectedId] = usePersistedState<number | null>(
    "locations:selectedId",
    null
  );
  const [loading, setLoading] = useState(true);
  const [addingLocation, setAddingLocation] = useState(false);
  const [newLocationName, setNewLocationName] = useState("");
  const [savingLocation, setSavingLocation] = useState(false);
  const [allVenues, setAllVenues] = useState<AdminVenueSummary[]>([]);
  const [pausing, setPausing] = useState(false);
  const [extending, setExtending] = useState(false);
  const [extendedBookings, setExtendedBookings] = useState<BookingDetailDto[] | null>(null);
  const [extendNoActive, setExtendNoActive] = useState(false);
  const [archiving, setArchiving] = useState(false);
  const [archiveFailed, setArchiveFailed] = useState(false);
  const [deletedName, setDeletedName] = useState<string | null>(null);
  // The location form autosaves, so there is no submit to hang a re-read off; bumping this on
  // every committed patch is what makes the schedule-conflict panel reflect the edit just made.
  const [scheduleRevision, setScheduleRevision] = useState(0);
  const [conflictBookingId, setConflictBookingId] = useState<number | null>(null);

  const { colors, isDark, primaryColor } = useAppTheme();
  const borderColor = colors.border;
  const cardBg = colors.card;
  const mutedColor = colors.muted;
  const canDeleteLocation = useCan("delete:location");

  function patchVenue(id: number, patch: Partial<VenueDto>) {
    setVenues((prev) => prev.map((r) => (r.id === id ? { ...r, ...patch } : r)));
    setScheduleRevision((n) => n + 1);
    // The pills read the admin list, so a rename has to reach it or the selector keeps
    // showing the old name until the next load.
    if (patch.name !== undefined) {
      setAllVenues((prev) =>
        prev.map((r) => (r.id === id ? { ...r, name: patch.name as string } : r))
      );
    }
  }

  useEffect(() => {
    let cancelled = false;
    Promise.all([fetchVenues(), adminGetVenues()]).then(([active, all]) => {
      if (cancelled) return;
      setVenues(active);
      // A caller that names a location outranks the remembered one: the dashboard's stranded-
      // bookings banner sends the admin here to act on a specific location, and opening on
      // whichever one they last edited would report an all-clear over the top of the problem.
      const requestedMatch =
        requestedId != null ? all.find((r) => r.id === requestedId) : undefined;
      const persistedMatch =
        persistedSelectedId != null ? all.find((r) => r.id === persistedSelectedId) : undefined;
      const nextId = (requestedMatch ?? persistedMatch)?.id ?? all[0]?.id ?? null;
      if (nextId !== null) setSelectedId(nextId);
      setPersistedSelectedId(nextId);
      setAllVenues(all);
      setLoading(false);
    });
    /* istanbul ignore next */
    return () => {
      cancelled = true;
    };
    // persistedSelectedId and requestedId seed the initial selection only; omitting them avoids
    // a refetch loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const selectedSummary = allVenues.find((r) => r.id === selectedId) ?? null;
  const isArchived = selectedSummary?.isArchived ?? false;
  // Archived locations are absent from the editable list, so this is null for them by design.
  const selectedVenue = isArchived ? null : (venues.find((r) => r.id === selectedId) ?? null);
  const activeCount = selectedSummary?.activeBookingsCount ?? 0;
  const isPaused = selectedSummary?.bookingsPausedUntil
    ? new Date(selectedSummary.bookingsPausedUntil) > new Date()
    : false;
  const pausedUntilText =
    isPaused && selectedSummary?.bookingsPausedUntil
      ? fmtTime(new Date(selectedSummary.bookingsPausedUntil))
      : null;
  const archivedCount = allVenues.filter((r) => r.isArchived).length;

  const selectAndRemember = (id: number | null) => {
    setSelectedId(id);
    setPersistedSelectedId(id);
  };

  function handleSelectLocation(id: number) {
    selectAndRemember(id);
    setExtendedBookings(null);
    setExtendNoActive(false);
    setArchiveFailed(false);
    setDeletedName(null);
  }

  async function handleSetArchived(id: number, archived: boolean) {
    setArchiving(true);
    setArchiveFailed(false);
    const ok = await adminSetVenueArchived(id, archived);
    if (!ok) {
      setArchiving(false);
      setArchiveFailed(true);
      return;
    }
    setAllVenues((prev) => prev.map((r) => (r.id === id ? { ...r, isArchived: archived } : r)));
    // The editable list is active-only, so both directions need it back from the server:
    // archiving drops the row, restoring brings back a record this screen never held.
    const active = await fetchVenues();
    setVenues(active);
    setArchiving(false);
  }

  function handleDeleted(id: number) {
    const deleted = allVenues.find((r) => r.id === id);
    const remaining = allVenues.filter((r) => r.id !== id);
    setAllVenues(remaining);
    setVenues((prev) => prev.filter((r) => r.id !== id));
    selectAndRemember(remaining.length > 0 ? remaining[0].id : null);
    setDeletedName(deleted?.name ?? null);
  }

  if (loading) {
    return (
      <ThemedView style={styles.center}>
        <ActivityIndicator size="large" color={primaryColor} />
      </ThemedView>
    );
  }

  return (
    <>
      <ScrollView contentContainerStyle={styles.container}>
        {Platform.OS !== "web" && (
          <Stack.Screen options={{ title: t("admin.locations.pageTitle") }} />
        )}

        <View style={styles.pageHeader}>
          <View style={{ gap: 2 }}>
            <ThemedText type="h1">{t("admin.locations.pageTitle")}</ThemedText>
            <ThemedText style={[styles.pageSub, { color: mutedColor }]}>
              {locationCountSummary(allVenues.length - archivedCount, archivedCount, t)}
            </ThemedText>
          </View>
          <Button
            size="md"
            icon="add"
            onPress={() => setAddingLocation(true)}
            disabled={addingLocation}
            accessibilityLabel={t("admin.locations.addLocation")}
          >
            {t("admin.locations.addLocation")}
          </Button>
        </View>

        {deletedName && (
          <View style={{ flexDirection: "row", alignItems: "center", gap: 8 }}>
            <Icon name="checkmark-circle-outline" size="lg" color={colors.success} />
            <ThemedText style={{ fontSize: 13, color: mutedColor }}>
              {t("admin.locations.deletedNotice", { name: deletedName })}
            </ThemedText>
          </View>
        )}

        {addingLocation && (
          <AddLocationForm
            value={newLocationName}
            saving={savingLocation}
            isDark={isDark}
            mutedColor={mutedColor}
            primaryColor={primaryColor}
            onValueChange={setNewLocationName}
            onSubmit={async () => {
              if (!newLocationName.trim()) return;
              setSavingLocation(true);
              const created = await createVenue(newLocationName.trim());
              setSavingLocation(false);
              if (created) {
                setVenues((prev) => [...prev, { ...created, sections: [] }]);
                setAllVenues((prev) => [...prev, { id: created.id, name: created.name }]);
                selectAndRemember(created.id);
              }
              setNewLocationName("");
              setAddingLocation(false);
            }}
            onCancel={() => {
              setAddingLocation(false);
              setNewLocationName("");
            }}
          />
        )}

        {allVenues.length > 0 && (
          <LocationPills
            venues={allVenues}
            selectedId={selectedId}
            onSelect={handleSelectLocation}
          />
        )}

        {selectedVenue && (
          <View style={styles.bulkActions}>
            <Button
              variant="secondary"
              tone={isPaused ? "success" : "warning"}
              size="md"
              style={styles.bulkAction}
              icon={isPaused ? "play-circle-outline" : "pause-circle-outline"}
              disabled={pausing}
              loading={pausing}
              accessibilityLabel={
                isPaused
                  ? t("admin.locations.pauseButton.resumeLabel", { name: selectedVenue.name })
                  : t("admin.locations.pauseButton.pauseLabel", { name: selectedVenue.name })
              }
              onPress={async () => {
                setPausing(true);
                if (isPaused) {
                  await unpauseVenueBookings(selectedVenue.id);
                  setAllVenues((prev) =>
                    prev.map((r) =>
                      r.id === selectedVenue.id ? { ...r, bookingsPausedUntil: undefined } : r
                    )
                  );
                } else {
                  await pauseVenueBookings(selectedVenue.id, 60);
                  setAllVenues((prev) =>
                    prev.map((r) =>
                      r.id === selectedVenue.id
                        ? {
                            ...r,
                            bookingsPausedUntil: new Date(
                              Date.now() + 60 * 60 * 1000
                            ).toISOString(),
                          }
                        : r
                    )
                  );
                }
                setPausing(false);
              }}
            >
              {pausing
                ? t("common.status.saving")
                : isPaused
                  ? t("admin.locations.pauseButton.resumeUntil", { time: pausedUntilText })
                  : t("admin.locations.pauseButton.pauseFor60")}
            </Button>

            <Button
              variant="secondary"
              size="md"
              style={styles.bulkAction}
              icon="timer-outline"
              disabled={
                extending || extendedBookings !== null || extendNoActive || activeCount === 0
              }
              loading={extending}
              accessibilityLabel={t("admin.locations.extendButton.a11yLabel", {
                name: selectedVenue.name,
              })}
              onPress={async () => {
                setExtending(true);
                const result = await extendVenueBookings(selectedVenue.id, 60);
                setExtending(false);
                if (result.ok) {
                  if (result.extendedBookings.length > 0) {
                    setExtendedBookings(result.extendedBookings);
                  } else {
                    setExtendNoActive(true);
                  }
                }
              }}
            >
              {extending
                ? t("admin.locations.extendButton.extending")
                : extendedBookings !== null
                  ? t("admin.locations.extendButton.extended", { count: extendedBookings.length })
                  : extendNoActive
                    ? t("admin.locations.extendButton.noActiveToExtend")
                    : activeCount > 0
                      ? t("admin.locations.extendButton.extend", { count: activeCount })
                      : t("admin.locations.extendButton.noActive")}
            </Button>
          </View>
        )}

        {allVenues.length === 0 ? (
          <View
            style={{
              alignItems: "center",
              justifyContent: "center",
              paddingVertical: 72,
              gap: 12,
            }}
          >
            <View
              style={{
                width: 64,
                height: 64,
                borderRadius: 32,
                borderWidth: 1,
                borderColor,
                alignItems: "center",
                justifyContent: "center",
                marginBottom: 4,
              }}
            >
              <Icon name="storefront-outline" size={28} color={mutedColor} />
            </View>
            <ThemedText style={{ fontSize: 16, fontWeight: "700", textAlign: "center" }}>
              {t("admin.locations.empty.title")}
            </ThemedText>
            <ThemedText
              style={{
                fontSize: 14,
                color: mutedColor,
                textAlign: "center",
                maxWidth: 280,
                lineHeight: 22,
              }}
            >
              {t("admin.locations.empty.subtitle")}
            </ThemedText>
          </View>
        ) : selectedSummary && isArchived ? (
          <View style={[styles.secCard, { backgroundColor: cardBg, borderColor }]}>
            <ArchivedLocationPanel
              id={selectedSummary.id}
              name={selectedSummary.name}
              restoring={archiving}
              restoreFailed={archiveFailed}
              onRestore={() => handleSetArchived(selectedSummary.id, false)}
              canDelete={canDeleteLocation}
              onDeleted={handleDeleted}
            />
          </View>
        ) : selectedVenue ? (
          <View style={styles.section}>
            <ScheduleConflictsPanel
              venueId={selectedVenue.id}
              timezone={selectedVenue.timezone ?? "UTC"}
              refreshKey={scheduleRevision}
              onOpenBooking={setConflictBookingId}
              borderColor={borderColor}
              mutedColor={mutedColor}
              cardBg={cardBg}
            />
            <LocationCard
              key={selectedVenue.id}
              venue={selectedVenue}
              onSaved={(patch) => patchVenue(selectedVenue.id, patch)}
              upcomingBookingsCount={selectedSummary?.upcomingBookingsCount ?? 0}
              isDark={isDark}
              borderColor={borderColor}
              mutedColor={mutedColor}
              cardBg={cardBg}
            />
            <View style={[styles.secCard, { backgroundColor: cardBg, borderColor }]}>
              <ArchiveLocationRow
                name={selectedVenue.name}
                upcomingBookingsCount={selectedSummary?.upcomingBookingsCount ?? 0}
                archiving={archiving}
                failed={archiveFailed}
                onArchive={() => handleSetArchived(selectedVenue.id, true)}
              />
            </View>
          </View>
        ) : null}
      </ScrollView>
      <BookingDetailPopup
        bookingId={conflictBookingId}
        onClose={() => setConflictBookingId(null)}
        // A booking moved or cancelled out of the conflict is one the panel should stop listing.
        onMutated={() => setScheduleRevision((n) => n + 1)}
      />
    </>
  );
}
