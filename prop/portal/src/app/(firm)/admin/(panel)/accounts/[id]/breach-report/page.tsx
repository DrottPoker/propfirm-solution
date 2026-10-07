import { AdminBreachReport } from "@/components/BreachReport";

export default async function AdminBreachReportPage({ params }: PageProps<"/admin/accounts/[id]/breach-report">) {
  const { id } = await params;
  return <AdminBreachReport accountId={id} />;
}
