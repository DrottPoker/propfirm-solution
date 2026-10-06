"use client";

import { useEffect, useState } from "react";

import { useBranding } from "@/app/providers";
import type { AccountDetails } from "@/lib/api/types";
import { certificateColors, certificateHeight, certificatesOf, certificateSvg, certificateWidth, shareText, type Certificate } from "@/lib/certificate";
import { useMe, useSetMyName } from "@/lib/queries";

import { buttonClass, ErrorText, fieldClass, Panel, secondaryButtonClass, Skeleton } from "./ui";

/**
 * The trader's certificates for the account, to download as an image and share: a passed challenge and every paid
 * payout, in the firm's colors and logo. They carry the trader's name, so a trader without one writes it first,
 * rather than sharing the email address.
 */
export function Certificates({ details }: { details: AccountDetails }) {
  const branding = useBranding();
  const me = useMe("trader");
  const logo = useLogoData(branding.logoUrl);
  const certificates = certificatesOf(details);
  const traderName = me.data?.name;
  const colors = certificateColors(branding.colors);

  return (
    <div id="certificates" className="scroll-mt-20">
      <Panel title="Certificates">
        {!me.data ? (
          <Skeleton className="aspect-[8/5] w-full max-w-md rounded-xl" />
        ) : !traderName ? (
          <NameForm firmName={branding.name} />
        ) : (
          <>
            <p className="text-sm text-muted">Download them as images, or share them where you like.</p>
            <ul className="grid grid-cols-1 gap-5 sm:grid-cols-2">
              {certificates.map((certificate) => (
                <CertificateCard
                  key={certificate.key}
                  certificate={certificate}
                  firmName={branding.name}
                  svg={certificateSvg(certificate, branding.name, traderName, colors, logo)}
                />
              ))}
            </ul>
          </>
        )}
      </Panel>
    </div>
  );
}

// The name on the certificates, written once. The firm knows the trader by it, so only the firm changes it later.
function NameForm({ firmName }: { firmName: string }) {
  const setName = useSetMyName();
  const [name, setNameText] = useState("");

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim()) {
          setName.mutate(name.trim());
        }
      }}
      className="flex flex-col gap-3"
    >
      <p className="text-sm text-muted">Your certificates are ready. Write your name as it should stand on them. {firmName} knows you by it, so only they can change it afterwards.</p>
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex min-w-60 flex-1 flex-col gap-1 text-sm">
          <span className="text-muted">Your name</span>
          <input value={name} onChange={(e) => setNameText(e.target.value)} maxLength={100} autoComplete="name" className={fieldClass} />
        </label>
        <button type="submit" disabled={setName.isPending || !name.trim()} className={buttonClass}>
          {setName.isPending ? "Saving..." : "Show my certificates"}
        </button>
      </div>
      <ErrorText error={setName.error} />
    </form>
  );
}

function CertificateCard({ certificate, firmName, svg }: { certificate: Certificate; firmName: string; svg: string }) {
  const [error, setError] = useState<Error | null>(null);
  const source = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
  const text = shareText(certificate, firmName);

  return (
    <li className="group flex flex-col gap-3">
      {/* eslint-disable-next-line @next/next/no-img-element -- a generated data URL, which next/image cannot optimize */}
      <img
        src={source}
        alt={`${certificate.title}: ${certificate.headline}`}
        className="w-full rounded-xl border border-border shadow-raised transition-transform duration-500 ease-out-soft group-hover:-translate-y-1 group-hover:-rotate-[0.6deg]"
      />
      <span className="text-sm font-medium">{certificate.headline}</span>
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={() => download(source, certificate.fileName).catch(setError)} className={`${secondaryButtonClass} text-sm`}>
          Download
        </button>
        <button type="button" onClick={() => share(source, certificate.fileName, text).catch(setError)} className={`${secondaryButtonClass} text-sm`}>
          Share
        </button>
        <button type="button" onClick={() => openShare(`https://x.com/intent/post?text=${encodeURIComponent(text)}&url=${portalUrl()}`)} className={`${secondaryButtonClass} text-sm`}>
          Post on X
        </button>
        <button type="button" onClick={() => openShare(`https://www.linkedin.com/sharing/share-offsite/?url=${portalUrl()}`)} className={`${secondaryButtonClass} text-sm`}>
          LinkedIn
        </button>
      </div>
      <ErrorText error={error} />
    </li>
  );
}

// The firm's shop, which a shared post links to, so others can find the firm.
function portalUrl(): string {
  return encodeURIComponent(`${window.location.origin}/buy`);
}

function openShare(url: string) {
  window.open(url, "_blank", "noopener,noreferrer");
}

// The image itself, where the device can share files, such as a phone. Elsewhere it is downloaded to share by hand.
async function share(source: string, fileName: string, text: string): Promise<void> {
  const blob = await pngOf(source);
  const file = new File([blob], fileName, { type: "image/png" });
  if (navigator.canShare?.({ files: [file] })) {
    try {
      await navigator.share({ files: [file], text });
    } catch (error) {
      // Closing the share sheet is not an error.
      if (!(error instanceof DOMException && error.name === "AbortError")) {
        throw error;
      }
    }

    return;
  }

  save(blob, fileName);
}

// The firm's logo as a data URL, so it is part of the image. It is on the portal's own address, so it can be read.
function useLogoData(url: string | null): string | null {
  const [data, setData] = useState<string | null>(null);
  useEffect(() => {
    if (!url) {
      return;
    }

    let cancelled = false;
    fetch(url)
      .then((response) => (response.ok ? response.blob() : null))
      .then((blob) => (blob ? dataUrlOf(blob) : null))
      .then((value) => !cancelled && setData(value))
      .catch(() => !cancelled && setData(null));
    return () => {
      cancelled = true;
    };
  }, [url]);

  return url ? data : null;
}

function dataUrlOf(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}

async function download(source: string, fileName: string): Promise<void> {
  save(await pngOf(source), fileName);
}

// The image is drawn on a canvas as a PNG, which every app can open.
async function pngOf(source: string): Promise<Blob> {
  const image = new Image();
  image.src = source;
  await image.decode();
  const canvas = document.createElement("canvas");
  canvas.width = certificateWidth;
  canvas.height = certificateHeight;
  canvas.getContext("2d")!.drawImage(image, 0, 0, certificateWidth, certificateHeight);
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
  if (!blob) {
    throw new Error("The image could not be made. Try again.");
  }

  return blob;
}

function save(blob: Blob, fileName: string) {
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(link.href);
}
