import { defineConfig, devices } from '@playwright/test'

const repositoryDirectory = new URL('../', import.meta.url).pathname
const frontendDirectory = new URL('./', import.meta.url).pathname

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://127.0.0.1:5174',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...devices['Desktop Chrome'],
    channel: process.env.CI ? undefined : 'chrome'
  },
  webServer: [
    {
      command: 'dotnet run --no-build --project backend/Oa.Api/Oa.Api.csproj --urls http://127.0.0.1:5235',
      cwd: repositoryDirectory,
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Cors__AllowedOrigins__0: 'http://127.0.0.1:5174' },
      url: 'http://127.0.0.1:5235/health/ready',
      reuseExistingServer: false,
      timeout: 120_000
    },
    {
      command: 'npm run dev -- --host 127.0.0.1 --port 5174',
      cwd: frontendDirectory,
      env: { ...process.env, VITE_API_BASE_URL: 'http://127.0.0.1:5235/api/v1' },
      url: 'http://127.0.0.1:5174',
      reuseExistingServer: false,
      timeout: 60_000
    }
  ]
})
