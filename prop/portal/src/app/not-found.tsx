import { ErrorScreen } from "@/components/ErrorScreen";
import { FirmLogo } from "@/components/FirmLogo";
import { getSite } from "@/lib/site";

// An address with no page, on a firm's portal, our platform or our admin view, in its own name and colors.
export default async function NotFound() {
  const site = await getSite();
  const brand =
    site.kind === "firm" ? (
      site.branding.logoUrl ? (
        <FirmLogo src={site.branding.logoUrl} alt={site.branding.name} className="h-10 max-w-[14rem]" />
      ) : (
        <span className="text-lg font-semibold">{site.branding.name}</span>
      )
    ) : site.kind === "platform" ? (
      <span className="text-lg font-semibold">{site.platform.name}</span>
    ) : site.kind === "ops" ? (
      <span className="text-lg font-semibold">{site.ops.name}</span>
    ) : null;

  return (
    <ErrorScreen
      brand={brand}
      title="This page does not exist"
      text="The link may be old or mistyped. Start again from the start page."
      home={site.kind === "ops" ? "/ops" : "/"}
      homeLabel="Go to the start page"
    />
  );
}
