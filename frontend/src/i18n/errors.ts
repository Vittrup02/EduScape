import { defaultLanguage, languages, type Translation } from './languages'

export type ErrorCode = keyof Translation['errors']

/** Translation key for a backend error code; unknown codes fall back to a generic message. */
export function errorKey(code: string): `errors.${ErrorCode}` {
  return Object.hasOwn(languages[defaultLanguage].errors, code)
    ? `errors.${code as ErrorCode}`
    : 'errors.UNKNOWN'
}
