import { AdminTeam } from "@/components/AdminTeam";
import { RequireRole } from "@/components/RequireRole";

export default function AdminTeamPage() {
  return (
    <RequireRole role="admin">
      <AdminTeam />
    </RequireRole>
  );
}
