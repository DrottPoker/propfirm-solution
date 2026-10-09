import type { Metadata } from "next";

import { PriceFeedPage } from "@/components/PriceFeedPage";

export const metadata: Metadata = { title: "Price feed" };

export default function Page() {
  return <PriceFeedPage />;
}
