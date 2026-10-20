import { expect, test } from '@playwright/test'
import { joinWithPin, newPage, openDemoTemplate, readPin, registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

test('teacher starts a session, a student joins with the PIN and stays on the team', async ({ page: teacher, browser }) => {
  await registerTeacher(teacher)

  // Start the seeded demo template in team mode with 3 teams.
  await openDemoTemplate(teacher)
  await teacher.getByLabel('Klasse').fill('9.A')
  await teacher.getByRole('button', { name: 'Færre hold' }).click()
  await expect(teacher.getByRole('group', { name: 'Antal hold' })).toContainText('3')
  await teacher.getByRole('button', { name: 'Start session' }).click()

  const pin = await readPin(teacher)
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

  // The teacher's live dashboard shows the student on the team.
  const blueTeam = teacher.getByRole('listitem').filter({ has: teacher.getByRole('heading', { name: 'Hold Blå' }) })
  await expect(blueTeam.getByText('Ida', { exact: true })).toBeVisible()

  // Closing the session (after confirming) ends it for the student and the PIN stops working.
  await teacher.getByRole('button', { name: 'Luk session' }).click()
  await teacher.getByRole('button', { name: 'Ja, luk sessionen' }).click()
  await expect(teacher.getByText('Sessionen er lukket. PIN-koden virker ikke længere.')).toBeVisible()

  // The student's device hears of it straight away, without a reload, and also after one.
  await expect(student.getByRole('heading', { name: 'Sessionen er slut' })).toBeVisible()
  await student.reload()
  await expect(student.getByRole('heading', { name: 'Deltag i et escape room' })).toBeVisible()

  const latecomer = await newPage(browser)
  await joinWithPin(latecomer, pin)
  await expect(latecomer.getByRole('alert')).toHaveText(
    'Der er ingen aktiv session med den PIN-kode. Tjek koden, og prøv igen.',
  )
})
