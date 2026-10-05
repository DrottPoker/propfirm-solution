import { RequireRole } from "@/components/RequireRole";
import { IdentityPage } from "@/components/TraderIdentity";

// The ID provider sends the trader back here with ?returned=1.
export default async function TraderIdentityPage({ searchParams }: PageProps<"/identity">) {
  const { returned } = await searchParams;
  return <RequireRole role="trader"><IdentityPage returned={returned === "1"} /></RequireRole>;
}
