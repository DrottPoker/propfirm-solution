import { ForgotPasswordForm } from "@/components/PasswordReset";

// An administrator who forgot the password asks for a link here.
export default function ForgotPasswordPage() {
  return <ForgotPasswordForm audience="admin" />;
}
