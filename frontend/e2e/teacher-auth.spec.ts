import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'

test.use({ locale: 'da-DK' })

test('teacher registers, stays signed in, logs out and logs in again', async ({ page }) => {
  // A new account per run; the CI database is thrown away afterwards.
  const email = `e2e-${randomUUID()}@example.com`
  const password = 'en lang og sikker adgangskode'

  // Teacher pages require login.
  await page.goto('/teacher')
  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('heading', { name: 'Log ind' })).toBeVisible()

  // Register: signed in straight away and sent on to the page they wanted.
  await page.getByRole('link', { name: 'Opret en konto' }).click()
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Adgangskode').fill(password)
  await page.getByRole('button', { name: 'Opret konto' }).click()

  await expect(page.getByRole('heading', { name: 'Mine rum' })).toBeVisible()
  await expect(page).toHaveURL(/\/teacher$/)
  await expect(page.getByText('Du har ingen rum endnu.')).toBeVisible()
  await expect(page.getByText(`Logget ind som ${email}`)).toBeVisible()

  // A reload keeps the teacher signed in.
  await page.reload()
  await expect(page.getByText('Du har ingen rum endnu.')).toBeVisible()

  // Logging out leaves the teacher page, which then requires login again.
  await page.getByRole('button', { name: 'Log ud' }).click()
  await expect(page.getByRole('heading', { name: 'Log ind' })).toBeVisible()
  await page.goto('/teacher')
  await expect(page).toHaveURL(/\/login$/)

  // A wrong password is rejected with a translated message.
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Adgangskode').fill('forkert adgangskode')
  await page.getByRole('button', { name: 'Log ind' }).click()
  await expect(page.getByRole('alert')).toHaveText('Forkert e-mail eller adgangskode.')

  // The right password signs the teacher in again.
  await page.getByLabel('Adgangskode').fill(password)
  await page.getByRole('button', { name: 'Log ind' }).click()
  await expect(page.getByRole('heading', { name: 'Mine rum' })).toBeVisible()
  await expect(page.getByText('Du har ingen rum endnu.')).toBeVisible()
})
