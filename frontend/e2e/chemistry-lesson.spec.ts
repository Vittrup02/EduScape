import { devices, expect, test, type Locator, type Page } from '@playwright/test'
import { dragTo, joinTeam, newPage, readPin, registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

const elementQuestion = 'Hvilket grundstof har symbolet Na?'
const orderQuestion = 'Sæt trinnene i rækkefølge'
const correctOrder = ['Afvej', 'Opløs', 'Filtrér']

/**
 * Drags with a finger, through the browser's real touch input, from the middle of `from` to a point on `to` given in
 * percent of its height. A list that let the touch scroll the page instead would not move.
 */
async function touchDrag(page: Page, from: Locator, to: Locator, yPercent: number) {
  const source = await from.boundingBox()
  const target = await to.boundingBox()
  if (source === null || target === null) {
    throw new Error('Nothing to drag from or to')
  }
  const start = { x: source.x + source.width / 2, y: source.y + source.height / 2 }
  const end = { x: target.x + target.width / 2, y: target.y + (target.height * yPercent) / 100 }
  const client = await page.context().newCDPSession(page)
  await client.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [start] })
  for (let step = 1; step <= 12; step++) {
    const point = { x: start.x + ((end.x - start.x) * step) / 12, y: start.y + ((end.y - start.y) * step) / 12 }
    await client.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [point] })
  }
  await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  await client.detach()
}

/** Puts the student's cards in the correct order by dragging them with a finger, from the top down. */
async function sortWithFinger(student: Page, cards: Locator, harmless: Locator) {
  for (let position = 0; position < correctOrder.length; position++) {
    const texts = await cards.locator('.order-text').allTextContents()
    const from = texts.indexOf(correctOrder[position])
    if (from !== position) {
      await touchDrag(student, cards.nth(from), cards.nth(position), 15)
      await expect(cards.nth(position).locator('.order-text')).toHaveText(correctOrder[position])
    }
  }
  // Chrome's emulated touch input drops the click of the first tap after a touch drag (also on a bare page without
  // eduscape), which a real finger doesn't. A tap on something harmless takes it.
  await harmless.tap()
}

/** The opening animation the exit door would play on this device. A string: E2E code has no DOM types. */
function exitDoorAnimation(student: Page): Promise<string> {
  return student.evaluate<string>(`(() => {
    const graphic = document.querySelector('[aria-label="Åben slutdør"] .object-graphic')
    graphic.setAttribute('data-opening', '')
    return getComputedStyle(graphic).animationName
  })()`)
}

test("a chemistry teacher's lock with two steps, one of them an order puzzle, played by two students", async ({
  page: teacher,
  browser,
}) => {
  test.slow()

  // The teacher builds the room: a chest whose puzzle has two steps, and the exit door.
  await registerTeacher(teacher)
  await teacher.getByRole('link', { name: 'Nyt rum' }).click()
  await teacher.getByLabel('Titel').fill('Reaktioner')
  await teacher.getByLabel('Fag').selectOption({ label: 'Kemi' })
  await teacher.getByRole('button', { name: 'Opret rummet' }).click()
  const stage = teacher.getByRole('region', { name: 'Scenen Scene 1' })
  const panel = teacher.getByRole('complementary')

  await dragTo(teacher, teacher.getByRole('button', { name: 'Tilføj Lås' }), stage, 30, 55)
  await panel.getByRole('radio', { name: 'Kiste' }).check()
  await panel.getByRole('button', { name: /^Flere trin/ }).click()

  // Step 1: a word, where a small spelling mistake is fine.
  await panel.getByLabel('Spørgsmål').fill(elementQuestion)
  await panel.getByLabel('Det rigtige svar').fill('Natrium')
  await panel.getByRole('checkbox', { name: 'Accepter små stavefejl' }).check()

  // Step 2: the steps of the experiment in order. The teacher types them as they come to mind and drags them into place.
  await panel.getByRole('button', { name: /^Trin 2/ }).click()
  await panel.getByRole('radio', { name: /^Træk i rækkefølge/ }).check()
  await panel.getByLabel('Spørgsmål').fill(orderQuestion)
  const items = panel.getByRole('list', { name: 'Elementer i den rigtige rækkefølge' })
  await items.getByLabel('Element 1', { exact: true }).fill('Opløs')
  await items.getByLabel('Element 2', { exact: true }).fill('Afvej')
  await items.getByLabel('Element 3', { exact: true }).fill('Filtrér')
  await dragTo(teacher, items.getByRole('button', { name: 'Flyt Afvej' }), items.getByRole('button', { name: 'Flyt Opløs' }), 50, 10)
  await expect(items.getByLabel('Element 1', { exact: true })).toHaveValue('Afvej')
  await expect(items.getByLabel('Element 2', { exact: true })).toHaveValue('Opløs')

  // A short wait after a wrong answer, so the class can't just guess.
  await panel.getByLabel('Ventetid efter forkert svar').selectOption({ label: '5 sekunder' })
  await panel.getByRole('button', { name: 'Færdig' }).click()

  await dragTo(teacher, teacher.getByRole('button', { name: 'Tilføj Slutdør' }), stage, 80, 55)
  await panel.getByRole('button', { name: 'Færdig' }).click()
  await expect(teacher.getByText('Rummet er klar til at blive prøvet.')).toBeVisible()

  await teacher.getByRole('button', { name: 'Start session' }).click()
  await expect(teacher.getByRole('heading', { name: 'Start en session' })).toBeVisible()
  await teacher.getByLabel('Klasse').fill('8.B')
  await teacher.getByRole('button', { name: 'Færre hold' }).click()
  await teacher.getByRole('button', { name: 'Færre hold' }).click()
  await teacher.getByRole('button', { name: 'Start session' }).click()
  const pin = await readPin(teacher)

  // Ida at a computer that asks for reduced motion, Bo on a tablet.
  const ida = await newPage(browser, { reducedMotion: 'reduce' })
  const bo = await newPage(browser, devices['iPad (gen 7)'])
  await joinTeam(ida, pin, 'Hold Rød', 'Ida')
  await joinTeam(bo, pin, 'Hold Rød', 'Bo')

  // The exit door is there from the start, shut.
  await bo.getByRole('button', { name: 'Slutdør' }).tap()
  await expect(bo.getByRole('status')).toHaveText('Døren er lukket. Løs gåderne for at åbne den.')

  // Ida answers the first step with a small spelling mistake; the second step follows in the same pop-up.
  await ida.getByRole('button', { name: `Lås: ${elementQuestion}` }).click()
  const idaDialog = ida.getByRole('dialog', { name: 'Lås' })
  await expect(idaDialog).toContainText('Trin 1 af 2')
  await idaDialog.getByLabel('Dit svar').fill('natrum')
  await idaDialog.getByRole('button', { name: 'Prøv' }).click()
  await expect(idaDialog).toContainText('Trin 2 af 2')
  await expect(idaDialog).toContainText(orderQuestion)

  // Bo's device follows: the lock is on its second step there too.
  await bo.getByRole('button', { name: `Lås: ${orderQuestion}` }).tap()
  const boDialog = bo.getByRole('dialog', { name: 'Lås' })
  const cards = boDialog.getByRole('list', { name: 'Elementerne i jeres rækkefølge' }).getByRole('listitem')
  await expect(cards).toHaveCount(3)
  // Shuffled: never in the right order to begin with.
  expect(await cards.locator('.order-text').allTextContents()).not.toEqual(correctOrder)

  // Trying the shuffled order is wrong, and the lock waits five seconds for the whole team.
  await boDialog.getByRole('button', { name: 'Prøv' }).tap()
  await expect(boDialog.getByRole('alert')).toHaveText('Forkert svar. Prøv igen.')
  await expect(boDialog.getByRole('timer')).toContainText(/Låsen er låst i \d sekund/)
  await expect(idaDialog.getByRole('timer')).toContainText(/Låsen er låst i \d sekund/)
  await expect(idaDialog.getByRole('button', { name: 'Prøv' })).toBeDisabled()

  // Bo drags the cards into the right order with a finger, and tries again once the wait is over.
  await sortWithFinger(bo, cards, boDialog.getByText(orderQuestion))
  await expect(boDialog.getByRole('timer')).toHaveCount(0, { timeout: 10_000 })
  await boDialog.getByRole('button', { name: 'Prøv' }).tap()

  // The lock is open, and the exit door opens by itself on both devices, without moving anyone.
  await expect(bo.getByRole('button', { name: `Åben lås: ${orderQuestion}` })).toBeVisible()
  await expect(idaDialog).toContainText('Låsen er åbnet.')
  await idaDialog.getByRole('button', { name: 'Luk' }).click()
  for (const student of [ida, bo]) {
    await expect(student.getByRole('button', { name: 'Åben slutdør' })).toBeVisible()
  }
  // The door swings open, but only fades in where the student asked for reduced motion.
  expect(await exitDoorAnimation(bo)).toBe('asset-swing')
  expect(await exitDoorAnimation(ida)).toBe('asset-fade')

  // Ida walks out, and the team has escaped.
  await ida.getByRole('button', { name: 'Åben slutdør' }).click()
  for (const student of [ida, bo]) {
    await expect(student.getByRole('heading', { name: 'I slap ud!' })).toBeVisible()
  }
})
