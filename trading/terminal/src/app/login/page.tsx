import { LoginForm } from "@/components/LoginForm";

// The firm's portal links here with its server chosen, for example /login?server=demo-firm.
export default async function LoginPage({ searchParams }: PageProps<"/login">) {
  const { server } = await searchParams;
  return <LoginForm requestedServer={typeof server === "string" && server.length > 0 ? server : null} />;
}
