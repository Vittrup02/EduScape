import { useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { getEditorRoom } from '../api/editor'
import { useLoad } from '../api/useLoad'
import { errorKey } from '../i18n/errors'
import { Editor } from './Editor'

/** Loads one of the teacher's own rooms into the editor. */
export function EditorPage() {
  const { t } = useTranslation()
  const { roomId = '' } = useParams()
  const [state] = useLoad(useCallback((signal: AbortSignal) => getEditorRoom(roomId, signal), [roomId]))

  if (state.status === 'loaded') {
    return <Editor key={state.data.id} room={state.data} />
  }

  return (
    <main>
      {state.status === 'loading' ? (
        <p role="status" className="status">
          {t('editor.loading')}
        </p>
      ) : (
        <>
          <p role="alert" className="error">
            {t(errorKey(state.code))}
          </p>
          <p>
            <Link to="/teacher">{t('editor.back')}</Link>
          </p>
        </>
      )}
    </main>
  )
}
