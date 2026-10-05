import { AdminNewTicket } from "@/components/AdminSupport";

// A trader's card links here with ?email= and ?account=, so the ticket goes to that trader about that account.
export default async function AdminNewTicketPage({ searchParams }: PageProps<"/admin/support/new">) {
  const { email, account } = await searchParams;
  return <AdminNewTicket initialEmail={typeof email === "string" ? email : ""} initialAccountId={typeof account === "string" ? account : null} />;
}
