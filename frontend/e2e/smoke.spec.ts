import { expect, test } from '@playwright/test'

test.use({ locale: 'da-DK' })

test('frontend reaches the backend health endpoint', async ({ page }) => {
  // The app sends a wake-up call to the backend as soon as it opens.
  const health = page.waitForResponse((response) => new URL(response.url()).pathname === '/health')
  await page.goto('/')

  await expect(page.getByRole('heading', { name: 'eduscape' })).toBeVisible()
  expect((await health).ok()).toBe(true)
  // An awake backend answers at once, so there is no "getting ready" notice.
  await expect(page.getByText(/Gør klar/)).toHaveCount(0)
})
