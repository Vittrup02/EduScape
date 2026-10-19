import { expect, test, type Locator, type Page } from '@playwright/test'
import { registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

/** Drags with the mouse from the middle of `from` to a point on `to`, given in percent of its size. */
async function dragTo(page: Page, from: Locator, to: Locator, xPercent: number, yPercent: number) {
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

/** The object's position on the scene, e.g. "30%", as the editor stores it. */
async function leftOf(object: Locator): Promise<string> {
  const style = (await object.getAttribute('style')) ?? ''
  return /left:\s*([^;]+)/.exec(style)?.[1] ?? ''
}

test('a teacher builds a room with a lock and a door, and tries it', async ({ page }) => {
  await registerTeacher(page)
  await page.getByRole('link', { name: 'Nyt rum' }).click()
  await page.getByLabel('Titel').fill('Brøker på flugt')
  await page.getByLabel('Fag').selectOption({ label: 'Matematik' })
  await page.getByLabel('Klassetrin').selectOption({ label: '6. klasse' })
  await page.getByRole('button', { name: 'Opret rummet' }).click()

  const stage = page.getByRole('region', { name: 'Scenen Scene 1' })
  await expect(stage).toBeVisible()

  // A padlock dragged onto the picture opens its panel straight away.
  await dragTo(page, page.getByRole('button', { name: 'Tilføj Hængelås' }), stage, 30, 50)
  const panel = page.getByRole('complementary')
  await expect(panel.getByRole('heading', { name: 'Lås 1' })).toBeVisible()
  await panel.getByLabel('Spørgsmål').fill('Hvad er 3 · 4?')
  await panel.getByRole('radio', { name: /^Kode Skal passe/ }).check()
  await panel.getByLabel('Det rigtige svar').fill('12')
  await panel.getByRole('button', { name: 'Færdig' }).click()

  // A second scene, then back to the first for the door.
  await page.getByRole('button', { name: 'Ny scene' }).click()
  await expect(page.getByRole('region', { name: 'Scenen Scene 2' })).toBeVisible()
  await page.getByRole('navigation', { name: 'Scener' }).getByRole('button', { name: /Scene 1/ }).click()

  // A new door leads to the other scene; it should appear once the lock is solved.
  await dragTo(page, page.getByRole('button', { name: 'Tilføj Dør' }), stage, 80, 55)
  await expect(panel.getByRole('heading', { name: 'Døren til Scene 2' })).toBeVisible()
  await expect(panel.getByLabel('Døren fører til')).toHaveValue(/.+/)
  await panel.getByLabel('Hvornår kan eleverne se den?').selectOption({ label: 'Vises når Låsen „Hvad er 3 · 4?“ er løst' })
  await expect(page.getByText('Scene 1 fører til Scene 2')).toBeVisible()

  // Moving the door can be undone and redone.
  const door = stage.getByRole('button', { name: 'Døren til Scene 2 (vises senere)' })
  const before = await leftOf(door)
  await dragTo(page, door, stage, 60, 30)
  const after = await leftOf(door)
  expect(after).not.toBe(before)
  await page.getByRole('button', { name: 'Fortryd' }).click()
  await expect.poll(() => leftOf(door)).toBe(before)
  await page.getByRole('button', { name: 'Gentag' }).click()
  await expect.poll(() => leftOf(door)).toBe(after)

  // Everything is saved by itself and is still there after a reload.
  await expect(page.getByRole('status').filter({ hasText: 'Gemt' })).toBeVisible()
  await expect(page.getByText('Rummet er klar til at blive prøvet.')).toBeVisible()
  await page.reload()
  await expect(stage.getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' })).toBeVisible()
  await expect(door).toBeVisible()
  expect(await leftOf(door)).toBe(after)

  // "Prøv rummet" opens the student view in a new tab, played on the server like a real session.
  const [play] = await Promise.all([
    page.context().waitForEvent('page'),
    page.getByRole('button', { name: 'Prøv rummet' }).click(),
  ])
  await expect(play.getByText(/^Prøveversion/)).toBeVisible()
  await expect(play.getByRole('button', { name: /^Dør til/ })).toHaveCount(0)
  await play.getByRole('button', { name: 'Lås: Hvad er 3 · 4?' }).click()
  const dialog = play.getByRole('dialog', { name: 'Lås' })
  await dialog.getByLabel('Dit svar').fill('12')
  await dialog.getByRole('button', { name: 'Prøv' }).click()
  await expect(play.getByRole('button', { name: 'Åben lås: Hvad er 3 · 4?' })).toBeVisible()
  await play.getByRole('button', { name: 'Dør til Scene 2' }).click()
  await expect(play.getByRole('region', { name: 'Scene 2' })).toBeVisible()
})
