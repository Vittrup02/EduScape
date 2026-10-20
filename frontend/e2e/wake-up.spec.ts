import { expect, test, type Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { newPage, openDemoTemplate, readPin, registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

const wakeUpMessage = 'Gør klar … det tager op til et minut første gang.'

/**
 * Acts like Render's free tier after a quiet spell: for `ms` the backend cannot be reached (a refused connection, or
 * the proxy's 503 while the app starts), then it answers normally.
 */
async function sleepBackend(page: Page, ms: number) {
  const wakesAt = Date.now() + ms
  let calls = 0
  await page.route('http://localhost:5115/**', async (route) => {
    if (Date.now() >= wakesAt) {
      await route.continue()
    } else if (calls++ % 2 === 0) {
      await route.abort('connectionrefused')
    } else {
      await route.fulfill({ status: 503, body: 'Service Unavailable' })
    }
  })
}

test('a student joining while the backend wakes up sees "getting ready", not an error', async ({ page: teacher, browser }) => {
  await registerTeacher(teacher)
  await openDemoTemplate(teacher)
  await teacher.getByLabel('Klasse').fill('6.C')
  await teacher.getByRole('button', { name: 'Start session' }).click()
  const pin = await readPin(teacher)

  const student = await newPage(browser)
  await sleepBackend(student, 5000)
  await student.goto('/join')
  await expect(student.getByRole('status')).toHaveText(wakeUpMessage)

  // The student can type the PIN meanwhile; it goes through once the backend is awake.
  await student.getByLabel('PIN-kode').fill(pin)
  await student.getByRole('button', { name: 'Videre' }).click()
  await expect(student.getByRole('heading', { name: 'Vælg dit hold' })).toBeVisible({ timeout: 20_000 })
  await expect(student.getByText(wakeUpMessage)).toHaveCount(0)
  await expect(student.getByRole('alert')).toHaveCount(0)
})

test('a teacher logging in while the backend wakes up sees "getting ready", not an error', async ({ page, browser }) => {
  const email = `e2e-${randomUUID()}@example.com`
  const setup = await newPage(browser)
  await setup.goto('/register')
  await setup.getByLabel('E-mail').fill(email)
  await setup.getByLabel('Adgangskode').fill('en lang og sikker adgangskode')
  await setup.getByRole('button', { name: 'Opret konto' }).click()
  await expect(setup.getByRole('heading', { name: 'Mine rum' })).toBeVisible()

  await sleepBackend(page, 5000)
  await page.goto('/login')
  await expect(page.getByRole('status')).toHaveText(wakeUpMessage)
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Adgangskode').fill('en lang og sikker adgangskode')
  await page.getByRole('button', { name: 'Log ind' }).click()

  await expect(page.getByRole('heading', { name: 'Mine rum' })).toBeVisible({ timeout: 20_000 })
  await expect(page.getByText(wakeUpMessage)).toHaveCount(0)
  await expect(page.getByRole('alert')).toHaveCount(0)
})
