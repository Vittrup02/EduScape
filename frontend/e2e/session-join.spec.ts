import { randomUUID } from 'node:crypto'
import { expect, test, type Browser, type Page } from '@playwright/test'

test.use({ locale: 'da-DK' })

async function newPage(browser: Browser): Promise<Page> {
  // Separate contexts: the teacher and each student have their own browser storage, like separate devices.
  const context = await browser.newContext({ locale: 'da-DK' })
  return context.newPage()
}

async function joinWithPin(page: Page, pin: string) {
  await page.goto('/')
  await page.getByRole('link', { name: 'Deltag med PIN-kode' }).click()
  await page.getByLabel('PIN-kode').fill(pin)
  await page.getByRole('button', { name: 'Videre' }).click()
}

test('teacher starts a session, a student joins with the PIN and stays on the team', async ({ page: teacher, browser }) => {
  // A new teacher per run; the CI database is thrown away afterwards.
  await teacher.goto('/register')
  await teacher.getByLabel('E-mail').fill(`e2e-${randomUUID()}@example.com`)
  await teacher.getByLabel('Adgangskode').fill('en lang og sikker adgangskode')
  await teacher.getByRole('button', { name: 'Opret konto' }).click()

  // Start the seeded demo template in team mode with 3 teams.
  const templates = teacher.getByRole('list', { name: 'Skabeloner' })
  await templates.getByRole('link', { name: /Start session: Demo: Flugten fra matematiklokalet/ }).click()
  await teacher.getByLabel('Klasse').fill('9.A')
  await teacher.getByRole('button', { name: 'Færre hold' }).click()
  await expect(teacher.getByRole('group', { name: 'Antal hold' })).toContainText('3')
  await teacher.getByRole('button', { name: 'Start session' }).click()

  const pinElement = teacher.getByLabel('PIN-kode')
  await expect(pinElement).toHaveText(/^\d{6}$/)
  const pin = (await pinElement.textContent()) ?? ''
  await expect(teacher.getByText('Hold Rød')).toBeVisible()
  await expect(teacher.getByText('Hold Grøn')).toBeVisible()
  await expect(teacher.getByText('Hold Gul')).toHaveCount(0)

  // The student joins: PIN → team → nickname.
  const student = await newPage(browser)
  await joinWithPin(student, pin)
  await student.getByRole('button', { name: 'Hold Blå' }).click()
  await student.getByLabel('Kaldenavn (frivilligt)').fill('Ida')
  await student.getByRole('button', { name: 'Deltag' }).click()
  await expect(student.getByRole('heading', { name: 'Du er på Hold Blå' })).toBeVisible()

  // A reload brings the student back to the same team via the device token.
  await student.reload()
  await expect(student.getByRole('heading', { name: 'Du er på Hold Blå' })).toBeVisible()
  await expect(student.getByText('Du er med som Ida.')).toBeVisible()

  // The teacher sees the student on the team.
  await teacher.getByRole('button', { name: 'Opdater' }).click()
  const blueTeam = teacher.getByRole('listitem').filter({ hasText: 'Hold Blå' })
  await expect(blueTeam.getByText('Ida')).toBeVisible()

  // Closing the session (after confirming) ends it for the student and the PIN stops working.
  await teacher.getByRole('button', { name: 'Luk session' }).click()
  await teacher.getByRole('button', { name: 'Ja, luk sessionen' }).click()
  await expect(teacher.getByText('Sessionen er lukket. PIN-koden virker ikke længere.')).toBeVisible()

  await student.reload()
  await expect(student.getByRole('alert')).toHaveText('Sessionen er lukket.')

  const latecomer = await newPage(browser)
  await joinWithPin(latecomer, pin)
  await expect(latecomer.getByRole('alert')).toHaveText(
    'Der er ingen aktiv session med den PIN-kode. Tjek koden, og prøv igen.',
  )
})
