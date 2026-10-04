import { LoginForm } from "@/components/LoginForm";
import { safeNext } from "@/lib/next";

// For example /login?next=/accounts/<id>, from a link in an email or the trading terminal.
export default async function LoginPage({ searchParams }: PageProps<"/login">) {
  const { next } = await searchParams;
  return <LoginForm role="trader" next={safeNext(next)} />;
}
