import { describe, expect, it } from 'vitest'
import i18n, { chooseLanguage, detectLanguage } from './i18n'

describe('detectLanguage', () => {
  it('prefers the saved choice over the browser', () => {
    expect(detectLanguage('en', ['da-DK'])).toBe('en')
  })

  it('uses the first supported browser language, ignoring the region', () => {
    expect(detectLanguage(null, ['de-DE', 'en-GB', 'da'])).toBe('en')
  })

  it('ignores a saved value that is no longer supported', () => {
    expect(detectLanguage('xx', ['en-US'])).toBe('en')
  })

  it('falls back to Danish', () => {
    expect(detectLanguage(null, ['de-DE', 'sv'])).toBe('da')
    expect(detectLanguage(null, [])).toBe('da')
  })
})

describe('chooseLanguage', () => {
  it('switches language, updates <html lang> and remembers the choice', async () => {
    await chooseLanguage('en')

    expect(i18n.resolvedLanguage).toBe('en')
    expect(document.documentElement.lang).toBe('en')
    expect(detectLanguage(localStorage.getItem('eduscape.language'), ['da-DK'])).toBe('en')
  })
})
