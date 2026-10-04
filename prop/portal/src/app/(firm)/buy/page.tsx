import { Shop } from "@/components/Shop";

// Open to everyone on the firm's portal: buying is how a new trader arrives. ?challenge= chooses a challenge and ?code=
// fills in a discount code, for example from "Try again" on a failed account.
export default async function BuyPage({ searchParams }: PageProps<"/buy">) {
  const { challenge, code } = await searchParams;
  return <Shop challenge={typeof challenge === "string" ? challenge : null} code={typeof code === "string" ? code : null} />;
}
