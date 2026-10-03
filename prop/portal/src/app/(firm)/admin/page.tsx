import { AdminAccounts } from "@/components/AdminAccounts";
import { RequireRole } from "@/components/RequireRole";

export default function AdminPage() {
  return <RequireRole role="admin"><AdminAccounts /></RequireRole>;
}
