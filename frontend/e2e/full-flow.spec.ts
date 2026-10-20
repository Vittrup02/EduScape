import { devices, expect, test, type Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { dragTo, expectNoSidewaysScroll, newPage, readPin } from './helpers.ts'

test.use({ locale: 'da-DK' })

const firstQuestion = 'Hvad er 1/2 + 1/4? Svar som decimaltal.'
const secondQuestion = 'Hvor mange cm er 2,5 m?'

/** Closes the object's panel in the editor once it is filled in. */
async function done(teacher: Page) {
  await teacher.getByRole('complementary').getByRole('button', { name: 'Færdig' }).click()
}

/** Taps a little below the middle of a scene object: on a phone the graphic is small, but the tap must still hit. */
async function tapNear(student: Page, name: string | RegExp) {
  const box = await student.getByRole('button', { name }).boundingBox()
  if (box === null) {
    throw new Error(`${String(name)} is not on the screen`)
  }
  await student.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2 + 20)
}

/** Answers the lock in its open dialog. */
async function answer(student: Page, value: string) {
  const dialog = student.getByRole('dialog', { name: 'Lås' })
  await dialog.getByLabel('Dit svar').fill(value)
  await dialog.getByRole('button', { name: 'Prøv' }).click()
}

test('a new teacher builds a room with two scenes, two students solve it together, and the teacher closes it', async ({
  page: teacher,
  browser,
}) => {
  test.slow()

  // A new teacher creates an account from the front page.
  await teacher.goto('/')
  await teacher.getByRole('link', { name: 'Opret en lærerkonto' }).click()
  await teacher.getByLabel('E-mail').fill(`e2e-${randomUUID()}@example.com`)
  await teacher.getByLabel('Adgangskode').fill('en lang og sikker adgangskode')
  await teacher.getByRole('button', { name: 'Opret konto' }).click()
  await expect(teacher.getByRole('heading', { name: 'Mine rum' })).toBeVisible()

  // A new room: only title, subject and grade level.
  await teacher.getByRole('link', { name: 'Nyt rum' }).click()
  await teacher.getByLabel('Titel').fill('Brøkjagten')
  await teacher.getByLabel('Fag').selectOption({ label: 'Matematik' })
  await teacher.getByLabel('Klassetrin').selectOption({ label: '7. klasse' })
  await teacher.getByRole('button', { name: 'Opret rummet' }).click()
  const classroom = teacher.getByRole('region', { name: 'Scenen Scene 1' })
  await expect(classroom).toBeVisible()
  // The editor says right away what an escape room needs.
  await expect(teacher.getByText(/Rummet har ingen slutdør/)).toBeVisible()

  // Scene 1: a padlock with a hint.
  await dragTo(teacher, teacher.getByRole('button', { name: 'Tilføj Hængelås' }), classroom, 30, 60)
  const panel = teacher.getByRole('complementary')
  await panel.getByLabel('Spørgsmål').fill(firstQuestion)
  await panel.getByLabel('Det rigtige svar').fill('0,75')
  await panel.getByRole('button', { name: 'Tilføj et hint' }).click()
  await panel.getByLabel('Hint').fill('Fire fjerdedele er en hel.')
  await done(teacher)

  // Scene 2, the laboratory, with a story, a code lock and the exit door.
  await teacher.getByRole('button', { name: 'Ny scene' }).click()
  await panel.getByLabel('Navn').fill('Laboratoriet')
  await panel.getByLabel('Fortællertekst').fill('Døren smækker i bag jer. Find vej ud!')
  const laboratory = teacher.getByRole('region', { name: 'Scenen Laboratoriet' })
  await dragTo(teacher, teacher.getByRole('button', { name: 'Tilføj Kodelås' }), laboratory, 30, 60)
  await panel.getByLabel('Spørgsmål').fill(secondQuestion)
  await panel.getByRole('radio', { name: /^Kode / }).check()
  await panel.getByLabel('Det rigtige svar').fill('250')
  await done(teacher)
  await dragTo(teacher, teacher.getByRole('button', { name: 'Tilføj Slutdør' }), laboratory, 80, 55)
  await done(teacher)
  // No door leads to the laboratory yet, so the students could never get out.
  await expect(teacher.getByText(/Eleverne kan aldrig nå hertil/).first()).toBeVisible()

  // Back in scene 1: a door to the laboratory that appears once the padlock is solved.
  await teacher.getByRole('navigation', { name: 'Scener' }).getByRole('button', { name: /^Scene 1/ }).click()
  await dragTo(teacher, teacher.getByRole('button', { name: 'Tilføj Dør' }), classroom, 80, 55)
  await expect(panel.getByLabel('Døren fører til')).toHaveValue(/.+/)
  await expect(panel.getByRole('heading', { name: 'Døren til Laboratoriet' })).toBeVisible()
  await panel.getByLabel('Hvornår kan eleverne se den?').selectOption({ label: `Vises når Låsen „${firstQuestion}“ er løst` })
  await done(teacher)
  await expect(teacher.getByText('Scene 1 fører til Laboratoriet')).toBeVisible()
  await expect(teacher.getByText('Rummet er klar til at blive prøvet.')).toBeVisible()

  // Straight from the editor to starting a session: class 7.A, two teams.
  await teacher.getByRole('button', { name: 'Start session' }).click()
  await expect(teacher.getByRole('heading', { name: 'Start en session' })).toBeVisible()
  await teacher.getByLabel('Klasse').fill('7.A')
  await teacher.getByRole('button', { name: 'Færre hold' }).click()
  await teacher.getByRole('button', { name: 'Færre hold' }).click()
  await expect(teacher.getByRole('group', { name: 'Antal hold' })).toContainText('2')
  await teacher.getByRole('button', { name: 'Start session' }).click()
  const pin = await readPin(teacher)

  // Two students join the same team: Ida on a tablet, Bo on a phone.
  const ida = await newPage(browser, devices['iPad (gen 7)'])
  const bo = await newPage(browser, devices['iPhone 13'])
  for (const [student, nickname] of [
    [ida, 'Ida'],
    [bo, 'Bo'],
  ] as const) {
    await student.goto('/')
    await student.getByRole('link', { name: 'Deltag med PIN-kode' }).click()
    await student.getByLabel('PIN-kode').fill(`${pin.slice(0, 3)} ${pin.slice(3)}`)
    await student.getByRole('button', { name: 'Videre' }).click()
    await expectNoSidewaysScroll(student)
    await student.getByRole('button', { name: 'Hold Rød' }).click()
    await student.getByLabel('Kaldenavn (frivilligt)').fill(nickname)
    await student.getByRole('button', { name: 'Deltag' }).click()
    await expect(student.getByRole('heading', { name: 'Du er på Hold Rød' })).toBeVisible()
    await expect(student.getByRole('button', { name: `Lås: ${firstQuestion}` })).toBeVisible()
    // The door waits for the padlock.
    await expect(student.getByRole('button', { name: /^Dør til/ })).toHaveCount(0)
    await expectNoSidewaysScroll(student)
  }

  // The teacher sees both students on the team.
  const redTeam = teacher.getByRole('listitem').filter({ has: teacher.getByRole('heading', { name: 'Hold Rød' }) })
  await expect(redTeam.getByText('Ida', { exact: true })).toBeVisible()
  await expect(redTeam.getByText('Bo', { exact: true })).toBeVisible()

  // Ida answers wrong three times; the hint appears for the whole team.
  await ida.getByRole('button', { name: `Lås: ${firstQuestion}` }).click()
  for (const wrong of ['1', '0,5', '2']) {
    await answer(ida, wrong)
    await expect(ida.getByRole('dialog', { name: 'Lås' }).getByRole('alert')).toHaveText('Forkert svar. Prøv igen.')
  }
  await expect(ida.getByText('Fire fjerdedele er en hel.')).toBeVisible()
  await ida.getByRole('dialog', { name: 'Lås' }).getByRole('button', { name: 'Luk' }).click()

  // Bo opens the same lock on the phone, sees the hint and answers with a decimal point instead of a comma.
  await tapNear(bo, `Lås: ${firstQuestion}`)
  await expect(bo.getByRole('dialog', { name: 'Lås' })).toContainText('Fire fjerdedele er en hel.')
  await expectNoSidewaysScroll(bo)
  await answer(bo, '0.75')

  // Ida sees the lock open and the door appear without reloading; the teacher sees the progress.
  await expect(ida.getByRole('button', { name: `Åben lås: ${firstQuestion}` })).toBeVisible()
  await expect(redTeam.getByText('1 af 2 låse løst')).toBeVisible()

  // Ida goes through the door; the laboratory opens with the teacher's story.
  await ida.getByRole('button', { name: 'Dør til Laboratoriet' }).click()
  const story = ida.getByRole('region', { name: 'Fortælling' })
  await expect(story).toHaveText(/Døren smækker i bag jer/)
  await story.getByRole('button', { name: 'Videre' }).click()

  // The exit door stays shut while a lock is unsolved.
  await ida.getByRole('button', { name: 'Slutdør' }).click()
  await expect(ida.getByRole('alert')).toHaveText('Slutdøren er stadig låst. Løs alle låse i rummet først.')

  // Bo follows through the door the team has opened and solves the code lock.
  await bo.getByRole('button', { name: 'Åben dør til Laboratoriet' }).click()
  await bo.getByRole('region', { name: 'Fortælling' }).getByRole('button', { name: 'Videre' }).click()
  await tapNear(bo, `Lås: ${secondQuestion}`)
  await answer(bo, '250')
  await expect(ida.getByRole('button', { name: `Åben lås: ${secondQuestion}` })).toBeVisible()

  // Ida opens the exit door, and the whole team has escaped.
  await ida.getByRole('button', { name: 'Slutdør' }).click()
  for (const student of [ida, bo]) {
    await expect(student.getByRole('heading', { name: 'I er brudt ud!' })).toBeVisible()
    await expectNoSidewaysScroll(student)
  }
  await expect(redTeam.getByText('2 af 2 låse løst')).toBeVisible()
  await expect(redTeam.getByText(/^Brudt ud efter/)).toBeVisible()

  // The teacher closes the session; the students' devices hear of it at once.
  await teacher.getByRole('button', { name: 'Luk session' }).click()
  await teacher.getByRole('button', { name: 'Ja, luk sessionen' }).click()
  await expect(teacher.getByText('Sessionen er lukket. PIN-koden virker ikke længere.')).toBeVisible()
  for (const student of [ida, bo]) {
    await expect(student.getByRole('heading', { name: 'Sessionen er slut' })).toBeVisible()
  }
})
