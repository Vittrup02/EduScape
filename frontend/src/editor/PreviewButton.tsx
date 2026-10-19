import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { saveDeviceToken } from '../api/deviceStorage'
import { startPreview } from '../api/editor'
import { errorKey } from '../i18n/errors'

/** Not an API code: the room could not be saved first (e.g. the title is empty or the server is waking up). */
const notSaved = 'notSaved'

interface PreviewButtonProps {
  roomId: string
  /** Saves any pending change; resolves to false if the room could not be saved. */
  flush: () => Promise<boolean>
}

/**
 * Opens the student view of the room in a new tab, played on the server exactly like a session. The tab is opened
 * straight away, inside the click, so the browser doesn't block it while the room is saved and the preview starts.
 */
export function PreviewButton({ roomId, flush }: PreviewButtonProps) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const preview = async () => {
    setErrorCode(null)
    setBusy(true)
    const tab = window.open('', '_blank')
    try {
      if (!(await flush())) {
        tab?.close()
        setErrorCode(notSaved)
        return
      }
      // The play view picks the preview up like any student's device token.
      saveDeviceToken(await startPreview(roomId))
      if (tab === null) {
        window.location.assign('/play')
      } else {
        tab.location.href = '/play'
      }
    } catch (error) {
      tab?.close()
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="preview">
      <button type="button" className="primary" disabled={busy} onClick={() => void preview()}>
        {busy ? t('editor.preview.opening') : t('editor.preview.button')}
      </button>
      {errorCode !== null && (
        <p role="alert" className="error">
          {errorCode === notSaved ? t('editor.preview.notSaved') : t(errorKey(errorCode))}
        </p>
      )}
    </div>
  )
}
