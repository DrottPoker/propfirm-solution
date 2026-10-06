"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { useMe, useShop } from "@/lib/queries";

import { AccountsOverview } from "./AccountsOverview";
import { RequireRole } from "./RequireRole";
import { Loading } from "./ui";

/**
 * The portal's front page: a trader's accounts, and for a visitor the firm's shop, where new traders start. A portal that
 * sells nothing sends visitors to the login instead.
 */
export function PortalHome() {
  const router = useRouter();
  const me = useMe("trader");
  const visitor = me.data === null;
  const shop = useShop(visitor);

  useEffect(() => {
    if (visitor && shop.data) {
      router.replace(shop.data.open ? "/buy" : "/login");
    } else if (visitor && shop.isError) {
      router.replace("/login");
    }
  }, [visitor, shop.data, shop.isError, router]);

  if (visitor || me.isPending) {
    return <Loading />;
  }

  return (
    <RequireRole role="trader">
      <AccountsOverview />
    </RequireRole>
  );
}
