import { expect, test } from '@playwright/test'

test.describe('with a browser that prefers English', () => {
  test.use({ locale: 'en-GB' })

  test('shows English', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('link', { name: 'Join with a PIN' })).toBeVisible()
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')
  })
})

test.describe('with a browser in an unsupported language', () => {
  test.use({ locale: 'de-DE' })

  test('falls back to Danish', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('link', { name: 'Deltag med PIN-kode' })).toBeVisible()
  })
})

test.describe('with a browser that prefers Danish', () => {
  test.use({ locale: 'da-DK' })

  test('remembers a chosen language after reload', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('link', { name: 'Deltag med PIN-kode' })).toBeVisible()

    await page.getByRole('button', { name: 'EN' }).click()
    await expect(page.getByRole('link', { name: 'Join with a PIN' })).toBeVisible()

    await page.reload()
    await expect(page.getByRole('link', { name: 'Join with a PIN' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'EN' })).toHaveAttribute('aria-pressed', 'true')
  })
})
