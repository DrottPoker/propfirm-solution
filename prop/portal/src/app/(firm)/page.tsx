import { redirect } from "next/navigation";

import { AccountsOverview } from "@/components/AccountsOverview";
import { RequireRole } from "@/components/RequireRole";

// Links from before the accounts had pages of their own, such as /?account=<id>, open the account's page.
export default async function Page({ searchParams }: PageProps<"/">) {
  const { account } = await searchParams;
  if (typeof account === "string" && account.length > 0) {
    redirect(`/accounts/${encodeURIComponent(account)}`);
  }

  return <RequireRole role="trader"><AccountsOverview /></RequireRole>;
}
