import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { useTeacher } from './auth/useTeacher'

export function HomePage() {
  const { t } = useTranslation()
  const teacher = useTeacher()

  return (
    <main>
      <h1>{t('app.name')}</h1>
      <p className="tagline">{t('home.tagline')}</p>
      <p className="actions">
        <Link className="button primary" to="/join">
          {t('home.joinAsStudent')}
        </Link>
        <Link className="button" to={teacher ? '/teacher' : '/login'}>
          {t(teacher ? 'home.teacherHome' : 'home.teacherLogin')}
        </Link>
      </p>
      {!teacher && (
        <p>
          {t('home.newTeacher')} <Link to="/register">{t('home.createAccount')}</Link>
        </p>
      )}
    </main>
  )
}
