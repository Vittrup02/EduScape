import { expect, test } from '@playwright/test'
import { joinTeam, newPage, openDemoTemplate, readPin, registerTeacher } from './helpers.ts'

test.use({ locale: 'da-DK' })

const firstLock = 'Lås: Hvad giver regnestykket på tavlen?'
const firstLockOpen = 'Åben lås: Hvad giver regnestykket på tavlen?'
const secondLock = /^Lås: Hvad er 10 \/ 3\?/

test('two students on the same team: one solves a lock and the other sees it open', async ({ page: teacher, browser }) => {
  await registerTeacher(teacher)
  await openDemoTemplate(teacher)
  await teacher.getByLabel('Klasse').fill('9.B')
  await teacher.getByRole('button', { name: 'Start session' }).click()
  const pin = await readPin(teacher)

  const ida = await newPage(browser)
  const bo = await newPage(browser)
  await joinTeam(ida, pin, 'Hold Rød', 'Ida')
  await joinTeam(bo, pin, 'Hold Rød', 'Bo')

  // Both start in the classroom; the second lock waits for the first.
  for (const student of [ida, bo]) {
    await expect(student.getByRole('button', { name: firstLock })).toBeVisible()
    await expect(student.getByRole('button', { name: secondLock })).toHaveCount(0)
  }

  // Ida answers wrong first, then right. The answer is checked on the server.
  await ida.getByRole('button', { name: firstLock }).click()
  const dialog = ida.getByRole('dialog', { name: 'Lås' })
  await dialog.getByLabel('Dit svar').fill('18')
  await dialog.getByRole('button', { name: 'Prøv' }).click()
  await expect(dialog.getByRole('alert')).toHaveText('Forkert svar. Prøv igen.')
  await dialog.getByLabel('Dit svar').fill('17')
  await dialog.getByRole('button', { name: 'Prøv' }).click()
  await expect(ida.getByRole('button', { name: firstLockOpen })).toBeVisible()

  // Bo sees the lock open and the next lock appear, pushed by the server without a reload.
  await expect(bo.getByRole('button', { name: firstLockOpen })).toBeVisible()
  await expect(bo.getByRole('button', { name: secondLock })).toBeVisible()

  // After a reload Bo gets the team's whole state back.
  await bo.reload()
  await expect(bo.getByRole('button', { name: firstLockOpen })).toBeVisible()
  await expect(bo.getByRole('button', { name: secondLock })).toBeVisible()

  // Bo solves the next lock with a rounded decimal answer, and Ida sees it.
  await bo.getByRole('button', { name: secondLock }).click()
  await bo.getByLabel('Dit svar').fill('3.33')
  await bo.getByRole('button', { name: 'Prøv' }).click()
  await expect(ida.getByRole('button', { name: /^Åben lås: Hvad er 10 \/ 3\?/ })).toBeVisible()
  await expect(ida.getByRole('button', { name: 'Dør til Biblioteket' })).toBeVisible()
})
