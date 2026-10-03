import { Dashboard } from "@/components/Dashboard";
import { RequireRole } from "@/components/RequireRole";

// A trader with several accounts opens one directly, for example /?account=<id>.
export default async function Page({ searchParams }: PageProps<"/">) {
  const { account } = await searchParams;
  const requested = typeof account === "string" && account.length > 0 ? account : null;
  return <RequireRole role="trader"><Dashboard requestedAccountId={requested} /></RequireRole>;
}
