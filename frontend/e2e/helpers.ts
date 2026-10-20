import { randomUUID } from 'node:crypto'
import { expect, type Browser, type BrowserContextOptions, type Locator, type Page } from '@playwright/test'

/**
 * A separate context, so the teacher and each student have their own browser storage, like separate devices.
 * `device` is e.g. `devices['iPhone 13']`, to play on a phone or a tablet.
 */
export async function newPage(browser: Browser, device: BrowserContextOptions = {}): Promise<Page> {
  const context = await browser.newContext({ ...device, locale: 'da-DK' })
  return context.newPage()
}

/** Drags with the mouse from the middle of `from` to a point on `to`, given in percent of its size. */
export async function dragTo(page: Page, from: Locator, to: Locator, xPercent: number, yPercent: number) {
  const source = await from.boundingBox()
  const target = await to.boundingBox()
  if (source === null || target === null) {
    throw new Error('Nothing to drag from or to')
  }
  await page.mouse.move(source.x + source.width / 2, source.y + source.height / 2)
  await page.mouse.down()
  const x = target.x + (target.width * xPercent) / 100
  const y = target.y + (target.height * yPercent) / 100
  await page.mouse.move(x, y, { steps: 12 })
  await page.mouse.up()
}

/** Nothing on the page is wider than the screen, so it never scrolls sideways on a phone or tablet. */
export async function expectNoSidewaysScroll(page: Page) {
  // A string, because the E2E code is compiled without the browser's DOM types.
  const overflow = await page.evaluate<number>(
    'document.documentElement.scrollWidth - document.documentElement.clientWidth',
  )
  expect(overflow).toBeLessThanOrEqual(0)
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
