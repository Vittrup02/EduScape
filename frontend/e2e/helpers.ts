import { randomUUID } from 'node:crypto'
import { expect, type Browser, type Page } from '@playwright/test'

/** A separate context, so the teacher and each student have their own browser storage, like separate devices. */
export async function newPage(browser: Browser): Promise<Page> {
  const context = await browser.newContext({ locale: 'da-DK' })
  return context.newPage()
}

/** Registers a new teacher; the CI database is thrown away afterwards. */
export async function registerTeacher(page: Page) {
  await page.goto('/register')
  await page.getByLabel('E-mail').fill(`e2e-${randomUUID()}@example.com`)
  await page.getByLabel('Adgangskode').fill('en lang og sikker adgangskode')
  await page.getByRole('button', { name: 'Opret konto' }).click()
}

/** Opens the start page for the seeded demo template. */
export async function openDemoTemplate(teacher: Page) {
  const templates = teacher.getByRole('list', { name: 'Skabeloner' })
  await templates.getByRole('link', { name: /Start session: Demo: Flugten fra matematiklokalet/ }).click()
}

/** Reads the PIN from the teacher's session page. */
export async function readPin(teacher: Page): Promise<string> {
  const pinElement = teacher.getByLabel('PIN-kode')
  await expect(pinElement).toHaveText(/^\d{6}$/)
  return (await pinElement.textContent()) ?? ''
}

export async function joinWithPin(page: Page, pin: string) {
  await page.goto('/')
  await page.getByRole('link', { name: 'Deltag med PIN-kode' }).click()
  await page.getByLabel('PIN-kode').fill(pin)
  await page.getByRole('button', { name: 'Videre' }).click()
}

/** Joins a team-mode session on the given team ("Hold Rød") with a nickname. */
export async function joinTeam(page: Page, pin: string, team: string, nickname: string) {
  await joinWithPin(page, pin)
  await page.getByRole('button', { name: team }).click()
  await page.getByLabel('Kaldenavn (frivilligt)').fill(nickname)
  await page.getByRole('button', { name: 'Deltag' }).click()
  await expect(page.getByRole('heading', { name: `Du er på ${team}` })).toBeVisible()
}
