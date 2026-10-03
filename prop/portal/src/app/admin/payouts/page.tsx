import { AdminPayouts } from "@/components/AdminPayouts";
import { RequireRole } from "@/components/RequireRole";

export default function AdminPayoutsPage() {
  return <RequireRole role="admin"><AdminPayouts /></RequireRole>;
}
