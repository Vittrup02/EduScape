import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { checkHealth } from './api/client'
import { useTeacher } from './auth/useTeacher'

type BackendStatus = 'checking' | 'online' | 'offline'

export function HomePage() {
  const { t } = useTranslation()
  const teacher = useTeacher()
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
    <main>
      <h1>{t('app.name')}</h1>
      <p className="status" data-state={status} role="status">
        {t(`health.${status}`)}
      </p>
      <p className="actions">
        <Link className="button primary" to="/join">
          {t('home.joinAsStudent')}
        </Link>
        <Link className="button" to={teacher ? '/teacher' : '/login'}>
          {t(teacher ? 'home.teacherHome' : 'home.teacherLogin')}
        </Link>
      </p>
    </main>
  )
}
