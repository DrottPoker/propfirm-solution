import { defineConfig, devices } from "@playwright/test";

// Own ports and database, so the tests can run while you develop and beside the terminal's tests.
export const servicePort = 5131;
const staffPort = 3031;
const database = "Host=localhost;Port=5432;Database=trading_staff_e2e;Username=trading;Password=trading";

export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  timeout: 60_000,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://localhost:${staffPort}`,
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      // Synthetic prices even when the developer's user secrets choose a real price feed.
      command: `dotnet run --project ../src/Trading.Service -c Release --no-build -- --urls http://localhost:${servicePort} --Cors:AllowedOrigins:0=http://localhost:${staffPort} --ConnectionStrings:Trading=${JSON.stringify(database)} --Terminal:Url=http://localhost:3001/ --PriceFeed:Provider=Synthetic`,
      url: `http://localhost:${servicePort}/health`,
      timeout: 120_000,
    },
    {
      command: `pnpm exec next dev --port ${staffPort}`,
      url: `http://localhost:${staffPort}/login`,
      env: { NEXT_PUBLIC_TRADING_API_URL: `http://localhost:${servicePort}`, NEXT_PUBLIC_ENVIRONMENT_NAME: "End-to-end", NEXT_DIST_DIR: ".next-e2e" },
      timeout: 120_000,
    },
  ],
});
