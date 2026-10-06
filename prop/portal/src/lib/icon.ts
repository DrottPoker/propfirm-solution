import { isHexColor, readableTextOn } from "./theme";

/**
 * The icon in the browser's tab: the first letter of the name on the brand color, as an SVG data URL. A square that
 * stays sharp at any size, which a firm's logo of any shape would not.
 */
export function monogramIcon(name: string, color: string): string {
  const background = isHexColor(color) ? color : "#2563eb";
  const letter = Array.from(name.trim())[0]?.toUpperCase() ?? "?";
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">` +
    `<rect width="64" height="64" rx="14" fill="${background}"/>` +
    `<text x="32" y="44" text-anchor="middle" font-family="system-ui,-apple-system,Segoe UI,sans-serif" font-size="36" font-weight="700" fill="${readableTextOn(background)}">${escapeXml(letter)}</text>` +
    `</svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

function escapeXml(text: string): string {
  return text.replace(/[<>&"']/g, (c) => `&#${c.charCodeAt(0)};`);
}

/** Kronant's mark: a crown on a brass coin, the krona it is named after. Drawn on 32 by 32. */
export const kronantMarkPaths = {
  coin: "M16 1.5a14.5 14.5 0 1 1 0 29 14.5 14.5 0 0 1 0-29Z",
  crown: "M8.5 21.5V12l4.4 3.6L16 9.2l3.1 6.4 4.4-3.6v9.5Z",
  band: "M8.5 23.2h15",
} as const;

/** Our mark as the tab's icon, on the platform and in our admin view. */
export const kronantIcon = `data:image/svg+xml,${encodeURIComponent(
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">` +
    `<path d="${kronantMarkPaths.coin}" fill="#c9a35b"/>` +
    `<path d="${kronantMarkPaths.crown}" fill="#15120c"/>` +
    `<path d="${kronantMarkPaths.band}" stroke="#15120c" stroke-width="1.6" stroke-linecap="round"/>` +
    `</svg>`,
)}`;
