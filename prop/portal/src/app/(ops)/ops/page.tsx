import { OpsFirms } from "@/components/OpsFirms";
import { RequireStaff } from "@/components/RequireStaff";

export default function OpsPage() {
  return (
    <RequireStaff>
      <OpsFirms />
    </RequireStaff>
  );
}
