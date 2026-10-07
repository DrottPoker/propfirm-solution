import { AdminIncident } from "@/components/AdminIncidents";

export default async function AdminIncidentPage({ params }: PageProps<"/admin/incidents/[id]">) {
  const { id } = await params;
  return <AdminIncident incidentId={id} />;
}
