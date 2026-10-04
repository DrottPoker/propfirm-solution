import { AdminGoLive } from "@/components/AdminGoLive";

// The way from the sandbox to live, a step at a time, for example /admin/go-live?step=deposit. The payment provider sends
// the firm back here after paying the deposit or going live, with ?checkout=<id>.
export default async function AdminGoLivePage({ searchParams }: PageProps<"/admin/go-live">) {
  const { checkout, step } = await searchParams;
  return <AdminGoLive step={typeof step === "string" ? step : null} returnedFromCheckout={typeof checkout === "string" && checkout.length > 0} />;
}
