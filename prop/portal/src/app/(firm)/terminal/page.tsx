import { TerminalLogin } from "@/components/TerminalLogin";

// The trading terminal sends the firm's traders here to log in, for example /terminal?account=<trading account id>.
export default async function TerminalPage({ searchParams }: PageProps<"/terminal">) {
  const { account } = await searchParams;
  return <TerminalLogin tradingAccountId={typeof account === "string" && account.length > 0 ? account : null} />;
}
