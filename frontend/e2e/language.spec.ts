import { expect, test } from '@playwright/test'

test.describe('with a browser that prefers English', () => {
  test.use({ locale: 'en-GB' })

  test('shows English', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('status')).toHaveText('The server is responding')
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')
  })
})

test.describe('with a browser in an unsupported language', () => {
  test.use({ locale: 'de-DE' })

  test('falls back to Danish', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('status')).toHaveText('Serveren svarer')
  })
})

test.describe('with a browser that prefers Danish', () => {
  test.use({ locale: 'da-DK' })

  test('remembers a chosen language after reload', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('status')).toHaveText('Serveren svarer')

    await page.getByRole('button', { name: 'EN' }).click()
    await expect(page.getByRole('status')).toHaveText('The server is responding')

    await page.reload()
    await expect(page.getByRole('status')).toHaveText('The server is responding')
    await expect(page.getByRole('button', { name: 'EN' })).toHaveAttribute('aria-pressed', 'true')
  })
})
