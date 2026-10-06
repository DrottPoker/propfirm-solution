"use client";

import { ErrorScreen } from "@/components/ErrorScreen";
import { FirmName } from "@/components/FirmName";

// A page of the firm's portal that failed, in the firm's name and colors.
export default function Error({ retry }: { error: Error & { digest?: string }; retry: () => void }) {
  return (
    <ErrorScreen
      brand={<FirmName size="lg" />}
      title="Something went wrong"
      text="The page could not be shown. Try again, or start again from the start page."
      home="/"
      homeLabel="Go to the start page"
      onRetry={retry}
    />
  );
}
