import { RequireRole } from "@/components/RequireRole";
import { TraderPayouts } from "@/components/TraderPayouts";

export default function PayoutsPage() {
  return <RequireRole role="trader"><TraderPayouts /></RequireRole>;
}
