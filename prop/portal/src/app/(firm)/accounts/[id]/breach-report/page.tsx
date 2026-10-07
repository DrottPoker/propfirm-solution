import { TraderBreachReport } from "@/components/BreachReport";
import { RequireRole } from "@/components/RequireRole";

// Why a loss limit was broken on the account's latest stage, with the prices behind it (ADR 0053).
export default async function BreachReportPage({ params }: PageProps<"/accounts/[id]/breach-report">) {
  const { id } = await params;
  return <RequireRole role="trader"><TraderBreachReport accountId={id} /></RequireRole>;
}
