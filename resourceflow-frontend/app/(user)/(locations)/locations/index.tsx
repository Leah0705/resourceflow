import { Stack } from "expo-router";
import { useTranslation } from "react-i18next";
import LocationsScreen from "@/components/venue/LocationsScreen";

export default function LocationsIndexScreen() {
  const { t } = useTranslation();

  return (
    <>
      <Stack.Screen options={{ title: t("venue.locationsScreen.routeTitle") }} />
      <LocationsScreen />
    </>
  );
}
