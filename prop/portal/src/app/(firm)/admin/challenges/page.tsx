import { AdminChallenges } from "@/components/AdminChallenges";
import { RequireRole } from "@/components/RequireRole";

export default function AdminChallengesPage() {
  return (
    <RequireRole role="admin">
      <AdminChallenges />
    </RequireRole>
  );
}
