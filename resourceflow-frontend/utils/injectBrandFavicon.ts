import { buildFaviconDataUri } from "@/constants/faviconIcons";
import { Brand } from "@/types";

export function injectBrandFavicon(brand: Brand): void {
  /* istanbul ignore next */
  if (typeof document === "undefined") return;

  const { faviconIcon, primaryColor, appName } = brand;

  // Always patch the SW-cached manifest so the PWA install prompt and installed
  // app title reflect the brand name and theme color, even when no icon is set.
  navigator.serviceWorker?.controller?.postMessage({
    type: "BRAND_UPDATE",
    brand: { name: appName, themeColor: primaryColor },
  });

  if (!faviconIcon) return;

  const coloredSvg = buildFaviconDataUri(faviconIcon, primaryColor);
  if (!coloredSvg) return;

  // Remove all existing favicon links so Chrome picks up the new one.
  // Updating href in-place is not enough — Chrome only re-reads the favicon
  // when a link element is newly added to the DOM.
  document
    .querySelectorAll('link[rel="icon"], link[rel="shortcut icon"]')
    .forEach((el) => el.remove());
  const iconLink = document.createElement("link");
  iconLink.rel = "icon";
  iconLink.type = "image/svg+xml";
  iconLink.href = coloredSvg;
  document.head.appendChild(iconLink);

  // Browser chrome colour on mobile
  let themeMeta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
  if (!themeMeta) {
    themeMeta = document.createElement("meta");
    themeMeta.name = "theme-color";
    document.head.appendChild(themeMeta);
  }
  themeMeta.content = primaryColor;
}
