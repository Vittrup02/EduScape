import { describe, expect, it } from 'vitest'
import { errorKey } from './errors'
import i18n from './i18n'

describe('errorKey', () => {
  it('maps a backend error code to its translation', async () => {
    expect(errorKey('SESSION_CLOSED')).toBe('errors.SESSION_CLOSED')
    expect(i18n.t(errorKey('SESSION_CLOSED'))).toBe('Sessionen er lukket.')

    await i18n.changeLanguage('en')
    expect(i18n.t(errorKey('SESSION_CLOSED'))).toBe('The session is closed.')
  })

  it.each(['SOMETHING_NEW', 'toString', '__proto__', ''])(
    'falls back to a generic message for unknown code %j',
    (code) => {
      expect(errorKey(code)).toBe('errors.UNKNOWN')
    },
  )
})
