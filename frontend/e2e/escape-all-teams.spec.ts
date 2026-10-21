import { expect, test, type Page } from '@playwright/test'
import { joinTeam, newPage, openDemoTemplate, readPin, registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

const teams = ['Hold Rød', 'Hold Blå', 'Hold Grøn'] as const

/** Opens a lock in the scene and answers it right. */
async function solve(student: Page, lock: RegExp, answer: string, choice = false) {
  await student.getByRole('button', { name: lock }).click()
  const dialog = student.getByRole('dialog', { name: 'Lås' })
  if (choice) {
    await dialog.getByRole('button', { name: answer, exact: true }).click()
  } else {
    await dialog.getByLabel('Dit svar').fill(answer)
    await dialog.getByRole('button', { name: 'Prøv' }).click()
  }
  await expect(dialog).toHaveCount(0)
}

/** Plays the demo room to the end: both locks in the classroom, the door, the library's lock and the exit door. */
async function escape(student: Page) {
  await solve(student, /^Lås: Hvad giver regnestykket på tavlen\?/, '17')
  await solve(student, /^Lås: Hvad er 10 \/ 3\?/, '3,33')
  await student.getByRole('button', { name: 'Åben dør til Biblioteket' }).click()
  await solve(student, /^Lås: Hvilket af tallene er et primtal\?/, '17', true)
  await student.getByRole('button', { name: 'Åben slutdør' }).click()
}

test('every team gets its own escape screen when it walks out, whether or not the others are done', async ({
  page: teacher,
  browser,
}) => {
  test.slow()

  await registerTeacher(teacher)
  await openDemoTemplate(teacher)
  await teacher.getByLabel('Klasse').fill('8.A')
  await teacher.getByRole('button', { name: 'Færre hold' }).click()
  await expect(teacher.getByRole('group', { name: 'Antal hold' })).toContainText('3')
  await teacher.getByRole('button', { name: 'Start session' }).click()
  const pin = await readPin(teacher)

  // One student on each of the three teams.
  const students: Page[] = []
  for (const team of teams) {
    const student = await newPage(browser)
    await joinTeam(student, pin, team, `Elev på ${team}`)
    students.push(student)
  }
  const [red, blue, green] = students

  // Red escapes first. Only red sees the escape screen; the others play on, and see nothing of red's result.
  await escape(red)
  await expect(red.getByRole('heading', { name: 'I slap ud!' })).toBeVisible()
  for (const other of [blue, green]) {
    await expect(other.getByRole('heading', { name: 'I slap ud!' })).toHaveCount(0)
    await expect(other.getByRole('region', { name: 'Klasselokalet' })).toBeVisible()
    await expect(other.getByRole('button', { name: /^Lås: Hvad giver regnestykket/ })).toBeVisible()
  }

  // Blue and green finish at the same time, and each gets its own escape screen too.
  await Promise.all([escape(blue), escape(green)])
  for (const student of students) {
    await expect(student.getByRole('heading', { name: 'I slap ud!' })).toBeVisible()
  }

  // Only the teacher sees how every team did.
  for (const team of teams) {
    const card = teacher.getByRole('listitem').filter({ has: teacher.getByRole('heading', { name: team, exact: true }) })
    await expect(card.getByText(/^Brudt ud efter/)).toBeVisible()
  }
})
