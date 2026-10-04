"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";

import { PlatformHome } from "./PlatformHome";
import { PlatformLogin } from "./PlatformLogin";
import { Message } from "./ui";

/**
 * The platform's own address shares the front page and the login page with the firms' portals, so they are chosen here
 * by path. Any other page of a portal leads to the front page.
 */
export function PlatformGate() {
  const pathname = usePathname();
  const router = useRouter();
  const known = pathname === "/" || pathname === "/login";

  useEffect(() => {
    if (!known) {
      router.replace("/");
    }
  }, [known, router]);

  return pathname === "/" ? <PlatformHome /> : pathname === "/login" ? <PlatformLogin /> : <Message text="Loading..." />;
}
