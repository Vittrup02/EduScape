import { useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { isBackendWaking, onBackendWakingChange } from './api/client'

/**
 * Shown on every page while a request waits for the backend to wake up (Render's free tier sleeps when idle), so
 * teachers and students see a friendly "getting ready" instead of an error.
 */
export function WakeUpNotice() {
  const { t } = useTranslation()
  const waking = useSyncExternalStore(onBackendWakingChange, isBackendWaking)

  if (!waking) {
    return null
  }
  return (
    <p role="status" className="wake-up-notice">
      <span className="wake-up-spinner" aria-hidden="true" />
      {t('backend.waking')}
    </p>
  )
}
