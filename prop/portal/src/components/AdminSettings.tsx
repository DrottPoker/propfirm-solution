"use client";

import Link from "next/link";

import { settingsPlaces } from "@/lib/adminNav";
import type { FirmSettings } from "@/lib/api/types";
import { notificationKinds } from "@/lib/notifications";
import { providerLabels } from "@/lib/orders";
import { useFirmSettings } from "@/lib/queries";

import { CardIcon, ChevronRightIcon, CodeIcon, GlobeIcon, MailIcon, PaletteIcon, ServerIcon, ShieldCheckIcon, TeamIcon } from "./icons";
import { AdminPage, Loading, Message, PageHeader } from "./ui";

const icons: Record<string, (props: { className?: string }) => React.ReactNode> = {
  "/admin/design": PaletteIcon,
  "/admin/domain": GlobeIcon,
  "/admin/checkout": CardIcon,
  "/admin/trading": ServerIcon,
  "/admin/identity": ShieldCheckIcon,
  "/admin/notifications": MailIcon,
  "/admin/integrations": CodeIcon,
  "/admin/team": TeamIcon,
};

/** Where a setting stands, in a few words, when the firm's settings say it. */
function standing(href: string, settings: FirmSettings): string | null {
  switch (href) {
    case "/admin/design":
      return settings.logoUrl ? "Your logo and colors" : "No logo yet";
    case "/admin/checkout":
      return settings.payments.provider ? providerLabels[settings.payments.provider] : "No sales in the portal";
    case "/admin/notifications": {
      const on = notificationKinds.filter((kind) => settings.emailSettings[kind.kind] !== false).length;
      return `${on} of ${notificationKinds.length} emails on`;
    }
    case "/admin/integrations":
      return [settings.hasApiKey ? "API key made" : "No API key", settings.webhookUrl ? "webhooks on" : null].filter(Boolean).join(", ");
    default:
      return null;
  }
}

/** Every setting of the firm as a card, with where it stands. Each one's page has the others as tabs. */
export function AdminSettings() {
  const settings = useFirmSettings();
  if (settings.isError) {
    return <Message text="The settings cannot be loaded right now. Try again shortly." />;
  }

  if (!settings.data) {
    return <Loading />;
  }

  return (
    <AdminPage>
      <PageHeader title="Settings" description="How your portal looks and sells, how your traders trade and are checked, and who runs the firm." />
      <ul className="stagger grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {settingsPlaces.map((place) => {
          const Icon = icons[place.href];
          const now = standing(place.href, settings.data);
          return (
            <li key={place.href}>
              <Link
                href={place.href}
                className="group flex h-full items-start gap-3.5 rounded-xl border border-border bg-panel p-4 shadow-card transition duration-200 ease-out-soft hover:-translate-y-0.5 hover:border-muted/40 hover:shadow-raised"
              >
                <span className="grid size-10 shrink-0 place-items-center rounded-xl border border-border bg-raised text-accent">
                  <Icon className="size-5" />
                </span>
                <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span className="font-medium">{place.label}</span>
                  <span className="text-sm text-muted">{place.description}</span>
                  {now && <span className="mt-1.5 text-xs text-foreground/80">{now}</span>}
                </span>
                <ChevronRightIcon className="mt-1 size-4 text-muted transition-transform duration-200 group-hover:translate-x-0.5" />
              </Link>
            </li>
          );
        })}
      </ul>
    </AdminPage>
  );
}
