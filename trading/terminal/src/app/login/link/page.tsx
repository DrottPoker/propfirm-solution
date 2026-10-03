import type { Metadata } from "next";

import { LinkLogin } from "@/components/LinkLogin";

// The token must not reach other sites through the Referer header.
export const metadata: Metadata = { referrer: "no-referrer" };

// The firm's portal opens /login/link?token=...&account=... to log the trader straight in.
export default async function LinkLoginPage({ searchParams }: PageProps<"/login/link">) {
  const { token, account } = await searchParams;
  return (
    <LinkLogin
      token={typeof token === "string" && token.length > 0 ? token : null}
      accountId={typeof account === "string" && account.length > 0 ? account : null}
    />
  );
}
