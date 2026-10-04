import { ForgotPasswordForm } from "@/components/PasswordReset";

// A trader who forgot the password asks for a link here.
export default function ForgotPasswordPage() {
  return <ForgotPasswordForm audience="trader" />;
}
