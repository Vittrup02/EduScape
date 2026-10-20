import { useId, useState, type SubmitEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useLocation } from 'react-router'
import { login, register } from '../api/auth'
import { ApiError } from '../api/client'
import { errorKey } from '../i18n/errors'
import type { LoginRedirectState } from './RequireTeacher'
import { useTeacher } from './useTeacher'

type Mode = 'login' | 'register'

function AuthForm({ mode }: { mode: Mode }) {
  const { t } = useTranslation()
  const teacher = useTeacher()
  const location = useLocation()
  const hintId = useId()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const redirectState = location.state as LoginRedirectState | null
  if (teacher) {
    return <Navigate to={redirectState?.from ?? '/teacher'} replace />
  }

  async function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    setPending(true)
    setErrorCode(null)
    try {
      await (mode === 'login' ? login(email, password) : register(email, password))
      // Signed in: the next render redirects.
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
    } finally {
      setPending(false)
    }
  }

  const isLogin = mode === 'login'
  const submitLabel = isLogin
    ? t(pending ? 'auth.loggingIn' : 'auth.loginButton')
    : t(pending ? 'auth.registering' : 'auth.registerButton')

  return (
    <main>
      <h1>{t(isLogin ? 'auth.loginTitle' : 'auth.registerTitle')}</h1>
      {/* noValidate: the server validates, and its error codes are shown translated instead of the browser's own messages. */}
      <form className="auth-form" noValidate onSubmit={(event) => void handleSubmit(event)}>
        <label>
          {t('auth.email')}
          <input
            type="email"
            name="email"
            autoComplete="email"
            required
            value={email}
            onChange={(event) => {
              setEmail(event.target.value)
            }}
          />
        </label>
        <label>
          {t('auth.password')}
          <input
            type="password"
            name="password"
            autoComplete={isLogin ? 'current-password' : 'new-password'}
            required
            maxLength={128}
            aria-describedby={isLogin ? undefined : hintId}
            value={password}
            onChange={(event) => {
              setPassword(event.target.value)
            }}
          />
        </label>
        {!isLogin && (
          <p id={hintId} className="hint">
            {t('auth.passwordHint')}
          </p>
        )}
        {errorCode && (
          <p role="alert" className="error">
            {t(errorKey(errorCode))}
          </p>
        )}
        <button type="submit" className="primary" disabled={pending}>
          {submitLabel}
        </button>
      </form>
      <p>
        {t(isLogin ? 'auth.noAccount' : 'auth.haveAccount')}{' '}
        <Link to={isLogin ? '/register' : '/login'} state={redirectState}>
          {t(isLogin ? 'auth.goToRegister' : 'auth.goToLogin')}
        </Link>
      </p>
    </main>
  )
}

export function LoginPage() {
  return <AuthForm mode="login" />
}

export function RegisterPage() {
  return <AuthForm mode="register" />
}
