import { AdminTicket } from "@/components/AdminSupport";

export default async function AdminTicketPage({ params }: PageProps<"/admin/support/[id]">) {
  const { id } = await params;
  return <AdminTicket ticketId={id} />;
}
