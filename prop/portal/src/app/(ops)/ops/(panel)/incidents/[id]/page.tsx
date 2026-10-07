import { OpsIncident } from "@/components/OpsIncidents";

export default async function OpsIncidentPage({ params }: PageProps<"/ops/incidents/[id]">) {
  const { id } = await params;
  return <OpsIncident incidentId={id} />;
}
