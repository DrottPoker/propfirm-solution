import { RequireRole } from "@/components/RequireRole";
import { TraderAccount } from "@/components/TraderAccount";

export default async function AccountPage({ params }: PageProps<"/accounts/[id]">) {
  const { id } = await params;
  return <RequireRole role="trader"><TraderAccount accountId={id} /></RequireRole>;
}
