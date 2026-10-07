import type { AccountDetails } from "./api/types";
import { formatDate, formatMoney } from "./format";
import { defaultColors, type ThemeColor } from "./theme";

// Certificates for a passed challenge and for each paid payout (finding 86), drawn as an image the trader can download
// and share, in the firm's name, logo and colors.

export type Certificate = {
  key: string;
  kind: "passed" | "payout";
  title: string;
  headline: string;
  /** The large line: the amount paid out, or the challenge passed, with the words above it. */
  figure: string;
  figureLabel: string;
  detail: string;
  date: string;
  fileName: string;
};

/** The account's certificates: one once every evaluation stage is passed, and one for every paid payout, oldest first. */
export function certificatesOf(details: AccountDetails): Certificate[] {
  const { account, challenge, stages, payouts } = details;
  const timeZone = challenge.tradingDay.timeZone;
  const size = `${formatMoney(account.initialBalance)} ${account.currency}`;
  const certificates: Certificate[] = [];

  const evaluation = stages.filter((s) => s.stage < challenge.evaluation.length);
  const passedAt = evaluation.at(-1)?.passedAt;
  if (evaluation.length > 0 && evaluation.every((s) => s.progress === "Passed") && passedAt) {
    certificates.push({
      key: "passed",
      kind: "passed",
      title: "Certificate of achievement",
      headline: `Passed the ${challenge.name} challenge`,
      figure: challenge.name,
      figureLabel: "Passed the challenge",
      detail: `Account #${account.number}, ${size}`,
      date: formatDate(passedAt, timeZone),
      fileName: `certificate-${account.number}-passed.png`,
    });
  }

  const paid = payouts.filter((p) => p.status === "Paid" && p.paidAt).sort((a, b) => Date.parse(a.paidAt!) - Date.parse(b.paidAt!));
  paid.forEach((payout, index) => {
    certificates.push({
      key: `payout-${payout.id}`,
      kind: "payout",
      title: "Payout certificate",
      headline: `Paid out ${formatMoney(payout.amount)} ${payout.currency}`,
      figure: `${formatMoney(payout.amount)} ${payout.currency}`,
      figureLabel: "Paid out",
      detail: `${challenge.name}, account #${account.number}${paid.length > 1 ? `, payout ${index + 1}` : ""}`,
      date: formatDate(payout.paidAt!, timeZone),
      fileName: `certificate-${account.number}-payout-${index + 1}.png`,
    });
  });

  return certificates;
}

export const certificateWidth = 1600;
export const certificateHeight = 1000;

/** The firm's colors the certificate is drawn in, so it looks like the firm's portal. */
export type CertificateColors = Record<"background" | "panel" | "foreground" | "muted" | "accent", string>;

/** The firm's colors for a certificate, each a plain six-digit hex color, with ours where the firm has none. */
export function certificateColors(colors: Partial<Record<ThemeColor, string>>): CertificateColors {
  const pick = (name: keyof CertificateColors) => {
    const color = colors[name];
    return color && /^#[0-9a-fA-F]{6}$/.test(color) ? color : defaultColors[name];
  };
  return { background: pick("background"), panel: pick("panel"), foreground: pick("foreground"), muted: pick("muted"), accent: pick("accent") };
}

// Text stands left, with a seal on the right, so the name and the figure have this much width.
const textLeft = 120;
const textWidth = 1000;

/** The certificate as an SVG image, with the firm's logo as a data URL when it has one. */
export function certificateSvg(certificate: Certificate, firmName: string, traderName: string, colors: CertificateColors, logo: string | null): string {
  const { background, panel, foreground, muted, accent } = colors;
  const w = certificateWidth;
  const h = certificateHeight;
  const brand = logo
    ? `<image href="${escapeXml(logo)}" x="${textLeft}" y="104" width="380" height="96" preserveAspectRatio="xMinYMid meet"/>`
    : `<text x="${textLeft}" y="168" font-size="44" font-weight="700" fill="${foreground}">${escapeXml(firmName)}</text>`;
  const seal = certificate.kind === "payout" ? "PAID OUT" : "PASSED";
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" font-family="Helvetica, Arial, sans-serif">
<defs>
<radialGradient id="glow" cx="0" cy="0" r="1" gradientUnits="userSpaceOnUse" gradientTransform="translate(160 80) scale(1250 900)">
<stop offset="0" stop-color="${accent}" stop-opacity="0.30"/>
<stop offset="1" stop-color="${accent}" stop-opacity="0"/>
</radialGradient>
<linearGradient id="rule" x1="0" x2="1" y1="0" y2="0">
<stop offset="0" stop-color="${accent}"/>
<stop offset="1" stop-color="${accent}" stop-opacity="0"/>
</linearGradient>
</defs>
<rect width="${w}" height="${h}" fill="${background}"/>
<rect width="${w}" height="${h}" fill="url(#glow)"/>
<rect x="40" y="40" width="${w - 80}" height="${h - 80}" rx="28" fill="${panel}" fill-opacity="0.55" stroke="${accent}" stroke-opacity="0.4" stroke-width="2"/>
${brand}
<g transform="translate(1290 360)">
<circle r="168" fill="${accent}" fill-opacity="0.10" stroke="${accent}" stroke-opacity="0.55" stroke-width="2"/>
<circle r="144" fill="none" stroke="${accent}" stroke-opacity="0.45" stroke-width="2" stroke-dasharray="3 9"/>
<path d="M-46 4 -14 36 50 -34" fill="none" stroke="${accent}" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
<text y="-78" text-anchor="middle" font-size="24" font-weight="700" letter-spacing="6" fill="${accent}">${seal}</text>
<text y="100" text-anchor="middle" font-size="24" letter-spacing="2" fill="${muted}">${escapeXml(certificate.date)}</text>
</g>
<text x="${textLeft}" y="300" font-size="26" font-weight="700" letter-spacing="7" fill="${accent}">${escapeXml(certificate.title.toUpperCase())}</text>
<text x="${textLeft}" y="372" font-size="28" fill="${muted}">Awarded to</text>
<text x="${textLeft}" y="470" font-size="${nameSize(traderName)}" font-family="Georgia, 'Times New Roman', serif" fill="${foreground}">${escapeXml(traderName)}</text>
<rect x="${textLeft}" y="512" width="880" height="3" fill="url(#rule)"/>
<text x="${textLeft}" y="612" font-size="28" fill="${muted}">${escapeXml(certificate.figureLabel)}</text>
<text x="${textLeft}" y="${612 + figureSize(certificate.figure) + 6}" font-size="${figureSize(certificate.figure)}" font-weight="700" fill="${foreground}">${escapeXml(certificate.figure)}</text>
<text x="${textLeft}" y="850" font-size="28" fill="${muted}">${escapeXml(certificate.detail)}</text>
<text x="${textLeft}" y="${h - 100}" font-size="24" fill="${muted}">${escapeXml(firmName)}</text>
</svg>`;
}

/**
 * The size of the trader's name, so that it fits beside the seal: 84 for a short name, smaller for a long one. A
 * letter or digit in the serif font is at most about 0.6 of its size wide.
 */
export function nameSize(name: string): number {
  return Math.max(32, Math.min(84, Math.floor(textWidth / (0.6 * Math.max(name.length, 1)))));
}

/** The size of the large figure, such as the amount paid out, so it fits beside the seal: at most 112. */
export function figureSize(figure: string): number {
  return Math.max(44, Math.min(112, Math.floor(textWidth / (0.62 * Math.max(figure.length, 1)))));
}

/** What a trader says when sharing a certificate, such as "I passed the Two-step 100K challenge with Aurora Funded." */
export function shareText(certificate: Certificate, firmName: string): string {
  return certificate.kind === "payout"
    ? `I got a payout of ${certificate.figure} from ${firmName}.`
    : `I passed the ${certificate.figure} challenge with ${firmName}.`;
}

export function escapeXml(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&apos;");
}
