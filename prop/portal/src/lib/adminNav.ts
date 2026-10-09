import type { FirmStatus } from "./api/types";

/** A page of the admin panel, as the menu, the settings and the search name it. */
export type AdminPlace = { href: string; label: string; description: string; keywords?: string[] };

/** The daily work, in the menu. */
export const dailyPlaces: AdminPlace[] = [
  { href: "/admin", label: "Overview", description: "What needs you, and how the firm is doing." },
  { href: "/admin/accounts", label: "Accounts", description: "Every challenge account, by group.", keywords: ["traders", "challenges"] },
  { href: "/admin/payouts", label: "Payouts", description: "Payouts to approve and to pay.", keywords: ["withdrawals"] },
  { href: "/admin/support", label: "Support", description: "Tickets from your traders.", keywords: ["tickets", "help"] },
  { href: "/admin/incidents", label: "Incidents", description: "Outages of the trading platform, and what you did about them.", keywords: ["outage", "status", "reinstate"] },
  { href: "/admin/orders", label: "Orders", description: "Challenges bought in your portal.", keywords: ["sales", "purchases"] },
  { href: "/admin/challenges", label: "Challenges", description: "What your firm sells, its rules and prices.", keywords: ["rules", "prices"] },
  { href: "/admin/discounts", label: "Discount codes", description: "Codes buyers type in your shop.", keywords: ["coupons", "promo"] },
];

/** The firm's own settings, together under Settings in the menu and as tabs on each of them. */
export const settingsPlaces: AdminPlace[] = [
  { href: "/admin/design", label: "Portal design", description: "Your logo, theme and brand color.", keywords: ["logo", "colors", "theme", "branding"] },
  { href: "/admin/domain", label: "Your domain", description: "Your portal on an address of your own.", keywords: ["dns", "url"] },
  { href: "/admin/checkout", label: "Checkout", description: "How traders pay in your shop.", keywords: ["stripe", "payments"] },
  { href: "/admin/trading", label: "Trading conditions", description: "Instruments, leverage, spreads and commission.", keywords: ["symbols", "leverage"] },
  { href: "/admin/terminal", label: "Terminal", description: "How orders start in your traders' terminal.", keywords: ["orders", "confirm", "lots", "size"] },
  { href: "/admin/identity", label: "KYC", description: "How your traders' identity is checked.", keywords: ["identity", "id"] },
  { href: "/admin/notifications", label: "Notifications", description: "The emails we send for you.", keywords: ["emails"] },
  { href: "/admin/integrations", label: "Integrations", description: "Your firm API, keys and webhooks.", keywords: ["api", "webhooks"] },
  { href: "/admin/team", label: "Team", description: "The people who run your firm here.", keywords: ["administrators", "invite"] },
];

/** Go live until the firm is live, and Plan and billing from then. */
export function liveOrBilling(status: FirmStatus): AdminPlace {
  return status === "Live"
    ? { href: "/admin/billing", label: "Plan and billing", description: "Your slots, payments and invoices.", keywords: ["invoices", "slots"] }
    : { href: "/admin/go-live", label: "Go live", description: "From the sandbox to real traders.", keywords: ["review", "application"] };
}

/** Whether the path is the place or a page under it. */
export function isUnder(path: string, href: string): boolean {
  return path === href || path.startsWith(`${href}/`);
}

/** Whether the path is one of the settings, or the page with all of them. */
export function isSettingsPath(path: string): boolean {
  return path === "/admin/settings" || settingsPlaces.some((place) => isUnder(path, place.href));
}
