import { ResetPasswordForm } from "@/components/PasswordReset";

// The email with a link to choose a new password links here, for example /ops/reset-password?token=<token>.
export default async function ResetPasswordPage({ searchParams }: PageProps<"/ops/reset-password">) {
  const { token } = await searchParams;
  return <ResetPasswordForm audience="staff" token={typeof token === "string" && token.length > 0 ? token : null} />;
}
