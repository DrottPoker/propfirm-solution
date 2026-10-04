"use client";

import { useEffect, useId, useRef } from "react";

import { CloseIcon } from "./icons";
import { buttonClass, dangerButtonClass, secondaryButtonClass } from "./ui";

type DialogProps = {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: React.ReactNode;
  children: React.ReactNode;
  /** The buttons at the bottom, for example Cancel and the action. */
  footer?: React.ReactNode;
};

/**
 * Opens the native dialog element as a modal while <code>open</code> is true. The browser keeps focus inside it, makes
 * the page behind inert, and closes it on Escape. A click on the backdrop closes it too.
 */
function useModal(open: boolean) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) {
      return;
    }

    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  return ref;
}

function Header({ titleId, title, description, onClose }: { titleId: string; title: string; description?: React.ReactNode; onClose: () => void }) {
  return (
    <div className="flex items-start justify-between gap-3 px-6 pt-5">
      <div className="flex min-w-0 flex-col gap-1">
        <h2 id={titleId} className="text-lg font-semibold">
          {title}
        </h2>
        {description && <div className="text-sm text-muted">{description}</div>}
      </div>
      <button type="button" aria-label="Close" onClick={onClose} className="-mr-2 grid size-9 shrink-0 place-items-center rounded-md text-muted hover:bg-background hover:text-foreground">
        <CloseIcon />
      </button>
    </div>
  );
}

/** A dialog in the middle of the screen, for a decision such as rejecting a payout. */
export function Modal({ open, onClose, title, description, children, footer }: DialogProps) {
  const ref = useModal(open);
  const titleId = useId();
  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      onClose={onClose}
      onClick={(event) => event.target === ref.current && onClose()}
      className="m-auto w-[calc(100%-2rem)] max-w-lg rounded-xl border border-border bg-panel p-0 text-foreground shadow-2xl backdrop:bg-black/60"
    >
      {open && (
        <div className="flex flex-col gap-4 pb-5">
          <Header titleId={titleId} title={title} description={description} onClose={onClose} />
          <div className="flex flex-col gap-4 px-6">{children}</div>
          {footer && <div className="flex flex-wrap justify-end gap-2.5 px-6 pt-1">{footer}</div>}
        </div>
      )}
    </dialog>
  );
}

/** A panel from the right side of the screen, for a task with a form such as starting a challenge. Full width on a phone. */
export function Sheet({ open, onClose, title, description, children, footer }: DialogProps) {
  const ref = useModal(open);
  const titleId = useId();
  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      onClose={onClose}
      onClick={(event) => event.target === ref.current && onClose()}
      className="m-0 ml-auto h-dvh max-h-dvh w-full max-w-md border-l border-border bg-panel p-0 text-foreground shadow-2xl backdrop:bg-black/60"
    >
      {open && (
        <div className="flex h-full flex-col">
          <div className="border-b border-border pb-4">
            <Header titleId={titleId} title={title} description={description} onClose={onClose} />
          </div>
          <div className="flex flex-1 flex-col gap-5 overflow-y-auto px-6 py-5">{children}</div>
          {footer && <div className="flex flex-wrap justify-end gap-2.5 border-t border-border px-6 py-4">{footer}</div>}
        </div>
      )}
    </dialog>
  );
}

/**
 * Asks before an action, in the portal's own look instead of the browser's. The action runs on <code>onConfirm</code>,
 * which closes the dialog itself once it is done, so an error can be shown in it meanwhile.
 */
export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title,
  description,
  confirmLabel,
  pendingLabel,
  pending = false,
  danger = false,
  children,
}: {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title: string;
  description?: React.ReactNode;
  confirmLabel: string;
  pendingLabel?: string;
  pending?: boolean;
  danger?: boolean;
  children?: React.ReactNode;
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="button" disabled={pending} onClick={onConfirm} className={danger ? dangerButtonClass : buttonClass}>
            {pending ? (pendingLabel ?? confirmLabel) : confirmLabel}
          </button>
        </>
      }
    >
      {children}
    </Modal>
  );
}
