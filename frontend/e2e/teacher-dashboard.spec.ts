import { expect, test, type Page } from '@playwright/test'
import { joinTeam, newPage, openDemoTemplate, readPin, registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

const firstLock = 'Lås: Hvad giver regnestykket på tavlen?'
const secondLock = /^Lås: Hvad er 10 \/ 3\?/

/** A team's card on the teacher's dashboard. */
function teamCard(teacher: Page, team: string) {
  return teacher.getByRole('listitem').filter({ has: teacher.getByRole('heading', { name: team, exact: true }) })
}

async function answer(student: Page, value: string) {
  const dialog = student.getByRole('dialog', { name: 'Lås' })
  await dialog.getByLabel('Dit svar').fill(value)
  await dialog.getByRole('button', { name: 'Prøv' }).click()
}

/** Answers wrong and waits for the server's verdict, which empties the answer field. */
async function answerWrong(student: Page, value: string) {
  await answer(student, value)
  await expect(student.getByRole('dialog', { name: 'Lås' }).getByRole('alert')).toHaveText('Forkert svar. Prøv igen.')
}

test("the teacher's dashboard follows a student's progress live", async ({ page: teacher, browser }) => {
  await registerTeacher(teacher)
  await openDemoTemplate(teacher)
  await teacher.getByLabel('Klasse').fill('9.C')
  await teacher.getByRole('button', { name: 'Start session' }).click()
  const pin = await readPin(teacher)
  await expect(teacher.getByText(/Forbinder til live-overblikket/)).toHaveCount(0)

  const red = teamCard(teacher, 'Hold Rød')
  const blue = teamCard(teacher, 'Hold Blå')
  await expect(red).toContainText('0 af 3 låse løst')
  await expect(red).toContainText('Ingen elever endnu.')

  // Ida joins: she appears on the dashboard in the start scene, without the teacher reloading.
  const ida = await newPage(browser)
  await joinTeam(ida, pin, 'Hold Rød', 'Ida')
  await expect(red.getByText('Ida', { exact: true })).toBeVisible()
  await expect(red.getByText('i Klasselokalet')).toBeVisible()

  // Wrong answers show which lock the team is working on.
  await ida.getByRole('button', { name: firstLock }).click()
  await answerWrong(ida, '18')
  await answerWrong(ida, '19')
  await expect(red).toContainText('Arbejder på „Hvad giver regnestykket på tavlen?“ · 2 forkerte svar')

  // Solving locks and going through the door shows up live too.
  await answer(ida, '17')
  await expect(red).toContainText('1 af 3 låse løst')
  await ida.getByRole('button', { name: secondLock }).click()
  await answer(ida, '3,33')
  await expect(red).toContainText('2 af 3 låse løst')
  await ida.getByRole('button', { name: 'Dør til Biblioteket' }).click()
  await expect(ida.getByRole('region', { name: 'Biblioteket' })).toBeVisible()
  await expect(red.getByText('i Biblioteket')).toBeVisible()

  // The teacher moves Ida to the blue team; her device follows, back in the start scene of the new team.
  await red.getByRole('combobox', { name: 'Flyt Ida til et andet hold' }).selectOption({ label: 'Hold Blå' })
  await expect(ida.getByRole('heading', { name: 'Du er på Hold Blå' })).toBeVisible()
  await expect(ida.getByRole('region', { name: 'Klasselokalet' })).toBeVisible()
  await expect(blue.getByText('Ida', { exact: true })).toBeVisible()
  await expect(blue.getByText('i Klasselokalet')).toBeVisible()
  await expect(red).toContainText('Ingen elever endnu.')
  await expect(red).toContainText('2 af 3 låse løst')
})
