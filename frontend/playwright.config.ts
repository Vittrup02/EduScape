import { defineConfig, devices } from '@playwright/test'

const isCI = Boolean(process.env.CI)

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 2 : 0,
  reporter: isCI ? [['html', { open: 'never' }], ['list']] : 'list',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  // Starts both apps so E2E runs the same way locally and in CI.
  webServer: [
    {
      // Release build: closer to production (and Windows Smart App Control blocks unsigned Debug builds).
      command: 'dotnet run --project ../backend/Eduscape.Api --launch-profile http -c Release',
      url: 'http://localhost:5115/health',
      reuseExistingServer: !isCI,
      timeout: 180_000,
      stdout: 'pipe',
    },
    {
      command: 'npm run dev -- --port 5173 --strictPort',
      url: 'http://localhost:5173',
      reuseExistingServer: !isCI,
      timeout: 60_000,
    },
  ],
})
