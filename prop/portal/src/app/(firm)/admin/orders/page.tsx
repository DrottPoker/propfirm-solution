import { AdminOrders } from "@/components/AdminOrders";
import { RequireRole } from "@/components/RequireRole";

export default function AdminOrdersPage() {
  return <RequireRole role="admin"><AdminOrders /></RequireRole>;
}
