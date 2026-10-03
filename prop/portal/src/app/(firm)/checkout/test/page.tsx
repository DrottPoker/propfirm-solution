import { TestCheckout } from "@/components/TestCheckout";

// A test order's payment page, for example /checkout/test?order=<id>&token=<token>.
export default async function TestCheckoutPage({ searchParams }: PageProps<"/checkout/test">) {
  const { order, token } = await searchParams;
  return (
    <TestCheckout
      orderId={typeof order === "string" && order.length > 0 ? order : null}
      token={typeof token === "string" && token.length > 0 ? token : null}
    />
  );
}
