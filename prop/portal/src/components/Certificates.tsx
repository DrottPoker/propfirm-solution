"use client";

import { useEffect, useState } from "react";

import { useBranding } from "@/app/providers";
import type { AccountDetails } from "@/lib/api/types";
import { certificateHeight, certificatesOf, certificateSvg, certificateWidth, type Certificate } from "@/lib/certificate";
import { useMe } from "@/lib/queries";
import { defaultColors } from "@/lib/theme";

import { ErrorText, Panel, secondaryButtonClass } from "./ui";

/** The trader's certificates for the account, to download as an image and share: a passed challenge and every paid payout. */
export function Certificates({ details }: { details: AccountDetails }) {
  const branding = useBranding();
  const me = useMe("trader");
  const logo = useLogoData(branding.logoUrl);
  const certificates = certificatesOf(details);
  const traderName = me.data?.name ?? details.account.email;
  const accent = branding.colors.accent ?? defaultColors.accent;

  return (
    <Panel title="Certificates">
      <p className="text-sm text-muted">Download them as images to share.</p>
      <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        {certificates.map((certificate) => (
          <CertificateCard key={certificate.key} certificate={certificate} svg={certificateSvg(certificate, branding.name, traderName, accent, logo)} />
        ))}
      </ul>
    </Panel>
  );
}

function CertificateCard({ certificate, svg }: { certificate: Certificate; svg: string }) {
  const [error, setError] = useState<Error | null>(null);
  const source = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;

  return (
    <li className="flex flex-col gap-2.5">
      {/* eslint-disable-next-line @next/next/no-img-element -- a generated data URL, which next/image cannot optimize */}
      <img src={source} alt={`${certificate.title}: ${certificate.headline}`} className="w-full rounded-lg border border-border" />
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm">{certificate.headline}</span>
        <button type="button" onClick={() => download(source, certificate.fileName).catch(setError)} className={`${secondaryButtonClass} shrink-0 text-sm`}>
          Download
        </button>
      </div>
      <ErrorText error={error} />
    </li>
  );
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

// The image is drawn on a canvas and saved as a PNG, which every app can open.
async function download(source: string, fileName: string): Promise<void> {
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

  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(link.href);
}
