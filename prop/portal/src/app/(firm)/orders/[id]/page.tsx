import { BuyerOrderView } from "@/components/BuyerOrder";

// The payment provider sends the buyer back here, for example /orders/<id>?token=<token>.
export default async function OrderPage({ params, searchParams }: PageProps<"/orders/[id]">) {
  const [{ id }, { token }] = await Promise.all([params, searchParams]);
  return <BuyerOrderView orderId={id} token={typeof token === "string" && token.length > 0 ? token : null} />;
}
