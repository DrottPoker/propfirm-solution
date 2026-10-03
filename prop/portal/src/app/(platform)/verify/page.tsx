import { VerifySignup } from "@/components/VerifySignup";

// The confirmation email links here, for example /verify?token=<token>.
export default async function VerifyPage({ searchParams }: PageProps<"/verify">) {
  const { token } = await searchParams;
  return <VerifySignup token={typeof token === "string" && token.length > 0 ? token : null} />;
}
