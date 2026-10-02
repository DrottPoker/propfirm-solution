import { Terminal } from "@/components/Terminal";

// No login yet: the account comes from the address, for example /?account=demo.
export default async function Page({ searchParams }: PageProps<"/">) {
  const { account } = await searchParams;
  const accountId = typeof account === "string" && account.length > 0 ? account : "demo";
  return <Terminal accountId={accountId} />;
}
