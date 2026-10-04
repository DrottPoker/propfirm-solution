import { LoginForm } from "@/components/LoginForm";

// The firm's portal links here with its server chosen, for example /login?server=demo-firm. After a session ended
// in the terminal, ended=1 sends a trader of a firm with a portal straight back there.
export default async function LoginPage({ searchParams }: PageProps<"/login">) {
  const { server, ended } = await searchParams;
  return <LoginForm requestedServer={typeof server === "string" && server.length > 0 ? server : null} sessionEnded={ended === "1"} />;
}
