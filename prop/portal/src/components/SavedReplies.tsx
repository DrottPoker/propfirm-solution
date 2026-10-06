"use client";

import { useEffect, useId, useRef, useState } from "react";

import type { SavedReply } from "@/lib/api/types";
import { FieldError, useRemoveReply, useSavedReplies, useSaveReply } from "@/lib/queries";
import { charactersLeft, fillReply, findReplies, replyPreview, savedReplyLimits, savedReplyProblem, savedReplySearchFrom, type ReplyValues } from "@/lib/support";
import { useSavedNote } from "@/lib/useSavedNote";

import { ConfirmDialog, Sheet } from "./Dialog";
import { ChevronRightIcon, PencilIcon, PlusIcon, SearchIcon, SupportIcon, TrashIcon } from "./icons";
import { useMessageBox } from "./SupportThread";
import { buttonClass, EmptyState, ErrorText, fieldClass, secondaryButtonClass, Skeleton } from "./ui";

/** How the panel of saved replies opens: on the list, or on a new reply. */
export type SavedRepliesStart = "list" | "new";

/**
 * The firm's saved replies beside the answer box, as a tool of a MessageForm: a button that lists them by title,
 * searchable when there are many, and puts the chosen one in the answer with {trader} and {firm} filled in, to edit
 * before it is sent. The list also opens the panel where they are added, changed and deleted, which
 * <code>onManage</code> shows outside the answer's form.
 */
export function SavedRepliesMenu({ values, onManage }: { values: ReplyValues; onManage: (start: SavedRepliesStart) => void }) {
  const box = useMessageBox();
  // Asked for at once, so the list is there when the button is clicked.
  const replies = useSavedReplies();
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const ref = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const popover = useRef<HTMLDivElement>(null);
  const popoverId = useId();

  const list = replies.data ?? [];
  const searchable = list.length >= savedReplySearchFrom;
  const found = findReplies(list, searchable ? search : "");

  const close = (refocus: boolean) => {
    setOpen(false);
    setSearch("");
    if (refocus) {
      trigger.current?.focus();
    }
  };

  // Closes on a click outside it, like the other menus.
  useEffect(() => {
    if (!open) {
      return;
    }

    const onPointerDown = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) {
        setOpen(false);
        setSearch("");
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  // Focus goes to the search, or the first reply, so the list is used from the keyboard at once.
  useEffect(() => {
    if (open) {
      popover.current?.querySelector<HTMLElement>("input, [data-reply], button")?.focus();
    }
  }, [open]);

  const choose = (reply: SavedReply) => {
    box.insert(fillReply(reply.body, values));
    setOpen(false);
    setSearch("");
  };

  const manage = (start: SavedRepliesStart) => {
    setOpen(false);
    setSearch("");
    onManage(start);
  };

  // Up and down move between the replies, from the button and the search too. Escape closes the list.
  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      close(true);
      return;
    }

    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
      return;
    }

    const items = Array.from(popover.current?.querySelectorAll<HTMLElement>("[data-reply]") ?? []);
    if (items.length === 0) {
      return;
    }

    event.preventDefault();
    const at = items.indexOf(document.activeElement as HTMLElement);
    const next = event.key === "ArrowDown" ? (at + 1) % items.length : at <= 0 ? items.length - 1 : at - 1;
    items[next].focus();
  };

  return (
    <div ref={ref} onKeyDown={open ? onKeyDown : undefined} className="relative">
      <button
        ref={trigger}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? popoverId : undefined}
        disabled={box.disabled}
        onClick={() => (open ? close(false) : setOpen(true))}
        className="flex items-center gap-1 rounded-md px-1.5 py-0.5 text-accent transition-colors hover:bg-accent/10 disabled:opacity-50"
      >
        Saved replies
        <ChevronRightIcon className={`size-3 transition-transform duration-150 ${open ? "-rotate-90" : "rotate-90"}`} />
      </button>
      {open && (
        <div
          ref={popover}
          id={popoverId}
          role="dialog"
          aria-label="Choose a saved reply"
          className="absolute right-0 top-full z-20 mt-2 flex w-80 max-w-[calc(100vw-2rem)] origin-top-right animate-pop flex-col rounded-xl border border-border bg-panel p-1.5 text-sm shadow-float"
        >
          {searchable && (
            <label className="m-1 flex items-center gap-2 rounded-lg border border-border bg-background/60 px-2.5 text-muted focus-within:border-accent">
              <SearchIcon className="size-3.5" />
              <span className="sr-only">Search saved replies</span>
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                onKeyDown={(event) => {
                  // Enter takes the first reply found, and never sends the answer the list is in.
                  if (event.key === "Enter") {
                    event.preventDefault();
                    if (found[0]) {
                      choose(found[0]);
                    }
                  }
                }}
                placeholder="Search by title or text"
                className="min-w-0 flex-1 bg-transparent py-1.5 text-foreground outline-none"
              />
            </label>
          )}
          {replies.isPending ? (
            <p className="px-3 py-2 text-muted">Loading...</p>
          ) : replies.isError ? (
            <div className="px-3 py-2">
              <ErrorText error={replies.error} />
            </div>
          ) : list.length === 0 ? (
            <div className="flex flex-col gap-2 px-3 py-2.5">
              <p className="font-medium">No saved replies yet</p>
              <p className="text-muted">Save answers you give often. Then put one in an answer here, with the trader&apos;s name filled in.</p>
              <button type="button" onClick={() => manage("new")} className={`${secondaryButtonClass} flex items-center justify-center gap-1.5 py-1.5`}>
                <PlusIcon />
                Add a saved reply
              </button>
            </div>
          ) : found.length === 0 ? (
            <p className="px-3 py-2 text-muted">No saved reply has &ldquo;{search.trim()}&rdquo; in it.</p>
          ) : (
            <ul aria-label="Saved replies" className="flex max-h-72 flex-col overflow-y-auto">
              {found.map((reply) => (
                <li key={reply.id}>
                  <button
                    type="button"
                    data-reply
                    onClick={() => choose(reply)}
                    aria-labelledby={`${popoverId}-${reply.id}-title`}
                    aria-describedby={`${popoverId}-${reply.id}-text`}
                    className="flex w-full flex-col gap-0.5 rounded-lg px-3 py-2 text-left outline-none transition-colors hover:bg-raised focus-visible:bg-raised"
                  >
                    <span id={`${popoverId}-${reply.id}-title`} className="truncate font-medium">
                      {reply.title}
                    </span>
                    <span id={`${popoverId}-${reply.id}-text`} className="truncate text-xs text-muted">
                      {replyPreview(reply.body)}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {list.length > 0 && (
            <div className="mt-1 border-t border-border pt-1">
              <button
                type="button"
                onClick={() => manage("list")}
                className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-muted transition-colors hover:bg-raised hover:text-foreground"
              >
                <PencilIcon />
                Manage saved replies
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/** A reply being written: a new one without an id, or a change to the one with it. */
type Draft = { id: string | null; title: string; body: string };

const newDraft: Draft = { id: null, title: "", body: "" };

/**
 * The firm's saved replies, which all its administrators share: added, changed and deleted here. Deleting one asks
 * first. A reply can have {trader} and {firm}, which are filled in when it is put in an answer.
 */
export function SavedRepliesSheet({ start, onClose }: { start: SavedRepliesStart; onClose: () => void }) {
  const replies = useSavedReplies();
  const save = useSaveReply();
  const remove = useRemoveReply();
  const formId = useId();
  const [draft, setDraft] = useState<Draft | null>(start === "new" ? newDraft : null);
  const [removing, setRemoving] = useState<SavedReply | null>(null);
  useSavedNote(save);

  const list = replies.data ?? [];
  const full = list.length >= savedReplyLimits.count;
  const edit = (next: Draft) => {
    save.reset();
    setDraft(next);
  };

  return (
    <>
      <Sheet
        open
        onClose={onClose}
        title="Saved replies"
        description="Answers you give often, shared by your whole team. Put one in an answer with Saved replies above the answer box, and change what you need before you send it."
        footer={
          draft ? (
            <>
              <button type="button" onClick={() => setDraft(null)} className={secondaryButtonClass}>
                Cancel
              </button>
              <button type="submit" form={formId} disabled={save.isPending} className={buttonClass}>
                {save.isPending ? "Saving..." : "Save reply"}
              </button>
            </>
          ) : (
            <button type="button" onClick={onClose} className={secondaryButtonClass}>
              Done
            </button>
          )
        }
      >
        {draft ? (
          <ReplyForm
            formId={formId}
            draft={draft}
            onChange={setDraft}
            error={save.error}
            onSubmit={(reply) => save.mutate(reply, { onSuccess: () => setDraft(null) })}
          />
        ) : replies.isPending ? (
          <div className="flex flex-col gap-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-14 rounded-lg" />
            ))}
          </div>
        ) : list.length === 0 ? (
          <>
            <ErrorText error={replies.error} />
            <EmptyState
              icon={<SupportIcon className="size-6" />}
              title="No saved replies yet"
              text="Save answers you give often, such as when payouts are paid. Then put one in an answer with a click, with the trader's name filled in, and change what you need before you send it."
              actions={
                <button type="button" onClick={() => edit(newDraft)} className={`${buttonClass} flex items-center gap-1.5`}>
                  <PlusIcon />
                  Add a saved reply
                </button>
              }
            />
          </>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-sm text-muted">
                {list.length} of {savedReplyLimits.count} saved replies
              </p>
              <button type="button" disabled={full} onClick={() => edit(newDraft)} className={`${secondaryButtonClass} flex items-center gap-1.5`}>
                <PlusIcon />
                Add a saved reply
              </button>
            </div>
            {full && <p className="text-xs text-muted">That is as many as a firm can have. Delete one you no longer use to add another.</p>}
            <ErrorText error={replies.error} />
            <ul aria-label="Your saved replies" className="flex flex-col">
              {list.map((reply) => (
                <li key={reply.id} className="flex items-start gap-2 border-t border-border py-3 first:border-t-0">
                  <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                    <span className="break-words font-medium">{reply.title}</span>
                    <span className="line-clamp-2 whitespace-pre-line break-words text-sm text-muted">{reply.body}</span>
                  </div>
                  <button
                    type="button"
                    aria-label={`Edit ${reply.title}`}
                    onClick={() => edit({ id: reply.id, title: reply.title, body: reply.body })}
                    className="grid size-8 shrink-0 place-items-center rounded-md text-muted transition-colors hover:bg-raised hover:text-foreground"
                  >
                    <PencilIcon />
                  </button>
                  <button
                    type="button"
                    aria-label={`Delete ${reply.title}`}
                    onClick={() => {
                      remove.reset();
                      setRemoving(reply);
                    }}
                    className="grid size-8 shrink-0 place-items-center rounded-md text-muted transition-colors hover:bg-raised hover:text-loss"
                  >
                    <TrashIcon />
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
      </Sheet>

      <ConfirmDialog
        open={removing !== null}
        onClose={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id, { onSuccess: () => setRemoving(null) })}
        title={`Delete “${removing?.title ?? ""}”?`}
        description="Nobody on your team can put it in an answer any more. Answers already sent with it stay as they are."
        confirmLabel="Delete"
        pendingLabel="Deleting..."
        pending={remove.isPending}
        danger
      >
        <ErrorText error={remove.error} />
      </ConfirmDialog>
    </>
  );
}

/** The title and text of a saved reply, checked before it is sent. The placeholders are explained beside the text. */
function ReplyForm({
  formId,
  draft,
  onChange,
  onSubmit,
  error,
}: {
  formId: string;
  draft: Draft;
  onChange: (draft: Draft) => void;
  onSubmit: (reply: Draft) => void;
  error: Error | null;
}) {
  const titleId = useId();
  const bodyId = useId();
  const hintId = useId();
  const [problem, setProblem] = useState<{ field: "title" | "body"; text: string } | null>(null);
  const field = problem?.field ?? (error instanceof FieldError ? error.field : null);
  const left = charactersLeft(draft.body, savedReplyLimits.body);

  return (
    <form
      id={formId}
      onSubmit={(event) => {
        event.preventDefault();
        const found = savedReplyProblem(draft.title, draft.body);
        setProblem(found);
        if (!found) {
          onSubmit({ id: draft.id, title: draft.title.trim(), body: draft.body.trim() });
        }
      }}
      className="flex flex-col gap-5"
    >
      <h3 className="font-semibold">{draft.id ? "Edit the saved reply" : "New saved reply"}</h3>
      <div className="flex flex-col gap-1.5 text-sm">
        <label htmlFor={titleId} className="font-medium">
          Title
        </label>
        <input
          id={titleId}
          value={draft.title}
          onChange={(e) => onChange({ ...draft, title: e.target.value })}
          maxLength={savedReplyLimits.title}
          placeholder="For example: When payouts are paid"
          aria-invalid={field === "title" || undefined}
          className={fieldClass}
        />
        <span className="text-xs text-muted">Only your team sees it, to find the reply by.</span>
      </div>
      <div className="flex flex-col gap-1.5 text-sm">
        <label htmlFor={bodyId} className="font-medium">
          Reply
        </label>
        <textarea
          id={bodyId}
          value={draft.body}
          onChange={(e) => onChange({ ...draft, body: e.target.value })}
          rows={10}
          placeholder={"Hi {trader},\n\n"}
          aria-invalid={field === "body" || (left !== null && left < 0) || undefined}
          aria-describedby={hintId}
          className={`${fieldClass} resize-y`}
        />
        <span id={hintId} className="text-xs text-muted">
          You can use <code className="text-foreground">{"{trader}"}</code> and <code className="text-foreground">{"{firm}"}</code>. They become the
          trader&apos;s name, or email when there is no name, and your firm&apos;s name when you put the reply in an answer.
        </span>
        {left !== null && (
          <span className={`self-end text-xs ${left < 0 ? "text-loss" : "text-muted"}`}>{left < 0 ? `${-left} characters too many` : `${left} characters left`}</span>
        )}
      </div>
      {problem && (
        <p role="alert" className="text-sm text-loss">
          {problem.text}
        </p>
      )}
      <ErrorText error={error} />
    </form>
  );
}
