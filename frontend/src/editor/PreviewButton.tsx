import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { saveDeviceToken } from '../api/deviceStorage'
import { startPreview, type EditorScene } from '../api/editor'
import { errorKey } from '../i18n/errors'

/** Not an API code: the room could not be saved first (e.g. the title is empty or the server is waking up). */
const notSaved = 'notSaved'

interface PreviewButtonProps {
  roomId: string
  /** The scene the teacher is working on, where the preview starts. */
  scene: EditorScene
  isStartScene: boolean
  /** Saves any pending change; resolves to false if the room could not be saved. */
  flush: () => Promise<boolean>
}

/**
 * Opens the student view of the room in a new tab, played on the server exactly like a session. It starts in the
 * scene the teacher is working on, with the locks before it solved, so a change can be tried at once; "start over"
 * plays the whole room. The tab is opened straight away, inside the click, so the browser doesn't block it while the
 * room is saved and the preview starts.
 */
export function PreviewButton({ roomId, scene, isStartScene, flush }: PreviewButtonProps) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const preview = async (startSceneId: string | null) => {
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
      saveDeviceToken(await startPreview(roomId, startSceneId))
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
      <div className="preview-buttons">
        {!isStartScene && (
          <button type="button" className="quiet" disabled={busy} onClick={() => void preview(null)}>
            {t('editor.preview.fromStart')}
          </button>
        )}
        <button
          type="button"
          className="primary"
          disabled={busy}
          aria-describedby={isStartScene ? undefined : `preview-from-${scene.id}`}
          onClick={() => void preview(isStartScene ? null : scene.id)}
        >
          {busy ? t('editor.preview.opening') : t('editor.preview.button')}
        </button>
      </div>
      {!isStartScene && (
        <p id={`preview-from-${scene.id}`} className="field-hint preview-from">
          {t('editor.preview.startsIn', { scene: scene.name })}
        </p>
      )}
      {errorCode !== null && (
        <p role="alert" className="error">
          {errorCode === notSaved ? t('editor.preview.notSaved') : t(errorKey(errorCode))}
        </p>
      )}
    </div>
  )
}
