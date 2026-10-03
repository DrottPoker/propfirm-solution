// Helpers for the sign-up form. Prop.Api decides; these only guide the person typing.

const firmIdPattern = /^[a-z0-9][a-z0-9-]{0,38}[a-z0-9]$/;

/** 2 to 40 lowercase letters, digits and dashes, not first or last, as Prop.Api requires. */
export function isValidFirmId(firmId: string): boolean {
  return firmIdPattern.test(firmId);
}

/** A short name made from the firm's name, for example "Nordic Prop AB" becomes "nordic-prop-ab". */
export function suggestFirmId(firmName: string): string {
  return firmName
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 40)
    .replace(/-+$/, "");
}

/** The address the firm's portal will have, from the platform's template. */
export function portalAddress(template: string, firmId: string): string {
  return template.replace("{firm}", firmId || "your-firm");
}
