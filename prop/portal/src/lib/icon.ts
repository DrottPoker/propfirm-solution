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
