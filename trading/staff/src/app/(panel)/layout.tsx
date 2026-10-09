import { StaffFrame } from "@/components/StaffFrame";

export default function PanelLayout({ children }: LayoutProps<"/">) {
  return <StaffFrame>{children}</StaffFrame>;
}
