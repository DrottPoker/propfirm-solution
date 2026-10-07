"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import type { FirmSettings } from "@/lib/api/types";
import { imageEdges, logoHardToSee } from "@/lib/logoContrast";
import { useFirmSettings, useRemoveLogo, useSaveColors, useUploadLogo } from "@/lib/queries";
import {
  contrastRatio,
  defaultColors,
  isHexColor,
  presetOf,
  readableContrast,
  readableTextOn,
  themeColors,
  themePresets,
  themeStyle,
  withPreset,
  type ThemeColor,
  type ThemeColors,
} from "@/lib/theme";

import { DropZone } from "./DropZone";
import { FirmLogo } from "./FirmLogo";
import { AlertIcon, CheckIcon } from "./icons";
import { AdminPage, buttonClass, ErrorText, Loading, Message, PageHeader, Panel, secondaryButtonClass, SegmentedControl } from "./ui";

const colorText: Record<ThemeColor, { label: string; hint: string }> = {
  accent: { label: "Brand color", hint: "Buttons, links and highlights" },
  "accent-foreground": { label: "Text on buttons", hint: "On the brand color" },
  background: { label: "Background", hint: "Behind everything" },
  panel: { label: "Panels", hint: "Cards and the header" },
  border: { label: "Borders", hint: "Lines between things" },
  foreground: { label: "Text", hint: "Most text" },
  muted: { label: "Muted text", hint: "Labels and help" },
  profit: { label: "Profit", hint: "Gains and progress" },
  loss: { label: "Loss", hint: "Losses and broken limits" },
  warning: { label: "Warning", hint: "Limits that are close" },
};

/** Brand colors that white button text is easy to read on. */
const brandSwatches = [
  { color: "#2563eb", name: "Blue" },
  { color: "#7c3aed", name: "Violet" },
  { color: "#0f766e", name: "Teal" },
  { color: "#15803d", name: "Green" },
  { color: "#c2410c", name: "Orange" },
  { color: "#be123c", name: "Rose" },
];

/** The brand colors to choose from, each with its name, and the chosen one's name and code under them. */
export function BrandSwatches({
  value,
  onChoose,
  disabled = false,
  children,
}: {
  value: string;
  onChoose: (color: string) => void;
  disabled?: boolean;
  /** More to choose with after the colors, such as a color picker. */
  children?: React.ReactNode;
}) {
  const chosen = brandSwatches.find((s) => s.color === value.toLowerCase());
  return (
    <div className="flex flex-col gap-2">
      <div role="group" aria-label="Brand colors" className="flex flex-wrap items-center gap-2.5">
        {brandSwatches.map((swatch) => (
          <button
            key={swatch.color}
            type="button"
            title={swatch.name}
            aria-label={`Brand color ${swatch.name}`}
            aria-pressed={chosen === swatch}
            disabled={disabled}
            onClick={() => onChoose(swatch.color)}
            className={`size-9 rounded-full border-2 p-0.5 transition-transform duration-200 ease-out-soft hover:scale-110 ${chosen === swatch ? "border-foreground" : "border-transparent"}`}
          >
            <span className="block size-full rounded-full" style={{ background: swatch.color }} />
          </button>
        ))}
        {children}
      </div>
      <span className="text-xs text-muted">
        {chosen ? chosen.name : "Your own color"} <span className="font-mono">({value})</span>
      </span>
    </div>
  );
}

/** Automatic is the easier to read of white and dark, worked out for each brand color. */
const buttonTexts: { label: string; value: string | null }[] = [
  { label: "Automatic", value: null },
  { label: "White", value: "#ffffff" },
  { label: "Dark", value: "#0b0e14" },
];

/**
 * How the firm's portal looks to its traders: the logo, a theme to start from, the brand color and the text on it, and
 * every color on its own, with a preview of the portal that shows the colors before they are saved.
 */
export function PortalDesign() {
  const settings = useFirmSettings();
  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Loading />;
  }

  return <Design key={JSON.stringify(settings.data.colors)} settings={settings.data} />;
}

function Design({ settings }: { settings: FirmSettings }) {
  const router = useRouter();
  const save = useSaveColors();
  const initial = settings.colors as ThemeColors;
  const [colors, setColors] = useState<ThemeColors>(initial);
  const effective = (name: ThemeColor): string =>
    colors[name] ?? (name === "accent-foreground" && colors.accent ? readableTextOn(colors.accent) : defaultColors[name]);
  const dirty = JSON.stringify(sorted(colors)) !== JSON.stringify(sorted(initial));
  const preset = presetOf(colors);
  const ratio = contrastRatio(effective("accent"), effective("accent-foreground"));
  const logoEdges = useLogoEdges(settings.logoUrl);
  // The logo stands on the panels, such as the header, in the colors being chosen.
  const logoHard = logoEdges !== null && logoHardToSee(logoEdges, effective("panel"));
  const [view, setView] = useState<"settings" | "preview">("settings");

  const setColor = (name: ThemeColor, value: string | null) =>
    setColors((current) => {
      const next = { ...current };
      if (value === null) {
        delete next[name];
      } else {
        next[name] = value;
      }

      return next;
    });

  // A new brand color gets the button text that is easy to read on it, until the firm chooses one.
  const setBrandColor = (value: string) =>
    setColors((current) => {
      const next = { ...current, accent: value };
      delete next["accent-foreground"];
      return next;
    });

  const submit = () =>
    save.mutate(colors as Record<string, string>, {
      // The portal's look is set on the server before a page renders, so the page is rendered again.
      onSuccess: () => router.refresh(),
    });

  return (
    <AdminPage>
      <PageHeader
        title="Portal design"
        description="How your portal looks to your traders, on every page. A new logo is saved at once. The preview shows your color changes before you save them."
      />
      {/* On a small screen the settings and the preview take turns, so the preview is one tap away. */}
      <div className="flex xl:hidden">
        <SegmentedControl
          label="Show"
          value={view}
          options={[
            { value: "settings", label: "Settings" },
            { value: "preview", label: "Preview" },
          ]}
          onChange={setView}
        />
      </div>
      <div className="grid grid-cols-1 items-start gap-6 xl:grid-cols-[22rem_minmax(0,1fr)]">
        <div className={`flex-col gap-5 ${view === "preview" ? "hidden xl:flex" : "flex"}`}>
          <Logo settings={settings} hardToSee={logoHard} />

          <Panel title="Theme">
            <div role="group" aria-label="Theme" className="grid grid-cols-3 gap-2">
              {themePresets.map((theme) => {
                const look = { ...defaultColors, ...theme.colors };
                return (
                  <button
                    key={theme.id}
                    type="button"
                    aria-pressed={preset === theme.id}
                    onClick={() => setColors(withPreset(colors, theme))}
                    className={`flex flex-col gap-2 rounded-lg border p-2 text-left text-sm ${preset === theme.id ? "border-accent" : "border-border hover:border-muted"}`}
                  >
                    <span aria-hidden="true" className="flex h-11 flex-col gap-1 rounded p-1.5" style={{ background: look.background }}>
                      <span className="h-2.5 w-3/4 rounded-sm" style={{ background: look.panel }} />
                      <span className="h-2 w-2/5 rounded-sm" style={{ background: effective("accent") }} />
                    </span>
                    {theme.name}
                  </button>
                );
              })}
            </div>
            {preset === null && <p className="text-xs text-muted">Your own colors. Choose a theme to start again from it.</p>}
            {logoHard && (
              <p role="status" className="flex gap-2 text-xs text-warning">
                <AlertIcon className="size-4 shrink-0" />
                Your logo is hard to see on this theme. See Logo above.
              </p>
            )}
          </Panel>

          <Panel title="Brand color">
            <BrandSwatches value={effective("accent")} onChoose={setBrandColor}>
              <label className="flex h-9 cursor-pointer items-center gap-2 rounded-full border border-dashed border-border px-3 text-sm text-muted hover:text-foreground">
                <input type="color" aria-label="Other brand color" value={effective("accent")} onChange={(e) => setBrandColor(e.target.value)} className="size-5 cursor-pointer rounded border-0 bg-transparent p-0" />
                Other
              </label>
            </BrandSwatches>
            <div className="flex flex-col gap-2">
              <span className="text-sm font-medium">Text on buttons</span>
              <div role="group" aria-label="Text on buttons" className="flex gap-0.5 self-start rounded-md border border-border p-0.5 text-sm">
                {buttonTexts.map((text) => {
                  const chosen = (colors["accent-foreground"]?.toLowerCase() ?? null) === text.value;
                  return (
                    <button
                      key={text.label}
                      type="button"
                      aria-pressed={chosen}
                      onClick={() => setColor("accent-foreground", text.value)}
                      className={`rounded px-3.5 py-1.5 ${chosen ? "bg-background font-medium" : "text-muted hover:text-foreground"}`}
                    >
                      {text.label}
                    </button>
                  );
                })}
              </div>
            </div>
            {ratio < readableContrast ? (
              <p role="status" className="flex gap-2 rounded-lg border border-warning/40 bg-warning/10 px-3 py-2.5 text-xs">
                <AlertIcon className="size-4 shrink-0 text-warning" />
                <span>
                  Button text is hard to read on this color ({ratio.toFixed(1)}:1, and text needs {readableContrast}:1). Choose the other text color, or a darker brand color.
                </span>
              </p>
            ) : (
              <p role="status" className="flex items-center gap-2 text-xs text-profit">
                <CheckIcon className="size-3.5" />
                Button text is easy to read ({ratio.toFixed(1)}:1).
              </p>
            )}
          </Panel>

          <Panel
            title="All colors"
            actions={
              Object.keys(colors).length > 0 && (
                <button type="button" onClick={() => setColors({})} className="text-xs text-muted hover:text-foreground">
                  Reset all colors
                </button>
              )
            }
          >
            <ul className="flex flex-col">
              {themeColors.map((name) => (
                <li key={name} className="flex items-center gap-3 border-t border-border py-2 first:border-t-0">
                  <input
                    type="color"
                    aria-label={colorText[name].label}
                    value={effective(name)}
                    onChange={(e) => isHexColor(e.target.value) && setColor(name, e.target.value)}
                    className="h-8 w-9 shrink-0 cursor-pointer rounded border border-border bg-transparent p-0.5"
                  />
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="text-sm font-medium">{colorText[name].label}</span>
                    <span className="text-xs text-muted">{colorText[name].hint}</span>
                  </span>
                  <span className="font-mono text-xs text-muted">{effective(name)}</span>
                  {colors[name] !== undefined && (
                    <button type="button" onClick={() => setColor(name, null)} className="text-xs text-muted hover:text-foreground" aria-label={`Reset ${colorText[name].label}`}>
                      Reset
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </Panel>
        </div>

        <section aria-labelledby="preview" className={`min-w-0 flex-col gap-3 xl:sticky xl:top-6 ${view === "settings" ? "hidden xl:flex" : "flex"}`}>
          <h2 id="preview" className="font-semibold">
            Preview
          </h2>
          <Preview colors={colors} settings={settings} />
        </section>
      </div>

      <div className="flex flex-wrap items-center gap-3 rounded-lg border border-border bg-panel px-4 py-3.5">
        <span className="flex-[1_1_14rem] text-sm text-muted">
          {save.isSuccess && !dirty ? "Saved. Your traders see the new look now." : dirty ? "Your traders see the new look when you save it." : "No changes yet."}
        </span>
        <button type="button" disabled={!dirty || save.isPending} onClick={() => setColors(initial)} className={secondaryButtonClass}>
          Discard
        </button>
        <button type="button" disabled={!dirty || save.isPending} onClick={submit} className={buttonClass}>
          {save.isPending ? "Saving..." : "Save design"}
        </button>
      </div>
      <ErrorText error={save.error} />
    </AdminPage>
  );
}

/** The colors in the same order, so that two sets compare by their values. */
function sorted(colors: ThemeColors): [string, string | undefined][] {
  return themeColors.map((name) => [name, colors[name]]);
}

/** The firm's logo's outline, once it has been read, to warn about a logo that disappears on the background. */
export function useLogoEdges(logoUrl: string | null): number[] | null {
  const [measured, setMeasured] = useState<{ url: string; edges: number[] | null } | null>(null);
  useEffect(() => {
    let current = true;
    if (logoUrl) {
      void imageEdges(logoUrl).then((edges) => current && setMeasured({ url: logoUrl, edges }));
    }

    return () => {
      current = false;
    };
  }, [logoUrl]);

  return measured && measured.url === logoUrl ? measured.edges : null;
}

/**
 * The firm's logo, uploaded and saved at once. Without one, the portal shows the firm's name. On the design page the
 * colors wait for Save design, which the text says; in the guide they are saved at once too.
 */
export function Logo({ settings, hardToSee, colorsWait = true }: { settings: FirmSettings; hardToSee: boolean; colorsWait?: boolean }) {
  const router = useRouter();
  const upload = useUploadLogo();
  const remove = useRemoveLogo();

  return (
    <Panel title="Logo">
      {settings.logoUrl ? (
        <FileTarget
          busy={upload.isPending}
          onFile={(file) => upload.mutate(file, { onSuccess: () => router.refresh() })}
          className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-background p-4"
        >
          <FirmLogo src={settings.logoUrl} alt="Your logo" className="h-10 max-w-[12rem]" />
          <span className="flex items-center gap-2">
            <label className={`${secondaryButtonClass} cursor-pointer text-sm has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-accent`}>
              {upload.isPending ? "Uploading..." : "Replace"}
              <input
                type="file"
                aria-label="Logo file"
                accept={logoTypes}
                disabled={upload.isPending}
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file) {
                    upload.mutate(file, { onSuccess: () => router.refresh() });
                  }

                  e.target.value = "";
                }}
                className="sr-only"
              />
            </label>
            <button
              type="button"
              disabled={remove.isPending}
              onClick={() => remove.mutate(undefined, { onSuccess: () => router.refresh() })}
              className={`${secondaryButtonClass} text-sm hover:text-loss`}
            >
              {remove.isPending ? "Removing..." : "Remove"}
            </button>
          </span>
        </FileTarget>
      ) : (
        <DropZone
          label="Logo file"
          title="Drop your logo here, or "
          hint="PNG, JPEG, WebP or SVG, at most 1 MB. About 40 px high in the portal."
          accept={logoTypes}
          busy={upload.isPending}
          onFile={(file) => upload.mutate(file, { onSuccess: () => router.refresh() })}
        />
      )}
      <p className="text-xs text-muted">
        {settings.logoUrl
          ? `Saved as soon as it is uploaded, and your traders see it at once. Drop a new file on it to replace it.${colorsWait ? " The colors wait for Save design." : ""}`
          : "Without a logo, your firm's name is shown. A logo is saved as soon as it is uploaded."}
      </p>
      {hardToSee && (
        <p role="status" className="flex gap-2 rounded-lg border border-warning/40 bg-warning/10 px-3 py-2.5 text-xs">
          <AlertIcon className="size-4 shrink-0 text-warning" />
          <span>Your logo is hard to see on this theme&apos;s panels. Upload a version for this background, or choose another theme.</span>
        </p>
      )}
      <ErrorText error={upload.error ?? remove.error} />
    </Panel>
  );
}

const logoTypes = "image/png,image/jpeg,image/webp,image/svg+xml";

// The logo's box, which a new file can also be dropped on.
function FileTarget({ busy, onFile, className, children }: { busy: boolean; onFile: (file: File) => void; className: string; children: React.ReactNode }) {
  const [dragging, setDragging] = useState(false);
  return (
    <div
      onDragOver={(e) => {
        e.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(e) => {
        e.preventDefault();
        setDragging(false);
        const file = e.dataTransfer.files[0];
        if (file && !busy) {
          onFile(file);
        }
      }}
      className={`${className} transition-colors ${dragging ? "border-accent bg-accent/5" : ""}`}
    >
      {children}
    </div>
  );
}

/** The trader's start page with the colors being chosen: the header, a notice and two accounts. */
function Preview({ colors, settings }: { colors: ThemeColors; settings: FirmSettings }) {
  return (
    <div style={themeStyle({ colors: colors as Record<string, string> }) as React.CSSProperties} className="overflow-hidden rounded-lg border border-border bg-background text-sm text-foreground">
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2 border-b border-border bg-panel px-4 py-2.5">
        {settings.logoUrl ? (
          <FirmLogo src={settings.logoUrl} alt={settings.name} className="h-7" />
        ) : (
          <span className="font-semibold">{settings.name}</span>
        )}
        <span className="flex gap-1">
          <span className="rounded-md bg-background px-2.5 py-1.5 font-medium">Accounts</span>
          <span className="px-2.5 py-1.5 text-muted">Payouts</span>
        </span>
        <span className="ml-auto flex items-center gap-2.5">
          <span className="rounded bg-accent px-3 py-1.5 font-medium text-accent-foreground">Buy a challenge</span>
          <span className="grid size-8 place-items-center rounded-full border border-border bg-background text-[10px] font-semibold">AN</span>
        </span>
      </div>
      <div className="flex flex-col gap-4 p-4 sm:p-5">
        <div className="flex flex-col gap-0.5">
          <span className="text-lg font-semibold">Your accounts</span>
          <span className="text-muted">Two accounts trading right now.</span>
        </div>
        <p className="flex items-center gap-2.5 rounded-lg border border-warning/40 bg-warning/10 px-3 py-2.5">
          <AlertIcon className="size-4 shrink-0 text-warning" />
          <span>
            <strong className="font-semibold">#1005 is 330.40 from its daily loss limit.</strong> <span className="text-muted">It resets when the day starts.</span>
          </span>
        </p>
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
          <PreviewCard name="Two-step 100K" number="#1003, 100,000.00 USD" stage="Phase 2" equity="103,655.20" result="+3,655.20" resultTone="text-profit" progress={68} daily="5,555.20 left" max="13,655.20 left" />
          <PreviewCard
            name="One-step 50K"
            number="#1005, 50,000.00 USD"
            stage="Phase 1"
            equity="48,230.40"
            result="-1,769.60"
            resultTone="text-loss"
            progress={0}
            daily="330.40 left"
            dailyTone="font-medium text-loss"
            max="1,230.40 left"
            maxTone="text-warning"
            warning
          />
        </div>
      </div>
    </div>
  );
}

function PreviewCard(props: {
  name: string;
  number: string;
  stage: string;
  equity: string;
  result: string;
  resultTone: string;
  progress: number;
  daily: string;
  dailyTone?: string;
  max: string;
  maxTone?: string;
  warning?: boolean;
}) {
  return (
    <div className={`flex flex-col gap-3 rounded-lg border bg-panel p-3.5 ${props.warning ? "border-warning/50" : "border-border"}`}>
      <div className="flex justify-between gap-2">
        <span className="flex flex-col">
          <span className="font-semibold">{props.name}</span>
          <span className="text-xs text-muted">{props.number}</span>
        </span>
        <span className="text-[11px] font-medium text-accent">{props.stage}</span>
      </div>
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-lg font-medium">{props.equity}</span>
        <span className={`${props.resultTone}`}>{props.result}</span>
      </div>
      <div className="h-1.5 overflow-hidden rounded-full bg-border">
        <div className="h-full rounded-full bg-profit" style={{ width: `${props.progress}%` }} />
      </div>
      <div className="flex justify-between gap-2 border-t border-border pt-2.5 text-[11px]">
        <span className="flex flex-col">
          <span className="text-muted">Daily loss limit</span>
          <span className={`text-[13px] ${props.dailyTone ?? ""}`}>{props.daily}</span>
        </span>
        <span className="flex flex-col items-end">
          <span className="text-muted">Max loss limit</span>
          <span className={`text-[13px] ${props.maxTone ?? ""}`}>{props.max}</span>
        </span>
      </div>
      <div className="flex justify-end gap-1.5 text-xs">
        <span className="rounded border border-border px-2.5 py-1.5 font-medium">Details</span>
        <span className="rounded bg-accent px-2.5 py-1.5 font-medium text-accent-foreground">Open terminal</span>
      </div>
    </div>
  );
}
