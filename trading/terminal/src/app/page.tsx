import { Terminal } from "@/components/Terminal";

// A trader with several accounts can open one directly, for example /?account=demo.
export default async function Page({ searchParams }: PageProps<"/">) {
  const { account } = await searchParams;
  return <Terminal requestedAccountId={typeof account === "string" && account.length > 0 ? account : null} />;
}
