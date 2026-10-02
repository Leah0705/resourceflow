import { Redirect, useLocalSearchParams, type Href } from "expo-router";

/**
 * `?venueId=` redirector — retargeted from the old `/book/[venueId]`
 * route to the new merged Locations page.
 */
export default function BookQueryRedirect() {
  const { venueId } = useLocalSearchParams<{ venueId?: string }>();
  if (venueId) {
    return <Redirect href={`/(user)/locations/${venueId}` as Href} />;
  }
  return <Redirect href="/" />;
}
