import { ResetPasswordForm } from "@/components/PasswordReset";

// The email with a link to choose a new password links here, for example /admin/reset-password?token=<token>.
export default async function ResetPasswordPage({ searchParams }: PageProps<"/admin/reset-password">) {
  const { token } = await searchParams;
  return <ResetPasswordForm audience="admin" token={typeof token === "string" && token.length > 0 ? token : null} />;
}
