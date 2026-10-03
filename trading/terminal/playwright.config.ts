import { defineConfig, devices } from "@playwright/test";

// Own ports and database, so the tests can run while you develop.
export const servicePort = 5121;
const terminalPort = 3021;
const database = "Host=localhost;Port=5432;Database=trading_e2e;Username=trading;Password=trading";

export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  timeout: 60_000,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://localhost:${terminalPort}`,
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      // Synthetic prices even when the developer's user secrets choose a real price feed.
      command: `dotnet run --project ../src/Trading.Service -c Release --no-build -- --urls http://localhost:${servicePort} --Cors:AllowedOrigins:0=http://localhost:${terminalPort} --ConnectionStrings:Trading=${JSON.stringify(database)} --PriceFeed:Provider=Synthetic`,
      url: `http://localhost:${servicePort}/health`,
      timeout: 120_000,
    },
    {
      command: `pnpm exec next dev --port ${terminalPort}`,
      url: `http://localhost:${terminalPort}/login`,
      env: { NEXT_PUBLIC_TRADING_API_URL: `http://localhost:${servicePort}`, NEXT_DIST_DIR: ".next-e2e" },
      timeout: 120_000,
    },
  ],
});
