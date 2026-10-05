import { RequireRole } from "@/components/RequireRole";
import { NewTicket } from "@/components/TraderSupport";

// An account's page links here with ?account=, so the ticket is about that account.
export default async function NewTicketPage({ searchParams }: PageProps<"/support/new">) {
  const { account } = await searchParams;
  return <RequireRole role="trader"><NewTicket initialAccountId={typeof account === "string" ? account : null} /></RequireRole>;
}
