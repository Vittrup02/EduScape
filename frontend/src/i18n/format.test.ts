import { describe, expect, it } from 'vitest'
import { formatDateTime, teamName } from './format'
import i18n from './i18n'

describe('format', () => {
  it('names teams after their translated colour', async () => {
    expect(teamName(i18n.t, 'Blue')).toBe('Hold Blå')

    await i18n.changeLanguage('en')
    expect(teamName(i18n.t, 'Blue')).toBe('Team Blue')
  })

  it('formats date and time for the language', () => {
    const iso = '2026-10-20T04:57:00Z'
    const expected = (language: string) =>
      new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))

    expect(formatDateTime(iso, 'da')).toBe(expected('da'))
    expect(formatDateTime(iso, 'en')).toBe(expected('en'))
    expect(formatDateTime(iso, 'da')).not.toBe(formatDateTime(iso, 'en'))
  })
})
