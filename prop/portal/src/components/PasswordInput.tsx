"use client";

import { useId, useState } from "react";

import { EyeIcon, EyeSlashIcon } from "./icons";
import { fieldClass } from "./ui";

/** A password field with a button that shows what was typed, so it can be checked before it is sent. */
export function PasswordInput({ className = "", id, ...props }: Omit<React.InputHTMLAttributes<HTMLInputElement>, "type">) {
  const [shown, setShown] = useState(false);
  const ownId = useId();
  const inputId = id ?? ownId;
  return (
    <span className="relative flex">
      <input {...props} id={inputId} type={shown ? "text" : "password"} className={`${fieldClass} w-full pr-11 ${className}`} />
      <button
        type="button"
        aria-label={shown ? "Hide password" : "Show password"}
        aria-pressed={shown}
        aria-controls={inputId}
        onClick={() => setShown(!shown)}
        className="absolute inset-y-0 right-0 grid w-10 place-items-center rounded-r-lg text-muted transition-colors hover:text-foreground"
      >
        {shown ? <EyeSlashIcon className="size-4" /> : <EyeIcon className="size-4" />}
      </button>
    </span>
  );
}
