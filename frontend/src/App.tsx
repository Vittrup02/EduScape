import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { checkHealth } from './api/client'
import { LanguageSwitcher } from './i18n/LanguageSwitcher'

type BackendStatus = 'checking' | 'online' | 'offline'

function App() {
  const { t } = useTranslation()
  const [status, setStatus] = useState<BackendStatus>('checking')

  useEffect(() => {
    const controller = new AbortController()
    void checkHealth(controller.signal).then((ok) => {
      if (!controller.signal.aborted) {
        setStatus(ok ? 'online' : 'offline')
      }
    })
    return () => {
      controller.abort()
    }
  }, [])

  return (
    <>
      <header>
        <LanguageSwitcher />
      </header>
      <main>
        <h1>{t('app.name')}</h1>
        <p className="status" data-state={status} role="status">
          {t(`health.${status}`)}
        </p>
      </main>
    </>
  )
}

export default App
