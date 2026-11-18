import { defineConfig, devices } from '@playwright/test';

// The browser tests start the real API (on a throwaway database) and the real web app, then drive them like a person.
// The web app is built to talk to the API at http://localhost:5293, so that port must be free: stop your own API first.

export const DEMO_PASSWORD = 'E2e#Pass-12345';

const connection =
  process.env.E2E_CONNECTION ||
  'Server=(localdb)\\MSSQLLocalDB;Database=ResolveIQ_E2E;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True';

export default defineConfig({
  testDir: './tests',
  // One browser at a time: the tests share one database, and the order of their steps matters.
  workers: 1,
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',

  use: {
    baseURL: 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // On a laptop with Edge installed, no extra download is needed: E2E_BROWSER=msedge.
    channel: process.env.E2E_BROWSER || undefined,
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], channel: process.env.E2E_BROWSER || undefined } }],

  webServer: [
    {
      // Rebuild the empty database, then start the API with demo accounts. Limits are raised so a fast test run is not slowed down.
      command: 'node scripts/prepare-db.js && dotnet run --project ../backend/ResolveIQ.API --no-launch-profile',
      url: 'http://localhost:5293/health',
      reuseExistingServer: false,
      timeout: 240_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: 'http://localhost:5293',
        ConnectionStrings__DefaultConnection: connection,
        // A throwaway signing key for this test run only (the real one lives in user secrets, never in code).
        Jwt__Key: 'e2e-only-signing-key-not-a-secret-0123456789abcdef',
        Seed__DemoPassword: DEMO_PASSWORD,
        RateLimiting__AuthPermitLimit: '1000',
        RateLimiting__GeneralPermitLimit: '10000',
        Maintenance__Enabled: 'false',
      },
    },
    {
      command: 'npx ng serve --port 4200',
      cwd: '../frontend',
      url: 'http://localhost:4200',
      reuseExistingServer: false,
      timeout: 240_000,
    },
  ],
});
