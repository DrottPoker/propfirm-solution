import { RequireRole } from "@/components/RequireRole";

// Every page of the admin panel needs a logged in administrator, and shares the panel's menu.
export default function AdminPanelLayout({ children }: LayoutProps<"/admin">) {
  return <RequireRole role="admin">{children}</RequireRole>;
}
