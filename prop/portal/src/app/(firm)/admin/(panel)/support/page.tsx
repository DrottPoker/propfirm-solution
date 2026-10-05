import { AdminTickets } from "@/components/AdminSupport";
import type { SupportTicketGroup } from "@/lib/api/types";
import { supportGroups } from "@/lib/support";

// The overview links to the tickets waiting for the firm, and a trader's card to their tickets with ?group=All&search=.
export default async function AdminSupportPage({ searchParams }: PageProps<"/admin/support">) {
  const { group, search } = await searchParams;
  return (
    <AdminTickets
      initialGroup={supportGroups.find((g) => g === group) ?? ("Open" satisfies SupportTicketGroup)}
      initialSearch={typeof search === "string" ? search : ""}
    />
  );
}
