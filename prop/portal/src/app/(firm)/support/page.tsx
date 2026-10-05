import { RequireRole } from "@/components/RequireRole";
import { TraderTickets } from "@/components/TraderSupport";

export default function SupportPage() {
  return <RequireRole role="trader"><TraderTickets /></RequireRole>;
}
