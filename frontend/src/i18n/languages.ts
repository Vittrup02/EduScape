import da from '../locales/da.json'
import en from '../locales/en.json'

export type Translation = typeof da

/**
 * The single place where UI languages are registered. To add a language, create
 * /src/locales/<code>.json with the same keys as da.json and add it here.
 * The language switcher and detection pick it up automatically.
 */
export const languages = { da, en } satisfies Record<string, Translation>

export type Language = keyof typeof languages

export const defaultLanguage: Language = 'da'

export const languageCodes = Object.keys(languages) as Language[]

export function isLanguage(code: string): code is Language {
  return Object.hasOwn(languages, code)
}
