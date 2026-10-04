import { redirect } from "next/navigation";

// The settings were split into the portal's design, its checkout and the integrations. Old links go to the design.
export default function AdminSettingsPage() {
  redirect("/admin/design");
}
