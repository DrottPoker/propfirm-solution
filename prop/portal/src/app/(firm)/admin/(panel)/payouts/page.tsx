import { AdminPayouts } from "@/components/AdminPayouts";
import { payoutViews } from "@/lib/payouts";

// The overview links to the payouts to pay with ?view=to-pay.
export default async function AdminPayoutsPage({ searchParams }: PageProps<"/admin/payouts">) {
  const { view } = await searchParams;
  return <AdminPayouts initialView={payoutViews.find((v) => v.id === view)?.id ?? "to-approve"} />;
}
