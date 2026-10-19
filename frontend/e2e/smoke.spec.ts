import { expect, test } from '@playwright/test'

test('frontend reaches the backend health endpoint', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: 'eduscape' })).toBeVisible()
  await expect(page.getByRole('status')).toHaveText('Serveren svarer')
})
