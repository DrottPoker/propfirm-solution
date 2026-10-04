import { redirect } from "next/navigation";

import { PlatformGate } from "@/components/PlatformGate";
import { SandboxBanner } from "@/components/SandboxBanner";
import { Message } from "@/components/ui";
import { getSite } from "@/lib/site";

import { PlatformProviders, Providers } from "../providers";

// A firm's portal. On the platform's own address and our admin view's there is no firm: the platform shows its front
// page and login instead, and our admin view's address goes to it.
export default async function FirmLayout({ children }: LayoutProps<"/">) {
  const site = await getSite();
  if (site.kind === "platform") {
    return (
      <PlatformProviders platform={site.platform}>
        <PlatformGate />
      </PlatformProviders>
    );
  }

  if (site.kind === "ops") {
    redirect("/ops");
  }

  if (site.kind !== "firm") {
    return <Message text={site.kind === "unknown-host" ? "No firm's portal is at this address." : "The portal cannot be reached right now. Try again shortly."} />;
  }

  return (
    <Providers branding={site.branding}>
      <SandboxBanner />
      {children}
    </Providers>
  );
}
