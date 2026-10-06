"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { accountStatus } from "@/lib/admin";
import { ApiError } from "@/lib/api/client";
import { formatMoney } from "@/lib/format";
import { useAdmins, useApproved, useBilling, useChallenges, useEmailTrader, useFirmSettings, usePrices, useStartAccount, useTraderAccounts } from "@/lib/queries";
import { useDebounced } from "@/lib/useDebounced";

import { TeamOnlyNote } from "./BeforeApproval";
import { Sheet } from "./Dialog";
import { InfoIcon, PlusIcon } from "./icons";
import { buttonClass, ErrorText, fieldClass, secondaryButtonClass } from "./ui";

/** The button that starts a challenge for a trader, with the panel it opens. */
export function StartChallengeButton({ secondary = false }: { secondary?: boolean }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)} className={`${secondary ? secondaryButtonClass : buttonClass} flex items-center gap-2`}>
        <PlusIcon />
        Start a challenge
      </button>
      {open && <StartChallengeSheet onClose={() => setOpen(false)} />}
    </>
  );
}

const looksLikeEmail = (text: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(text.trim());

/**
 * Starts a challenge for a trader who paid outside the portal, or won one. The trader is found by email, so the panel
 * says when the email is a trader's already. It can email the trader in the firm's name at once, though only an
 * administrator's address until we have approved the firm.
 */
function StartChallengeSheet({ onClose }: { onClose: () => void }) {
  const router = useRouter();
  const formId = useId();
  const settings = useFirmSettings();
  const challenges = useChallenges(settings.data !== undefined && settings.data.status !== "Provisioning");
  const prices = usePrices();
  const billing = useBilling();
  const start = useStartAccount();
  const emailTrader = useEmailTrader();
  const [email, setEmail] = useState("");
  const [chosen, setChosen] = useState("");
  const [reference, setReference] = useState("");
  const [notify, setNotify] = useState(true);
  const lookup = useDebounced(looksLikeEmail(email) ? email.trim() : "", 400);
  const traderAccounts = useTraderAccounts(lookup);
  const approved = useApproved();
  const admins = useAdmins(approved === false);
  const mayEmail = approved !== false || (admins.data?.admins ?? []).some((a) => a.email.toLowerCase() === email.trim().toLowerCase());

  const list = [...(challenges.data ?? [])].sort((a, b) => a.name.localeCompare(b.name));
  const challengeId = chosen || list[0]?.id || "";
  const existing = (traderAccounts.data?.accounts ?? []).filter((a) => a.email.toLowerCase() === lookup.toLowerCase());
  const slots = billing.data?.slots;
  const busy = start.isPending || emailTrader.isPending;

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    start.mutate(
      { email: email.trim(), challengeId, reference: reference.trim() || null },
      {
        onSuccess: async (account) => {
          let emailed = "";
          if (notify && mayEmail) {
            emailed = await emailTrader.mutateAsync(account.id).then(
              (sent) => sent.kind,
              (error) => (error instanceof ApiError && error.status === 409 ? "withheld" : "failed"),
            );
          }

          router.push(`/admin/accounts/${account.id}${emailed ? `?emailed=${emailed}` : ""}`);
        },
      },
    );
  };

  return (
    <Sheet
      open
      onClose={onClose}
      title="Start a challenge"
      description="For a trader who paid you outside the portal, or won a competition. It starts at once."
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="submit" form={formId} disabled={busy || !challengeId} className={buttonClass}>
            {start.isPending ? "Starting..." : emailTrader.isPending ? "Emailing the trader..." : "Start challenge"}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-5">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Trader&apos;s email</span>
          <input type="email" required autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
          {existing.length > 0 && (
            <span className="text-xs text-muted">
              Has {existing.length === 1 ? "an account" : `${existing.length} accounts`} here already:{" "}
              {existing
                .slice(0, 3)
                .map((a) => `#${a.number} ${list.find((c) => c.id === a.challengeId)?.name ?? a.challengeId} (${accountStatus(a).label.toLowerCase()})`)
                .join(", ")}
              .
            </span>
          )}
        </label>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1.5 text-sm font-medium">Challenge</legend>
          {settings.data?.status === "Provisioning" && <p className="text-sm text-muted">Your trading server is being set up. This takes a few seconds.</p>}
          {list.map((challenge) => {
            const price = prices.data?.find((p) => p.challengeId === challenge.id);
            const checked = challenge.id === challengeId;
            return (
              <label
                key={challenge.id}
                className={`flex cursor-pointer items-start gap-3 rounded-lg border px-3.5 py-3 text-sm ${checked ? "border-accent bg-accent/5" : "border-border hover:border-muted"}`}
              >
                <input type="radio" name="challenge" value={challenge.id} checked={checked} onChange={() => setChosen(challenge.id)} className="mt-1 accent-accent" />
                <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span className="font-medium">{challenge.name}</span>
                  <span className="text-xs text-muted">
                    {formatMoney(challenge.initialBalance)} {challenge.currency} · {challenge.evaluation.length === 1 ? "1 phase" : `${challenge.evaluation.length} phases`}, then
                    funded at {challenge.funded.profitSplitPercent}%
                  </span>
                </span>
                {price && (
                  <span className="whitespace-nowrap text-xs text-muted">
                    {formatMoney(price.amount)} {price.currency}
                  </span>
                )}
              </label>
            );
          })}
        </fieldset>

        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Your reference <span className="font-normal text-muted">(optional)</span>
          </span>
          <input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="For example an order number from your own shop" className={fieldClass} />
        </label>

        <label className={`flex items-start gap-3 rounded-lg border border-border p-3.5 text-sm ${mayEmail ? "cursor-pointer" : "opacity-80"}`}>
          <input
            type="checkbox"
            checked={notify && mayEmail}
            disabled={!mayEmail}
            onChange={(e) => setNotify(e.target.checked)}
            className="mt-1 accent-accent"
          />
          <span className="flex flex-col gap-1.5">
            <span className="font-medium">Email the trader</span>
            <span className="text-xs text-muted">
              In your firm&apos;s name: an invitation to choose a password for your portal, or for a trader who has one, that the challenge has started.
            </span>
            <TeamOnlyNote />
          </span>
        </label>

        {slots && slots.slots !== null && (
          <p className={`flex items-center gap-2 text-xs ${slots.free === 0 ? "text-warning" : "text-muted"}`}>
            <InfoIcon className="size-3.5 shrink-0" />
            {slots.free === 0
              ? "Every slot is taken, so no challenge can start. Add slots under Plan and billing."
              : `Takes one of your ${slots.free} free ${slots.free === 1 ? "slot" : "slots"} until the account ends.`}
          </p>
        )}

        <ErrorText error={start.error ?? challenges.error} />
      </form>
    </Sheet>
  );
}
