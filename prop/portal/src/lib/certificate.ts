import type { AccountDetails } from "./api/types";
import { formatDate, formatMoney } from "./format";

// Certificates for a passed challenge and for each paid payout (finding 86), drawn as an image the trader can download
// and share, in the firm's name and color.

export type Certificate = {
  key: string;
  title: string;
  headline: string;
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
      title: "Certificate of achievement",
      headline: `Passed the ${challenge.name} challenge`,
      detail: `Account #${account.number} · ${size}`,
      date: formatDate(passedAt, timeZone),
      fileName: `certificate-${account.number}-passed.png`,
    });
  }

  const paid = payouts.filter((p) => p.status === "Paid" && p.paidAt).sort((a, b) => Date.parse(a.paidAt!) - Date.parse(b.paidAt!));
  paid.forEach((payout, index) => {
    certificates.push({
      key: `payout-${payout.id}`,
      title: "Payout certificate",
      headline: `Paid out ${formatMoney(payout.amount)} ${payout.currency}`,
      detail: `${challenge.name} · account #${account.number}${paid.length > 1 ? ` · payout ${index + 1}` : ""}`,
      date: formatDate(payout.paidAt!, timeZone),
      fileName: `certificate-${account.number}-payout-${index + 1}.png`,
    });
  });

  return certificates;
}

export const certificateWidth = 1600;
export const certificateHeight = 1000;

/** The certificate as an SVG image, with the firm's logo as a data URL when it has one. */
export function certificateSvg(certificate: Certificate, firmName: string, traderName: string, accent: string, logo: string | null): string {
  const color = /^#[0-9a-fA-F]{6}$/.test(accent) ? accent : "#2563eb";
  const w = certificateWidth;
  const h = certificateHeight;
  const brand = logo
    ? `<image href="${escapeXml(logo)}" x="${w / 2 - 160}" y="110" width="320" height="90" preserveAspectRatio="xMidYMid meet"/>`
    : `<text x="${w / 2}" y="170" text-anchor="middle" font-size="44" font-weight="700" fill="#0f172a">${escapeXml(firmName)}</text>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" font-family="Helvetica, Arial, sans-serif">
<rect width="${w}" height="${h}" fill="#ffffff"/>
<rect x="36" y="36" width="${w - 72}" height="${h - 72}" fill="none" stroke="${color}" stroke-width="10"/>
<rect x="60" y="60" width="${w - 120}" height="${h - 120}" fill="none" stroke="${color}" stroke-opacity="0.35" stroke-width="2"/>
${brand}
<text x="${w / 2}" y="300" text-anchor="middle" font-size="34" letter-spacing="8" fill="${color}">${escapeXml(certificate.title.toUpperCase())}</text>
<text x="${w / 2}" y="390" text-anchor="middle" font-size="28" fill="#475569">This certifies that</text>
<text x="${w / 2}" y="500" text-anchor="middle" font-size="${nameSize(traderName)}" font-family="Georgia, 'Times New Roman', serif" fill="#0f172a">${escapeXml(traderName)}</text>
<line x1="${w / 2 - 380}" y1="540" x2="${w / 2 + 380}" y2="540" stroke="#cbd5e1" stroke-width="2"/>
<text x="${w / 2}" y="630" text-anchor="middle" font-size="48" font-weight="700" fill="#0f172a">${escapeXml(certificate.headline)}</text>
<text x="${w / 2}" y="700" text-anchor="middle" font-size="30" fill="#475569">${escapeXml(certificate.detail)}</text>
<text x="${w / 2}" y="840" text-anchor="middle" font-size="28" fill="#475569">${escapeXml(certificate.date)} · ${escapeXml(firmName)}</text>
</svg>`;
}

/**
 * The size of the trader's name, so that it fits inside the frame: 84 for a short name, smaller for a long one such as
 * an email address. A letter or digit in the serif font is at most about 0.6 of its size wide.
 */
export function nameSize(name: string): number {
  return Math.max(32, Math.min(84, Math.floor((certificateWidth - 300) / (0.6 * Math.max(name.length, 1)))));
}

export function escapeXml(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&apos;");
}
