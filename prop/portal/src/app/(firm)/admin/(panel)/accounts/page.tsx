import { AdminAccounts } from "@/components/AdminAccounts";
import { accountGroups } from "@/lib/admin";
import type { AccountGroup } from "@/lib/api/types";

// The overview links to a group of accounts, for example ?group=AwaitingFunding, and an account to its trader's with ?search=.
export default async function AdminAccountsPage({ searchParams }: PageProps<"/admin/accounts">) {
  const { group, search } = await searchParams;
  return (
    <AdminAccounts
      initialGroup={accountGroups.find((g) => g === group) ?? ("All" satisfies AccountGroup)}
      initialSearch={typeof search === "string" ? search : ""}
    />
  );
}
