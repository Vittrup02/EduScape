import { useTranslation } from 'react-i18next'
import { BrowserRouter, Link, Navigate, Outlet, Route, Routes } from 'react-router'
import { logout } from './api/auth'
import { LoginPage, RegisterPage } from './auth/AuthForm'
import { RequireTeacher } from './auth/RequireTeacher'
import { useTeacher } from './auth/useTeacher'
import { EditorPage } from './editor/EditorPage'
import { NewRoomPage } from './editor/NewRoomPage'
import { HomePage } from './HomePage'
import { LanguageSwitcher } from './i18n/LanguageSwitcher'
import { JoinPage } from './play/JoinPage'
import { PlayPage } from './play/PlayPage'
import { SessionPage } from './teacher/SessionPage'
import { StartSessionPage } from './teacher/StartSessionPage'
import { TeacherHomePage } from './teacher/TeacherHomePage'

/** Header on every page: the language switcher for everyone, and logout for a signed-in teacher. */
function Layout() {
  const { t } = useTranslation()
  const teacher = useTeacher()

  return (
    <>
      <header>
        <Link to="/" className="brand">
          {t('app.name')}
        </Link>
        <div className="header-actions">
          {teacher && (
            <>
              <span className="signed-in-as">{t('auth.signedInAs', { email: teacher.email })}</span>
              <button type="button" onClick={() => void logout()}>
                {t('auth.logout')}
              </button>
            </>
          )}
          <LanguageSwitcher />
        </div>
      </header>
      <Outlet />
    </>
  )
}

/** All routes; separate from the router so tests can render them in a MemoryRouter. */
export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />
        <Route
          path="teacher"
          element={
            <RequireTeacher>
              <Outlet />
            </RequireTeacher>
          }
        >
          <Route index element={<TeacherHomePage />} />
          <Route path="rooms/new" element={<NewRoomPage />} />
          <Route path="rooms/:roomId/edit" element={<EditorPage />} />
          <Route path="rooms/:roomId/start" element={<StartSessionPage kind="room" />} />
          <Route path="templates/:roomId/start" element={<StartSessionPage kind="template" />} />
          <Route path="sessions/:sessionId" element={<SessionPage />} />
        </Route>
        {/* Students never log in. */}
        <Route path="join" element={<JoinPage />} />
        <Route path="play" element={<PlayPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}

function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  )
}

export default App
