import { useTranslation } from 'react-i18next'
import { chooseLanguage } from './i18n'
import { languageCodes } from './languages'

/** DA/EN buttons. Each language labels itself, so a new language needs no changes here. */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation()

  return (
    <div className="language-switcher" role="group" aria-label={t('language.switcherLabel')}>
      {languageCodes.map((code) => {
        const own = i18n.getFixedT(code)
        return (
          <button
            key={code}
            type="button"
            lang={code}
            title={own('language.name')}
            aria-pressed={i18n.resolvedLanguage === code}
            onClick={() => void chooseLanguage(code)}
          >
            {own('language.short')}
          </button>
        )
      })}
    </div>
  )
}
