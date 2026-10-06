"use client";

import { ErrorScreen } from "@/components/ErrorScreen";

// A page that failed outside a firm's portal, or before the firm was known. The firm's own pages have theirs.
export default function Error({ retry }: { error: Error & { digest?: string }; retry: () => void }) {
  return (
    <ErrorScreen
      brand={null}
      title="Something went wrong"
      text="The page could not be shown. Try again, or start again from the start page."
      home="/"
      homeLabel="Go to the start page"
      onRetry={retry}
    />
  );
}
