/** Base address of the trading service. Override with NEXT_PUBLIC_TRADING_API_URL. */
export const tradingApiUrl = process.env.NEXT_PUBLIC_TRADING_API_URL ?? "http://localhost:5101";

/** Our staff view in Kronant Prop, linked from the menu. Override with NEXT_PUBLIC_PROP_STAFF_URL, or set it empty to hide the link. */
export const propStaffUrl = process.env.NEXT_PUBLIC_PROP_STAFF_URL ?? "http://ops.localhost:3002/ops";

/** Which platform this is, shown at the top of every page so nobody acts on the wrong one. Set NEXT_PUBLIC_ENVIRONMENT_NAME per server. */
export const environmentName = process.env.NEXT_PUBLIC_ENVIRONMENT_NAME ?? "Development";

/** The platform is our own brand (ADR 0046). */
export const productName = "Kronant Trader";
