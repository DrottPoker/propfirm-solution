import { GetStarted } from "@/components/GetStarted";
import { stepOf } from "@/lib/getStarted";

// A new firm comes here right after signing up. It can come back from the overview. ?step= is the step it is on.
export default async function GetStartedPage({ searchParams }: PageProps<"/admin/get-started">) {
  const { step } = await searchParams;
  return <GetStarted step={stepOf(step)} />;
}
