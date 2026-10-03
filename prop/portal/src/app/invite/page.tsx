import { InviteForm } from "@/components/InviteForm";

// The firm's invitation email links here, for example /invite?token=<token>.
export default async function InvitePage({ searchParams }: PageProps<"/invite">) {
  const { token } = await searchParams;
  return <InviteForm token={typeof token === "string" && token.length > 0 ? token : null} />;
}
