import { OpsFirm } from "@/components/OpsFirm";
import { opsFirmTabs } from "@/lib/ops";

// One firm for our staff, for example /ops/firms/acme, on a tab with ?tab=billing.
export default async function OpsFirmPage({ params, searchParams }: PageProps<"/ops/firms/[id]">) {
  const { id } = await params;
  const { tab } = await searchParams;
  return <OpsFirm firmId={id} initialTab={opsFirmTabs.find((t) => t === tab) ?? null} />;
}
