import { RequireRole } from "@/components/RequireRole";
import { TestBillingCheckout } from "@/components/TestBillingCheckout";

// A test page for paying the platform or saving a card, for example /admin/billing/checkout/<id>.
export default async function TestBillingCheckoutPage({ params }: PageProps<"/admin/billing/checkout/[id]">) {
  const { id } = await params;
  return (
    <RequireRole role="admin">
      <TestBillingCheckout checkoutId={id} />
    </RequireRole>
  );
}
