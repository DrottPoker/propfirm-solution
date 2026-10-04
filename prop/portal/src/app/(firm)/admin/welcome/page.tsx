import { AdminWelcome } from "@/components/AdminWelcome";
import { safeNext } from "@/lib/next";

// Signing up ends here, on the firm's own address, for example /admin/welcome?token=<token>&next=/admin/get-started.
export default async function AdminWelcomePage({ searchParams }: PageProps<"/admin/welcome">) {
  const { token, next } = await searchParams;
  return <AdminWelcome token={typeof token === "string" && token.length > 0 ? token : null} next={safeNext(next)} />;
}
