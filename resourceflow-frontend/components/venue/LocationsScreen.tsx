import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  NativeScrollEvent,
  NativeSyntheticEvent,
  Platform,
  RefreshControl,
  ScrollView,
  View,
  useWindowDimensions,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useTranslation } from "react-i18next";
import i18n from "@/i18n";
import { ThemedText } from "@/components/themed-text";
import { ThemedView } from "@/components/themed-view";
import { useAppTheme } from "@/hooks/use-app-theme";
import { fetchVenues, VenueDto } from "@/api/venues";
import PageContainer from "@/components/layout/PageContainer";
import PageLoader from "@/components/common/PageLoader";
import ScrollToTopFab from "@/components/common/ScrollToTopFab";
import Footer from "@/components/layout/Footer";
import { useScrollToTopFab } from "@/hooks/use-scroll-to-top-fab";
import { useTabBarClearance } from "@/hooks/use-tab-bar-clearance";
import { scrollIntoView } from "@/utils/scrollIntoView";
import {
  forgetWaitlistTicket,
  readWaitlistTickets,
  rememberWaitlistTicket,
} from "@/utils/waitlistTickets";
import { getVenueDate } from "@/utils/venueTime";
import { CONTENT_MAX_WIDTH, CONTENT_PADDING_H, isMobileWidth } from "@/constants/breakpoints";
import { DRAWER_WIDTH } from "@/components/booking/BookingDrawer.styles";
import ScreenHeading from "@/components/layout/ScreenHeading";
import LocationListItem from "@/components/venue/LocationListItem";
import LocationsFilterBar, { type TimeWindow } from "@/components/venue/LocationsFilterBar";
import BookingDrawer from "@/components/booking/BookingDrawer";
import Button from "@/components/common/Button";
import { hexToRgb } from "@/utils/colors";
import { theme } from "@/theme/theme";
import { styles, pinnedMask } from "./LocationsScreen.styles";

/**
 * What the panel is open on: a location plus the time tapped, or that location's walk-in
 * waitlist, which has no time.
 */
interface DrawerTarget {
  venue: VenueDto;
  time: string;
  waitlist?: boolean;
}

export function availabilitySummary(
  counts: Record<number, number | null>,
  total: number
): string | null {
  const reported = Object.values(counts);
  if (total === 0 || reported.length < total) return null;
  if (reported.some((c) => c === null)) return i18n.t("venue.locationsScreen.checkingAvailability");
  const withResources = reported.filter((c) => (c ?? 0) > 0).length;
  if (total === 1)
    return withResources === 1
      ? i18n.t("venue.locationsScreen.resourcesAvailable")
      : i18n.t("venue.locationsScreen.noResourcesAtThisTime");
  return i18n.t("venue.locationsScreen.availabilitySummary", { count: total, withResources });
}

/**
 * Deep links via /locations/[id] pass `highlightId` to scroll to and expand a specific
 * location; when they also carry a time, the drawer opens straight onto it as well —
 * arriving from a time press on the home page should still show the location the guest
 * picked, not just a booking panel detached from it.
 */
/** Narrowest list worth reading beside the drawer; below it the two panes stop being two. */
const MIN_LIST_WIDTH = 360;

/**
 * Whether the drawer fits *beside* the list rather than over it. The drawer is a fixed
 * `DRAWER_WIDTH` that never shrinks, so the question is one of arithmetic, not of device
 * class: gating on the phone breakpoint gave an ~800dp tablet in portrait both panes and left
 * the list under 300dp, where the cards overflow their column instead of reflowing.
 *
 * @see [LocationsScreen.test.tsx](../../tests/components/venue/LocationsScreen.test.tsx)
 * — pins the boundary either side: the split at the width that fits, the sheet one dp under.
 */
export function splitFits(width: number): boolean {
  return width >= DRAWER_WIDTH + MIN_LIST_WIDTH + CONTENT_PADDING_H * 2;
}

export default function LocationsScreen({
  highlightId,
  initialTime,
  initialPartySize,
  initialWaitlist = false,
  hasNativeHeader = false,
}: {
  highlightId?: number;
  initialTime?: string;
  initialPartySize?: number;
  /** Opens the highlighted location's walk-in waitlist on arrival. */
  initialWaitlist?: boolean;
  /**
   * True on the route pushed over a tab root, where a native stack header sits above this
   * screen; false on the header-less `locations` root. The two need opposite treatment of the
   * top inset, and only the header case can collapse an iOS large title.
   */
  hasNativeHeader?: boolean;
}) {
  const { t } = useTranslation();
  const [venues, setVenues] = useState<VenueDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  /**
   * Bumped by a pull-to-refresh and folded into every card's key, so the cards remount and ask
   * for availability again. The list alone reloading would leave each card on the slots it
   * fetched at mount, which is the half of the page a guest pulls down to update.
   *
   * @see [LocationsScreen.test.tsx](../../tests/components/venue/LocationsScreen.test.tsx)
   * — pins that a refresh remounts the cards and keeps the list on screen meanwhile.
   */
  const [generation, setGeneration] = useState(0);
  // Raw scroll offsets live in refs; state holds only the booleans the UI reacts to, so
  // scrolling re-renders the list on transitions instead of every scroll event.
  const [filterPinned, setFilterPinned] = useState(false);
  const fab = useScrollToTopFab();
  const { colors, primaryColor } = useAppTheme();
  const { width } = useWindowDimensions();
  const insets = useSafeAreaInsets();
  const tabBarClearance = useTabBarClearance();
  const isCompact = isMobileWidth(width);
  const pageRgb = useMemo(() => hexToRgb(colors.page), [colors.page]);

  const [partySize, setPartySize] = useState(initialPartySize ?? 2);
  const [dateOverride, setDateOverride] = useState<string | null>(null);
  const [timeWindow, setTimeWindow] = useState<TimeWindow>("All");
  const [drawer, setDrawer] = useState<DrawerTarget | null>(null);
  const [waitlistTickets, setWaitlistTickets] = useState(readWaitlistTickets);
  /**
   * Two panes rather than an overlay, so the drawer takes its width off everything in the
   * list column — the footer included, which is why the footer moves out from under the
   * list to sit beneath both panes instead.
   *
   * @see [LocationsScreen.test.tsx](../../tests/components/venue/LocationsScreen.test.tsx)
   * — pins that the footer leaves the column for the side drawer and stays for the sheet.
   */
  const sideDrawer = Boolean(drawer) && splitFits(width);
  const [availability, setAvailability] = useState<Record<number, number | null>>({});
  // Where the filter bar's band sits in the unscrolled list, so the page can tell a pinned
  // bar from one still slot under the heading and only draw its edge once it is pinned.
  const filterTop = useRef(0);

  const scrollRef = useRef<ScrollView>(null);
  const itemRefs = useRef<Record<number, View | null>>({});
  const didDeepLink = useRef(false);

  const scrollToTop = useCallback(() => {
    scrollRef.current?.scrollTo({ y: 0, animated: true });
  }, []);

  const handleScroll = useCallback(
    (e: NativeSyntheticEvent<NativeScrollEvent>) => {
      setFilterPinned(e.nativeEvent.contentOffset.y > filterTop.current);
      fab.trackScroll(e);
    },
    [fab]
  );

  const loadVenues = useCallback(() => {
    setLoading(true);
    setLoadFailed(false);
    fetchVenues()
      .then((data) => {
        setVenues(data);
        setLoading(false);
      })
      .catch(() => {
        setLoadFailed(true);
        setLoading(false);
      });
  }, []);

  useEffect(loadVenues, [loadVenues]);

  // A pull keeps the list on screen while it reloads: swapping in the spinner would drop the
  // guest back to a blank page for a gesture that means "same page, newer numbers".
  const refresh = useCallback(() => {
    setRefreshing(true);
    fetchVenues()
      .then((data) => {
        setVenues(data);
        setGeneration((g) => g + 1);
      })
      .catch(() => {})
      .finally(() => setRefreshing(false));
  }, []);

  // Every location under one brand shares a clock for the purposes of this page;
  // the first location's timezone is what "today" means in the filter bar.
  const today = useMemo(() => getVenueDate(venues[0]?.timezone ?? "UTC"), [venues]);
  const date = dateOverride ?? today;

  const registerRef = useCallback((id: number, ref: View | null) => {
    itemRefs.current[id] = ref;
  }, []);

  const scrollTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (scrollTimer.current) clearTimeout(scrollTimer.current);
    },
    []
  );

  const scrollToItem = useCallback((id: number, delay: number) => {
    if (scrollTimer.current) clearTimeout(scrollTimer.current);
    // let the accordion's expand tween settle before measuring
    scrollTimer.current = setTimeout(() => {
      scrollIntoView({ current: itemRefs.current[id] ?? null }, scrollRef, { block: "start" });
    }, delay);
  }, []);

  const handleExpand = useCallback(
    (id: number) => {
      scrollToItem(id, 150);
    },
    [scrollToItem]
  );

  const handleAvailability = useCallback((id: number, count: number | null) => {
    setAvailability((prev) => (prev[id] === count ? prev : { ...prev, [id]: count }));
  }, []);

  const handleBook = useCallback((venue: VenueDto, time: string) => {
    setDrawer({ venue, time });
  }, []);

  // Switching location keeps the time the guest was looking at; the form re-asks for
  // availability and moves to the nearest bookable slot when the new location can't take it.
  const handleDrawerVenueChange = useCallback((venue: VenueDto) => {
    setDrawer((current) => (current ? { ...current, venue } : current));
  }, []);

  const closeDrawer = useCallback(() => setDrawer(null), []);

  const handleJoinWaitlist = useCallback((venue: VenueDto) => {
    setDrawer({ venue, time: "", waitlist: true });
  }, []);

  // Deep link: scroll the highlighted location into view, and when the link carried a
  // time, open its booking drawer straight away — that link's intent is to book.
  useEffect(() => {
    if (loading || didDeepLink.current || !highlightId) return;
    const target = venues.find((r) => r.id === highlightId);
    if (!target) return;
    didDeepLink.current = true;
    scrollToItem(highlightId, 220);
    if (initialWaitlist) setDrawer({ venue: target, time: "", waitlist: true });
    else if (initialTime) setDrawer({ venue: target, time: initialTime });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loading, venues, highlightId, initialTime, initialWaitlist]);

  if (loading) {
    return <PageLoader />;
  }

  if (loadFailed) {
    return (
      <ThemedView style={styles.loadingRoot}>
        <ThemedText
          role="alert"
          accessibilityLiveRegion="assertive"
          style={[styles.emptyText, { color: colors.muted }]}
        >
          {t("venue.locationsScreen.couldntLoad")}
        </ThemedText>
        <Button variant="secondary" size="md" onPress={loadVenues}>
          {t("venue.locationsScreen.tryAgain")}
        </Button>
      </ThemedView>
    );
  }

  const summary = availabilitySummary(availability, venues.length);
  // Where the navbar's own content ends: its column is capped at CONTENT_MAX_WIDTH but
  // tracks the viewport below that, and it insets its contents by CONTENT_PADDING_H
  // either side. Matching it is what puts the drawer's edge under the overflow menu.
  const contentWidth = Math.min(width, CONTENT_MAX_WIDTH) - CONTENT_PADDING_H * 2;

  const filterBar = (
    <LocationsFilterBar
      partySize={partySize}
      onPartySizeChange={setPartySize}
      date={date}
      onDateChange={setDateOverride}
      today={today}
      timeWindow={timeWindow}
      onTimeWindowChange={setTimeWindow}
      summary={summary}
      compact={isCompact}
      raised={filterPinned}
    />
  );

  const list = (
    <View style={styles.list}>
      {venues.map((r) => (
        <LocationListItem
          key={`${r.id}:${generation}`}
          venue={r}
          partySize={partySize}
          date={date}
          timeWindow={timeWindow}
          today={today}
          compact={isCompact}
          defaultExpanded={highlightId === r.id}
          registerRef={registerRef}
          onExpand={handleExpand}
          onBook={handleBook}
          onJoinWaitlist={handleJoinWaitlist}
          onAvailabilityChange={handleAvailability}
        />
      ))}
    </View>
  );

  const emptyState = (
    <ThemedView style={styles.empty}>
      <ThemedText style={[styles.emptyText, { color: colors.muted }]}>
        {t("venue.locationsScreen.noLocationsYet")}
      </ThemedText>
    </ThemedView>
  );

  // A tab root: off web it draws with no native header, so the heading is the screen's top.
  const heading = (
    <ScreenHeading
      standalone
      title={t("venue.locationsScreen.title")}
      subtitle={t("venue.locationsScreen.subtitle")}
    />
  );

  const measureFilterBand = (e: { nativeEvent: { layout: { y: number } } }) => {
    filterTop.current = e.nativeEvent.layout.y;
  };

  // The same inset PageContainer gives its column, so the band the bar pins in lines up with
  // the heading above it and the cards below.
  const pageInset = isCompact ? theme.spacing.lg : theme.spacing.xxl;

  /**
   * Off web the bar pins by `stickyHeaderIndices`, which counts the ScrollView's direct
   * children — a fragment would count as one and pin whatever came after it — so the page is
   * three siblings there: heading, band, list. Web keeps its `position: sticky` band inside
   * the one container it always had.
   *
   * @see [LocationsScreen.test.tsx](../../tests/components/venue/LocationsScreen.test.tsx)
   * — pins that the native band is the ScrollView's own second child and is the one pinned.
   */
  const nativeHead = (
    <View style={[styles.nativeSection, styles.nativeHead, { paddingHorizontal: pageInset }]}>
      <View style={styles.nativeColumn}>
        {heading}
        {venues.length === 0 && emptyState}
      </View>
    </View>
  );

  const nativeBand = venues.length > 0 && (
    <View
      testID="locations-filter-sticky"
      onLayout={measureFilterBand}
      style={[styles.nativeFilterBand, { backgroundColor: colors.page }]}
    >
      <View style={[styles.nativeColumn, { paddingHorizontal: pageInset }]}>
        {filterBar}
        {/* The compact bar has no room for the summary, so it goes under the bar here. */}
        {isCompact && summary ? (
          <ThemedText
            testID="locations-native-summary"
            style={[styles.nativeSummary, { color: colors.muted }]}
            role="status"
            accessibilityLiveRegion="polite"
          >
            {summary}
          </ThemedText>
        ) : null}
      </View>
    </View>
  );

  const nativeBody = venues.length > 0 && (
    <View style={[styles.nativeSection, styles.nativeBody, { paddingHorizontal: pageInset }]}>
      <View style={styles.nativeColumn}>{list}</View>
    </View>
  );

  const webContent = (
    <PageContainer style={styles.page}>
      {heading}

      {venues.length === 0 ? (
        emptyState
      ) : (
        <>
          <View
            testID="locations-filter-sticky"
            onLayout={measureFilterBand}
            style={[styles.filterSticky, filterPinned && pinnedMask(pageRgb)]}
          >
            {filterBar}
          </View>
          {list}
        </>
      )}
    </PageContainer>
  );

  const onWeb = Platform.OS === "web";
  /**
   * Who owns the space above and below the list. A native header already insets its own
   * content, so the header case hands both edges to the scroll view
   * (`contentInsetAdjustmentBehavior`), which is also what lets iOS collapse the large title as
   * the list moves; the header-less root has nothing above it and pads the status bar and the
   * tab bar itself. Doing both at once double-pads either edge.
   *
   * @see [LocationsScreen.test.tsx](../../tests/components/venue/LocationsScreen.test.tsx)
   * — pins each route taking exactly one of the two, at the top and at the bottom.
   */
  const headerOwnsInset = !onWeb && hasNativeHeader;

  return (
    <ThemedView style={styles.root}>
      <View
        testID="locations-row"
        style={[styles.row, sideDrawer && [styles.rowWithDrawer, { maxWidth: contentWidth }]]}
      >
        {/* Header-less off web, the column starts under the status bar rather than at the top
            of the display; the inset sits outside the ScrollView so the pinned filter band
            pins below the bar instead of under it. */}
        <View
          testID="locations-list-column"
          style={[styles.listColumn, !onWeb && !headerOwnsInset && { paddingTop: insets.top }]}
        >
          <ScrollView
            ref={scrollRef}
            style={styles.scroll}
            contentContainerStyle={[
              styles.scrollContent,
              !headerOwnsInset && { paddingBottom: tabBarClearance },
            ]}
            showsVerticalScrollIndicator={false}
            onScroll={handleScroll}
            scrollEventThrottle={16}
            contentInsetAdjustmentBehavior={headerOwnsInset ? "automatic" : undefined}
            stickyHeaderIndices={!onWeb && venues.length > 0 ? [1] : undefined}
            refreshControl={
              onWeb ? undefined : (
                <RefreshControl
                  refreshing={refreshing}
                  onRefresh={refresh}
                  tintColor={primaryColor}
                  colors={[primaryColor]}
                />
              )
            }
          >
            {onWeb ? webContent : nativeHead}
            {!onWeb && nativeBand}
            {!onWeb && nativeBody}

            <ScrollToTopFab visible={fab.visible} onPress={scrollToTop} />
            {!sideDrawer && <Footer />}
          </ScrollView>
        </View>

        {drawer && (
          <BookingDrawer
            venue={drawer.venue}
            venues={venues}
            onVenueChange={handleDrawerVenueChange}
            partySize={partySize}
            date={date}
            time={drawer.time}
            today={today}
            variant={splitFits(width) ? "side" : "sheet"}
            onPartySizeChange={setPartySize}
            onDateChange={setDateOverride}
            onClose={closeDrawer}
            onJoinWaitlist={() => handleJoinWaitlist(drawer.venue)}
            waitlist={
              drawer.waitlist
                ? {
                    entryRef: waitlistTickets[drawer.venue.id],
                    onJoined: (entryRef) =>
                      setWaitlistTickets(rememberWaitlistTicket(drawer.venue.id, entryRef)),
                    onReset: () =>
                      setWaitlistTickets(forgetWaitlistTicket(waitlistTickets[drawer.venue.id])),
                  }
                : undefined
            }
          />
        )}
      </View>

      {sideDrawer && <Footer />}
    </ThemedView>
  );
}
