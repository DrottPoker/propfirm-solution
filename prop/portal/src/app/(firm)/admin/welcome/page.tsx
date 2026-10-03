import { AdminWelcome } from "@/components/AdminWelcome";

// Signing up ends here, on the firm's own address, for example /admin/welcome?token=<token>.
export default async function AdminWelcomePage({ searchParams }: PageProps<"/admin/welcome">) {
  const { token } = await searchParams;
  return <AdminWelcome token={typeof token === "string" && token.length > 0 ? token : null} />;
}
