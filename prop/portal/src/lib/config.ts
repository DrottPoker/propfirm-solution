/**
 * Where the portal's server reaches Prop.Api. Browsers never call it directly: they call the portal, which
 * passes /api/portal on. Read when the portal starts or is built, so set PROP_API_URL before `next build`.
 */
export const propApiUrl = process.env.PROP_API_URL ?? "http://localhost:5201";
