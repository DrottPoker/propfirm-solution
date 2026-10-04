/**
 * The page to return to after logging in, from a ?next= parameter: only a path on this site, so a link cannot send
 * someone elsewhere after they logged in. Null for anything else.
 */
export function safeNext(next: string | string[] | null | undefined): string | null {
  return typeof next === "string" && next.startsWith("/") && !next.startsWith("//") && !next.startsWith("/\\") ? next : null;
}

/** The login page that returns to the page afterwards, or the login page alone for the role's home. */
export function loginWithNext(login: string, home: string, here: string): string {
  return here === home ? login : `${login}?next=${encodeURIComponent(here)}`;
}
