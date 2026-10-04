import { OpsFirm } from "@/components/OpsFirm";
import { RequireStaff } from "@/components/RequireStaff";

// One firm for our staff, for example /ops/firms/acme.
export default async function OpsFirmPage({ params }: PageProps<"/ops/firms/[id]">) {
  const { id } = await params;
  return (
    <RequireStaff>
      <OpsFirm firmId={id} />
    </RequireStaff>
  );
}
