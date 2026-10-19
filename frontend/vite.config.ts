import react from '@vitejs/plugin-react'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'
import { securityHeaders } from './security/securityHeaders.ts'
import { defaultApiUrl } from './src/api/defaultApiUrl.ts'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // Same value Vite bakes into the bundle (.env files or the Workers Builds build variable)
  const env: Partial<Record<string, string>> = loadEnv(mode, process.cwd(), 'VITE_')
  const apiUrl = env.VITE_API_URL ?? defaultApiUrl

  return {
    plugins: [react(), securityHeaders(apiUrl)],
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      // Playwright specs live in /e2e and are run by `npm run test:e2e`.
      include: ['src/**/*.test.{ts,tsx}', 'security/**/*.test.ts'],
    },
  }
})
