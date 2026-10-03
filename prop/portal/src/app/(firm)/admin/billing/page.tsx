import { AdminBilling } from "@/components/AdminBilling";
import { RequireRole } from "@/components/RequireRole";

// The payment provider sends the firm back here after paying, with ?checkout=<id>.
export default async function AdminBillingPage({ searchParams }: PageProps<"/admin/billing">) {
  const { checkout } = await searchParams;
  return (
    <RequireRole role="admin">
      <AdminBilling returnedFromCheckout={typeof checkout === "string" && checkout.length > 0} />
    </RequireRole>
  );
}
