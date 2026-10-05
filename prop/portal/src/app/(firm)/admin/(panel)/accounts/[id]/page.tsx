import { AdminAccount, type Emailed } from "@/components/AdminAccount";

const emailOutcomes: Emailed[] = ["Invitation", "Notice", "failed", "withheld"];

// After starting a challenge, ?emailed= says how emailing the trader went.
export default async function AdminAccountPage({ params, searchParams }: PageProps<"/admin/accounts/[id]">) {
  const { id } = await params;
  const { emailed } = await searchParams;
  return <AdminAccount accountId={id} emailed={emailOutcomes.find((e) => e === emailed) ?? null} />;
}
