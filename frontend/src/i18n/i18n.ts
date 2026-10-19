import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import { defaultLanguage, isLanguage, languageCodes, languages, type Language } from './languages'

const storageKey = 'eduscape.language'

/**
 * Picks the UI language: the user's saved choice first, then the browser's preferred
 * languages (matching on the primary subtag, so "en-GB" gives "en"), then Danish.
 */
export function detectLanguage(saved: string | null, browserLanguages: readonly string[]): Language {
  for (const candidate of [saved, ...browserLanguages]) {
    const code = candidate?.toLowerCase().split('-')[0]
    if (code && isLanguage(code)) {
      return code
    }
  }
  return defaultLanguage
}

/** Switches language and remembers it, so the user's choice wins over the browser next time. */
export async function chooseLanguage(language: Language): Promise<void> {
  try {
    localStorage.setItem(storageKey, language)
  } catch {
    // Storage can be blocked (e.g. private browsing); the choice then lasts for this visit only.
  }
  await i18n.changeLanguage(language)
}

function readSavedLanguage(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

// Keeps screen readers and hyphenation in step with the visible language.
i18n.on('languageChanged', (language) => {
  document.documentElement.lang = language
})

void i18n.use(initReactI18next).init({
  resources: Object.fromEntries(
    Object.entries(languages).map(([code, translation]) => [code, { translation }]),
  ),
  lng: detectLanguage(readSavedLanguage(), navigator.languages),
  fallbackLng: defaultLanguage,
  supportedLngs: languageCodes,
  // Resources are bundled, so initialise synchronously and render translated text on first paint.
  initAsync: false,
  interpolation: {
    // React already escapes rendered values.
    escapeValue: false,
  },
})

export default i18n
