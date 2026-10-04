import { RequireStaff } from "@/components/RequireStaff";

// Every page of our admin view needs a logged in staff member, and shares its menu.
export default function OpsPanelLayout({ children }: LayoutProps<"/ops">) {
  return <RequireStaff>{children}</RequireStaff>;
}
