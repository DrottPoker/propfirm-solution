import { ForgotPasswordForm } from "@/components/PasswordReset";

// Our staff ask for a link here when they forgot the password.
export default function ForgotPasswordPage() {
  return <ForgotPasswordForm audience="staff" />;
}
