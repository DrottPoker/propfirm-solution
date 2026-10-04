import { ChallengeEditor } from "@/components/ChallengeEditor";

// ?id= changes a challenge, ?from= starts a new one as a copy of another, and neither starts one from the template.
export default async function ChallengeEditorPage({ searchParams }: PageProps<"/admin/challenges/edit">) {
  const { id, from } = await searchParams;
  return <ChallengeEditor challengeId={typeof id === "string" ? id : null} copyOf={typeof from === "string" ? from : null} />;
}
