import { AdminVerification } from "@/components/AdminVerification";

// The payment provider sends the firm back here after paying the deposit, with ?checkout=<id>.
export default async function AdminVerificationPage({ searchParams }: PageProps<"/admin/verification">) {
  const { checkout } = await searchParams;
  return <AdminVerification returnedFromCheckout={typeof checkout === "string" && checkout.length > 0} />;
}
