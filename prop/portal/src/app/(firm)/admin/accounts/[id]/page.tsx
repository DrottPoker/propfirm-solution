import { AdminAccount } from "@/components/AdminAccount";
import { RequireRole } from "@/components/RequireRole";

export default async function AdminAccountPage({ params }: PageProps<"/admin/accounts/[id]">) {
  const { id } = await params;
  return <RequireRole role="admin"><AdminAccount accountId={id} /></RequireRole>;
}
