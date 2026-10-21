import { expect, test, type Locator, type Page } from '@playwright/test'
import { dragTo, registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

/** The object's position on the scene, e.g. "30%", as the editor stores it. */
async function leftOf(object: Locator): Promise<string> {
  const style = (await object.getAttribute('style')) ?? ''
  return /left:\s*([^;]+)/.exec(style)?.[1] ?? ''
}

/** Opens "Prøv rummet" (or another preview button) and returns the student view in its new tab. */
async function openPreview(page: Page, button: string): Promise<Page> {
  const [play] = await Promise.all([page.context().waitForEvent('page'), page.getByRole('button', { name: button }).click()])
  await expect(play.getByText(/^Prøveversion/)).toBeVisible()
  return play
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

  // A lock dragged onto the picture opens its panel straight away, asking for the kind of puzzle.
  await dragTo(page, page.getByRole('button', { name: 'Tilføj Lås' }), stage, 30, 50)
  const panel = page.getByRole('complementary')
  await expect(panel.getByRole('heading', { name: 'Lås 1' })).toBeVisible()
  await expect(stage.getByRole('button')).toHaveCount(1)
  await panel.getByRole('button', { name: /^Skriv svaret/ }).click()
  await panel.getByLabel('Spørgsmål').fill('Hvad er 3 · 4?')
  await panel.getByRole('checkbox', { name: 'Præcis kode' }).check()
  await panel.getByLabel('Det rigtige svar').fill('12')
  await panel.getByRole('button', { name: 'Færdig' }).click()

  // A scene removed with its cross comes back with undo.
  await page.getByRole('button', { name: 'Ny scene' }).click()
  await page.getByRole('button', { name: 'Slet Scene 2' }).click()
  await expect(page.getByText('Scene 2 er slettet.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Slet Scene 1' })).toHaveCount(0)
  await page.getByRole('status').filter({ hasText: 'Scene 2 er slettet.' }).getByRole('button', { name: 'Fortryd' }).click()

  // A second scene, then back to the first for the door.
  await page.getByRole('navigation', { name: 'Scener' }).getByRole('button', { name: /^Scene 2/ }).click()
  await expect(page.getByRole('region', { name: 'Scenen Scene 2' })).toBeVisible()
  await page.getByLabel('Fortællertekst').fill('Døren smækker bag jer. Nu er I alene i mørket.')
  // Without an exit door the students could never break out, so the editor asks for one.
  await expect(page.getByText(/Rummet har ingen slutdør/)).toBeVisible()
  await dragTo(page, page.getByRole('button', { name: 'Tilføj Slutdør' }), page.getByRole('region', { name: 'Scenen Scene 2' }), 80, 55)
  await expect(panel.getByRole('heading', { name: 'Slutdøren' })).toBeVisible()
  await page.getByRole('navigation', { name: 'Scener' }).getByRole('button', { name: /^Scene 1/ }).click()

  // A new door leads to the other scene, and opens once the scene's lock is solved.
  await dragTo(page, page.getByRole('button', { name: 'Tilføj Dør' }), stage, 80, 55)
  await expect(panel.getByRole('heading', { name: 'Døren til Scene 2' })).toBeVisible()
  await expect(panel.getByLabel('Døren fører til')).toHaveValue(/.+/)
  await expect(panel.getByRole('radio', { name: 'Når alle gåder i scenen er løst' })).toBeChecked()
  await expect(page.getByText('Scene 1 fører til Scene 2')).toBeVisible()

  // A text box with a piece of the story, written straight onto the picture.
  await dragTo(page, page.getByRole('button', { name: 'Tilføj Tekstfelt' }), stage, 50, 15)
  await panel.getByLabel('Tekst', { exact: true }).fill('Klokken er kvart over otte. Læreren er sent på den.')
  await expect(stage.getByRole('button', { name: /^Tekstfeltet „Klokken er kvart over otte/ })).toBeVisible()

  // Moving the door can be undone and redone.
  const door = stage.getByRole('button', { name: 'Døren til Scene 2' })
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
  const play = await openPreview(page, 'Prøv rummet')
  await expect(play.getByText('Klokken er kvart over otte. Læreren er sent på den.')).toBeVisible()
  // The door is there, but shut until the lock is solved.
  await play.getByRole('button', { name: 'Dør til Scene 2' }).click()
  await expect(play.getByRole('status')).toHaveText('Døren er lukket. Løs gåderne for at åbne den.')
  await play.getByRole('button', { name: 'Lås: Hvad er 3 · 4?' }).click()
  const dialog = play.getByRole('dialog', { name: 'Lås' })
  await dialog.getByLabel('Dit svar').fill('12')
  await dialog.getByRole('button', { name: 'Prøv' }).click()
  await expect(play.getByRole('button', { name: 'Åben lås: Hvad er 3 · 4?' })).toBeVisible()
  // The door opens by itself, but the student walks through.
  await expect(play.getByRole('region', { name: 'Scene 1' })).toBeVisible()
  await play.getByRole('button', { name: 'Åben dør til Scene 2' }).click()
  await expect(play.getByRole('region', { name: 'Scene 2' })).toBeVisible()

  // Scene 2 opens with the teacher's story.
  const story = play.getByRole('region', { name: 'Fortælling' })
  await expect(story).toHaveText(/Døren smækker bag jer/)
  await story.getByRole('button', { name: 'Videre' }).click()
  await expect(story).toHaveCount(0)
  await play.close()

  // Working on scene 2, the teacher tries it straight from there: the lock before it counts as solved.
  await page.getByRole('navigation', { name: 'Scener' }).getByRole('button', { name: /^Scene 2/ }).click()
  await expect(page.getByText('Starter i Scene 2. Låsene i scenerne før er løst.')).toBeVisible()
  const fromScene = await openPreview(page, 'Prøv rummet')
  await expect(fromScene.getByRole('region', { name: 'Scene 2' })).toBeVisible()
  await fromScene.getByRole('navigation', { name: 'Scener' }).getByRole('button', { name: 'Scene 1' }).click()
  await expect(fromScene.getByRole('button', { name: 'Åben lås: Hvad er 3 · 4?' })).toBeVisible()
  await fromScene.close()

  // "Start forfra" plays the whole room.
  const fromStart = await openPreview(page, 'Start forfra')
  await expect(fromStart.getByRole('region', { name: 'Scene 1' })).toBeVisible()
  await expect(fromStart.getByRole('button', { name: 'Lås: Hvad er 3 · 4?' })).toBeVisible()
})

test('a new room and its scenes get readable names, and scenes are sorted by dragging', async ({ page }) => {
  await registerTeacher(page)
  await page.getByRole('link', { name: 'Nyt rum' }).click()
  // No title needed to get started.
  await page.getByRole('button', { name: 'Opret rummet' }).click()
  await expect(page.getByLabel('Titel')).toHaveValue('Unavngivet rum')

  const strip = page.getByRole('navigation', { name: 'Scener' })
  await strip.getByRole('button', { name: 'Ny scene' }).click()
  await strip.getByRole('button', { name: 'Ny scene' }).click()
  await expect(page.getByLabel('Navn')).toHaveValue('Scene 3')

  // Drag "Scene 3" in front of "Scene 1" with the mouse.
  const sceneNames = () => strip.locator('.scene-tab-name').allTextContents()
  await dragTo(page, strip.getByRole('button', { name: /^Scene 3/ }), strip.getByRole('button', { name: /^Scene 1/ }), 10, 50)
  await expect.poll(sceneNames).toEqual(['Scene 3', 'Scene 1', 'Scene 2'])

  // The order is saved.
  await expect(page.getByRole('status').filter({ hasText: 'Gemt' })).toBeVisible()
  await page.reload()
  await expect.poll(sceneNames).toEqual(['Scene 3', 'Scene 1', 'Scene 2'])
})
