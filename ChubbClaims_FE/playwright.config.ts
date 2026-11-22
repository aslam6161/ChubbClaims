import { defineConfig, devices } from '@playwright/test';

const WEB_URL = process.env['E2E_WEB_URL'] ?? 'http://localhost:4200';

/**
 * Browser tests against the running stack (API, SQL Server, Kafka, SignalR): `npm run e2e`.
 * The dev server is started if it isn't already running; the API must be (docker compose up).
 */
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  // One at a time: tests read queues, counts and notification badges that a parallel test
  // creating claims would change underneath them.
  workers: 1,
  fullyParallel: false,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  forbidOnly: !!process.env['CI'],
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'e2e-report' }]],
  outputDir: 'e2e-results',
  globalSetup: './e2e/support/global-setup.ts',
  use: {
    baseURL: WEB_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } },
    },
  ],
  webServer: {
    command: 'npm start',
    url: WEB_URL,
    reuseExistingServer: true,
    timeout: 180_000,
  },
});
