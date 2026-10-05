import { RequireRole } from "@/components/RequireRole";
import { TestIdentityPage } from "@/components/TraderIdentity";

export default async function TestIdentityCheckPage({ searchParams }: PageProps<"/identity/test">) {
  const { session } = await searchParams;
  return <RequireRole role="trader"><TestIdentityPage sessionId={typeof session === "string" ? session : null} /></RequireRole>;
}
