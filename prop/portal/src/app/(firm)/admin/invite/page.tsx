import { InviteForm } from "@/components/InviteForm";

// An administrator's invitation email links here, for example /admin/invite?token=<token>.
export default async function AdminInvitePage({ searchParams }: PageProps<"/admin/invite">) {
  const { token } = await searchParams;
  return <InviteForm role="admin" token={typeof token === "string" && token.length > 0 ? token : null} />;
}
