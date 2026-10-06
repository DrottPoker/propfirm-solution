"use client";

import { useState } from "react";

import { usePlatform } from "@/app/providers";
import { SignupError, useAvailability, useSignUp } from "@/lib/queries";
import { isValidFirmId, portalAddress, suggestFirmId } from "@/lib/signup";
import { useDebounced } from "@/lib/useDebounced";

import { PasswordInput } from "./PasswordInput";
import { PlatformCard } from "./PlatformCard";
import { RobotCheck } from "./RobotCheck";
import { buttonClass, ErrorText, fieldClass } from "./ui";

/**
 * A firm signs up on the platform: its name, the short name for its address and trading server, and the
 * administrator's email and password. Then either an email confirms the address, or the firm's admin panel opens.
 */
export function SignupForm() {
  const platform = usePlatform();
  const signUp = useSignUp();
  const [firmName, setFirmName] = useState("");
  const [chosenFirmId, setChosenFirmId] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [currency, setCurrency] = useState(platform.currencies[0] ?? "USD");
  const [acceptTerms, setAcceptTerms] = useState(false);
  const [robotAnswer, setRobotAnswer] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const robotCheckDone = platform.robotCheckSiteKey === null || robotAnswer !== null;

  // The short name follows the firm's name until it is edited.
  const firmId = chosenFirmId ?? suggestFirmId(firmName);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    signUp.mutate(
      { firmName: firmName.trim(), firmId, email: email.trim(), password, acceptTerms, currency, robotCheck: robotAnswer },
      {
        onSuccess: (result) => {
          if (result.adminUrl) {
            window.location.assign(result.adminUrl);
          }
        },
        // The answer to the robot check works once, so a new try needs a new one.
        onError: () => setAttempt((a) => a + 1),
      },
    );
  };

  if (signUp.data?.verificationRequired) {
    return (
      <PlatformCard title="Check your email">
        <p className="text-sm">
          We sent a link to <span className="font-medium">{email.trim()}</span>. Open it within 24 hours to confirm your email address and open the admin
          panel of {firmName.trim()}.
        </p>
        <p className="text-xs text-muted">No email? Check the spam folder, or sign up again to get a new link.</p>
      </PlatformCard>
    );
  }

  const fieldError = signUp.error instanceof SignupError ? signUp.error.field : null;

  return (
    <PlatformCard title="Start your prop firm">
      <form onSubmit={submit} className="flex flex-col gap-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Firm name</span>
          <input required value={firmName} onChange={(e) => setFirmName(e.target.value)} className={fieldClass} aria-invalid={fieldError === "firmName"} />
        </label>

        <div className="flex flex-col gap-1 text-sm">
          <label className="flex flex-col gap-1">
            <span className="text-muted">Short name</span>
            <input
              required
              value={firmId}
              onChange={(e) => setChosenFirmId(e.target.value.toLowerCase())}
              className={`${fieldClass} font-mono`}
              aria-describedby="firm-id-help"
              aria-invalid={fieldError === "firmId"}
            />
          </label>
          <FirmIdHelp firmId={firmId} template={platform.firmPortalUrl} onPick={setChosenFirmId} />
        </div>

        {platform.currencies.length > 1 && (
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Currency of the accounts</span>
            <select value={currency} onChange={(e) => setCurrency(e.target.value)} className={fieldClass} aria-invalid={fieldError === "currency"}>
              {platform.currencies.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <span className="text-xs text-muted">Your traders&apos; accounts, balances and limits are in it. It cannot be changed later. Prices in your shop can be in any currency.</span>
          </label>
        )}

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Your email</span>
          <input
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={fieldClass}
            aria-invalid={fieldError === "email"}
          />
        </label>

        <div className="flex flex-col gap-1 text-sm">
          <label htmlFor="signup-password" className="text-muted">
            Password
          </label>
          <PasswordInput
            id="signup-password"
            autoComplete="new-password"
            required
            minLength={platform.minimumPasswordLength}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={fieldError === "password"}
            aria-describedby={platform.minimumPasswordLength > 1 ? "signup-password-hint" : undefined}
          />
          {platform.minimumPasswordLength > 1 && (
            <span id="signup-password-hint" className="text-xs text-muted">
              At least {platform.minimumPasswordLength} characters.
            </span>
          )}
        </div>

        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" checked={acceptTerms} onChange={(e) => setAcceptTerms(e.target.checked)} className="mt-1" />
          <span>
            I accept the <DocumentLink href={platform.termsUrl}>terms of service</DocumentLink> and the{" "}
            <DocumentLink href={platform.dpaUrl}>data processing agreement</DocumentLink> on behalf of the firm.
          </span>
        </label>

        {platform.robotCheckSiteKey && <RobotCheck siteKey={platform.robotCheckSiteKey} attempt={attempt} onAnswer={setRobotAnswer} />}

        <ErrorText error={signUp.error} />

        <button type="submit" disabled={signUp.isPending || signUp.isSuccess || !acceptTerms || !robotCheckDone} className={buttonClass}>
          {signUp.isPending ? "Setting up your firm..." : signUp.isSuccess ? "Opening your admin panel..." : "Create my firm"}
        </button>
        <p className="text-xs text-muted">
          Your firm starts in a sandbox: try everything with your own test traders. Going live comes later, after a check of the company and its owners.
        </p>
      </form>
    </PlatformCard>
  );
}

function FirmIdHelp({ firmId, template, onPick }: { firmId: string; template: string; onPick: (firmId: string) => void }) {
  const checked = useDebounced(firmId, 300);
  const valid = isValidFirmId(checked);
  const availability = useAvailability(valid ? checked : "");

  let status: React.ReactNode = null;
  if (firmId.length > 0 && !isValidFirmId(firmId)) {
    status = <span className="text-loss">Use 2 to 40 lowercase letters, digits and dashes, not first or last.</span>;
  } else if (valid && checked === firmId && availability.data) {
    const { available, reason, suggestions } = availability.data;
    status = available ? (
      <span className="text-profit">Available</span>
    ) : (
      <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <span className="text-loss">{reason}</span>
        {suggestions.length > 0 && (
          <>
            <span>Free:</span>
            {suggestions.map((s) => (
              <button key={s} type="button" onClick={() => onPick(s)} className="rounded border border-border px-1.5 py-0.5 font-mono text-foreground hover:border-accent">
                {s}
              </button>
            ))}
          </>
        )}
      </span>
    );
  }

  return (
    <span id="firm-id-help" className="flex flex-col gap-1 text-xs text-muted">
      <span>
        Your portal&apos;s address: <span className="font-mono">{portalAddress(template, firmId)}</span>. It cannot be changed, but you can add your own
        domain later.
      </span>
      {status}
    </span>
  );
}

function DocumentLink({ href, children }: { href: string | null; children: React.ReactNode }) {
  if (!href) {
    return <>{children}</>;
  }

  return (
    <a href={href} target="_blank" rel="noreferrer" className="text-accent hover:underline">
      {children}
    </a>
  );
}
