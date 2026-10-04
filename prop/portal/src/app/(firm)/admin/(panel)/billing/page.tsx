import { AdminBilling } from "@/components/AdminBilling";

// The payment provider sends the firm back here after paying, with ?checkout=<id>. ?tab=company shows the company's details.
export default async function AdminBillingPage({ searchParams }: PageProps<"/admin/billing">) {
  const { checkout, tab } = await searchParams;
  return <AdminBilling returnedFromCheckout={typeof checkout === "string" && checkout.length > 0} tab={tab === "company" ? "company" : "billing"} />;
}
