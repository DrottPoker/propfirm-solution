"use client";

import { useState } from "react";

import type { CustomDomain } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { useCheckDomain, useDomain, useFirmSettings, useRemoveDomain, useSaveDomain } from "@/lib/queries";

import { CopyButton } from "./CopyButton";
import { ConfirmDialog } from "./Dialog";
import { AdminPage, Badge, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/**
 * The firm's own domain for its portal (ADR 0039): the firm adds it, puts two DNS records at its domain host, and the
 * portal moves there once we find them. The address with us keeps working.
 */
export function AdminDomain() {
  const domain = useDomain();
  const settings = useFirmSettings();

  if (domain.isError) {
    return <Message text={domain.error.message} />;
  }

  if (!domain.data || !settings.data) {
    return <Message text="Loading..." />;
  }

  const data = domain.data;
  return (
    <AdminPage narrow>
      <PageHeader
        title="Your domain"
        description={
          <>
            Your portal is at <span className="text-foreground">{settings.data.portalUrl}</span>. With your own domain, traders find it at an address such as
            portal.yourfirm.com instead.
          </>
        }
      />
      {!data.available ? (
        <Panel>
          <p className="text-sm text-muted">Own domains are not on here yet.</p>
        </Panel>
      ) : data.domain === null ? (
        <AddDomain />
      ) : (
        <DomainRecords domain={data} />
      )}
    </AdminPage>
  );
}

function AddDomain() {
  const save = useSaveDomain();
  const [domain, setDomain] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    save.mutate(domain.trim());
  };

  return (
    <Panel title="Add your domain">
      <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
        <label className="flex min-w-60 flex-1 flex-col gap-1 text-sm">
          <span className="text-muted">Domain</span>
          <input required value={domain} onChange={(e) => setDomain(e.target.value)} placeholder="portal.yourfirm.com" className={`${fieldClass} font-mono`} />
        </label>
        <button type="submit" disabled={save.isPending} className={buttonClass}>
          {save.isPending ? "Adding..." : "Add domain"}
        </button>
      </form>
      <ErrorText error={save.error} />
      <p className="text-xs text-muted">Use a subdomain, such as portal. or members., so your own website stays where it is. Next you add two DNS records.</p>
    </Panel>
  );
}

function DomainRecords({ domain }: { domain: CustomDomain }) {
  const check = useCheckDomain();
  const remove = useRemoveDomain();
  const [removing, setRemoving] = useState(false);
  const active = domain.status === "Active";
  const records = [
    { type: "CNAME", name: domain.domain!, value: domain.cnameTarget, why: "Points your domain to us." },
    { type: "TXT", name: domain.txtName!, value: domain.txtValue!, why: "Shows the domain is yours." },
  ];

  return (
    <>
      <Panel
        title={domain.domain!}
        actions={<Badge tone={active ? "profit" : "warning"}>{active ? "Your portal's address" : "Waiting for the records"}</Badge>}
      >
        {active ? (
          <p className="text-sm">
            Your portal is at <span className="font-mono">https://{domain.domain}/</span>
            {domain.activeAt && <span className="text-muted"> since {formatDateTime(domain.activeAt)}</span>}. The secure certificate is made the first time it is opened, and your
            address with us keeps working.
          </p>
        ) : (
          <p className="text-sm text-muted">
            Add these two records where you manage your domain&apos;s DNS. We look for them every few minutes, and the portal moves to your domain once both are there.
            DNS can take up to an hour to show new records.
          </p>
        )}
        <div className="overflow-x-auto">
          <table className="w-full min-w-[36rem] text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="py-2 font-normal">
                  Type
                </th>
                <th scope="col" className="py-2 pl-4 font-normal">
                  Name
                </th>
                <th scope="col" className="py-2 pl-4 font-normal">
                  Value
                </th>
              </tr>
            </thead>
            <tbody>
              {records.map((record) => (
                <tr key={record.type} className="border-t border-border align-top">
                  <td className="py-2.5 font-mono">{record.type}</td>
                  <td className="py-2.5 pl-4">
                    <span className="flex items-center gap-2 font-mono break-all">
                      {record.name} <CopyButton value={record.name} label={`${record.type} name`} />
                    </span>
                  </td>
                  <td className="py-2.5 pl-4">
                    <span className="flex items-center gap-2 font-mono break-all">
                      {record.value} <CopyButton value={record.value} label={`${record.type} value`} />
                    </span>
                    <span className="text-xs text-muted">{record.why}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!active && (
          <div className="flex flex-col gap-2">
            {domain.problem && (
              <p role="status" className="text-sm text-warning">
                {domain.problem}
                {domain.checkedAt && <span className="text-muted"> Looked {formatDateTime(domain.checkedAt)}.</span>}
              </p>
            )}
            <div className="flex flex-wrap gap-2">
              <button type="button" disabled={check.isPending} onClick={() => check.mutate()} className={buttonClass}>
                {check.isPending ? "Looking..." : "Check now"}
              </button>
            </div>
            <ErrorText error={check.error} />
          </div>
        )}
      </Panel>
      <div>
        <button type="button" onClick={() => setRemoving(true)} className={`${secondaryButtonClass} text-loss`}>
          Remove domain
        </button>
      </div>
      <ConfirmDialog
        open={removing}
        onClose={() => setRemoving(false)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => setRemoving(false) })}
        title={`Remove ${domain.domain}?`}
        description="Your portal goes back to its address with us. Links to your domain stop working until you add it again."
        confirmLabel="Remove domain"
        pendingLabel="Removing..."
        pending={remove.isPending}
        danger
      >
        <ErrorText error={remove.error} />
      </ConfirmDialog>
    </>
  );
}
