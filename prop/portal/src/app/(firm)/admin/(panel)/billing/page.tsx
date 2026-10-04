import { AdminBilling } from "@/components/AdminBilling";

// The payment provider sends the firm back here after paying, with ?checkout=<id>.
export default async function AdminBillingPage({ searchParams }: PageProps<"/admin/billing">) {
  const { checkout } = await searchParams;
  return <AdminBilling returnedFromCheckout={typeof checkout === "string" && checkout.length > 0} />;
}
