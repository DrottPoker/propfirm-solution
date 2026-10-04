import { ResetPasswordForm } from "@/components/PasswordReset";

// The email with a link to choose a new password links here, for example /reset-password?token=<token>.
export default async function ResetPasswordPage({ searchParams }: PageProps<"/reset-password">) {
  const { token } = await searchParams;
  return <ResetPasswordForm audience="trader" token={typeof token === "string" && token.length > 0 ? token : null} />;
}
