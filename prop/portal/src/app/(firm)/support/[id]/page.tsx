import { RequireRole } from "@/components/RequireRole";
import { TraderTicket } from "@/components/TraderSupport";

export default async function TicketPage({ params }: PageProps<"/support/[id]">) {
  const { id } = await params;
  return <RequireRole role="trader"><TraderTicket ticketId={id} /></RequireRole>;
}
