import { ChallengeEditor } from "@/components/ChallengeEditor";

// ?id= changes a challenge, ?from= starts a new one as a copy of another, and ?template= with ?size= one from a
// template. With none of them, a new one starts from the first template.
export default async function ChallengeEditorPage({ searchParams }: PageProps<"/admin/challenges/edit">) {
  const { id, from, template, size } = await searchParams;
  const balance = typeof size === "string" ? Number(size) : NaN;
  return (
    <ChallengeEditor
      challengeId={typeof id === "string" ? id : null}
      copyOf={typeof from === "string" ? from : null}
      templateId={typeof template === "string" ? template : null}
      size={Number.isFinite(balance) && balance > 0 ? balance : null}
    />
  );
}
