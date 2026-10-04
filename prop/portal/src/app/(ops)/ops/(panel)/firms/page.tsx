import { OpsFirms } from "@/components/OpsFirms";
import type { OpsFirmGroup } from "@/lib/api/types";
import { opsGroups } from "@/lib/ops";

// The overview links to a group of firms, for example ?group=ToReview, and a search can be kept with ?search=.
export default async function OpsFirmsPage({ searchParams }: PageProps<"/ops/firms">) {
  const { group, search } = await searchParams;
  return (
    <OpsFirms initialGroup={opsGroups.find((g) => g === group) ?? ("All" satisfies OpsFirmGroup)} initialSearch={typeof search === "string" ? search : ""} />
  );
}
