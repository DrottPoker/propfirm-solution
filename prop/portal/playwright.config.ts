import { defineConfig, devices } from "@playwright/test";

// The whole chain: the trading platform, Prop.Api and the portal. Own ports and databases, so the tests can
// run while you develop.
export const tradingPort = 5122;
const propApiPort = 5222;
export const portalPort = 3022;

/** Where firms sign up, and the address template of new firms' portals. Browsers send *.localhost to this computer. */
export const platformUrl = `http://app.localhost:${portalPort}`;
const firmPortalUrl = `http://{firm}.localhost:${portalPort}/`;

/** Our own admin view, where our staff review firms. */
export const opsUrl = `http://ops.localhost:${portalPort}`;

/** The trading platform's terminal address in login links. Nothing runs there: the tests stop at the link. */
export const terminalUrl = "http://localhost:3023";

const tradingDatabase = "Host=localhost;Port=5432;Database=trading_portal_e2e;Username=trading;Password=trading";
const propDatabase = "Host=localhost;Port=5432;Database=prop_e2e;Username=prop;Password=prop";

export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  timeout: 60_000,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://localhost:${portalPort}`,
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      // Synthetic prices even when the developer's user secrets choose a real price feed.
      command: `dotnet run --project ../../trading/src/Trading.Service -c Release --no-build -- --urls http://localhost:${tradingPort} --ConnectionStrings:Trading=${JSON.stringify(tradingDatabase)} --Terminal:Url=${terminalUrl}/ --PriceFeed:Provider=Synthetic`,
      url: `http://localhost:${tradingPort}/health`,
      timeout: 120_000,
    },
    {
      // Test ID checks even when the developer's user secrets choose Didit.
      command: `dotnet run --project ../src/Prop.Api -c Release --no-build -- --urls http://localhost:${propApiPort} --ConnectionStrings:Prop=${JSON.stringify(propDatabase)} --TradingPlatform:Url=http://localhost:${tradingPort}/ --Firms:0:Portal:Url=http://localhost:${portalPort}/ --Platform:Url=${platformUrl}/ --Platform:FirmPortalUrl=${firmPortalUrl} --Platform:OpsUrl=${opsUrl}/ --Identity:Provider=Test`,
      url: `http://localhost:${propApiPort}/health`,
      timeout: 120_000,
    },
    {
      command: `pnpm exec next dev --port ${portalPort}`,
      url: `http://localhost:${portalPort}/login`,
      env: { PROP_API_URL: `http://localhost:${propApiPort}`, NEXT_DIST_DIR: ".next-e2e" },
      timeout: 120_000,
    },
  ],
});
