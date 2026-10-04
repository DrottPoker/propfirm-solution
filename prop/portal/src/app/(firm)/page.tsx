import { redirect } from "next/navigation";

import { PortalHome } from "@/components/PortalHome";

// Links from before the accounts had pages of their own, such as /?account=<id>, open the account's page. Visitors go to the shop.
export default async function Page({ searchParams }: PageProps<"/">) {
  const { account } = await searchParams;
  if (typeof account === "string" && account.length > 0) {
    redirect(`/accounts/${encodeURIComponent(account)}`);
  }

  return <PortalHome />;
}
