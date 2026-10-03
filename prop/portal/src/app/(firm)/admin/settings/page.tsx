import { AdminSettings } from "@/components/AdminSettings";
import { RequireRole } from "@/components/RequireRole";

export default function AdminSettingsPage() {
  return (
    <RequireRole role="admin">
      <AdminSettings />
    </RequireRole>
  );
}
