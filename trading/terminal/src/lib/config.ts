/** Base address of the trading service. Override with NEXT_PUBLIC_TRADING_API_URL. */
export const tradingApiUrl = process.env.NEXT_PUBLIC_TRADING_API_URL ?? "http://localhost:5101";

/** The platform is our own brand, not the firms' (ADR 0046). */
export const productName = "Kronant Trader";
