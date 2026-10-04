import { LoginForm } from "@/components/LoginForm";
import { safeNext } from "@/lib/next";

// For example /admin/login?next=/admin/payouts, from a link in an email.
export default async function AdminLoginPage({ searchParams }: PageProps<"/admin/login">) {
  const { next } = await searchParams;
  return <LoginForm role="admin" next={safeNext(next)} />;
}
