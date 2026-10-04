import { AdminVerification } from "@/components/AdminVerification";
import { RequireRole } from "@/components/RequireRole";

// The payment provider sends the firm back here after paying the deposit, with ?checkout=<id>.
export default async function AdminVerificationPage({ searchParams }: PageProps<"/admin/verification">) {
  const { checkout } = await searchParams;
  return (
    <RequireRole role="admin">
      <AdminVerification returnedFromCheckout={typeof checkout === "string" && checkout.length > 0} />
    </RequireRole>
  );
}
