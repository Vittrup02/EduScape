import { useId, useState, type SubmitEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { createRoom } from '../api/editor'
import { subjects, type GradeLevel, type Subject } from '../api/teacher'
import { errorKey } from '../i18n/errors'
import { GradeLevelSelect } from './GradeLevelSelect'

/** Three things and the room exists; everything else happens in the editor. */
export function NewRoomPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const titleId = useId()
  const subjectId = useId()
  const gradeId = useId()
  const [title, setTitle] = useState('')
  const [subject, setSubject] = useState<Subject>('Mathematics')
  const [gradeLevel, setGradeLevel] = useState<GradeLevel>('Grade7')
  const [busy, setBusy] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const submit = async (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault()
    setBusy(true)
    setErrorCode(null)
    try {
      const room = await createRoom(title, subject, gradeLevel, t('editor.scene.defaultName', { n: 1 }))
      void navigate(`/teacher/rooms/${room.id}/edit`, { replace: true })
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
      setBusy(false)
    }
  }

  return (
    <main>
      <h1>{t('editor.newRoom.title')}</h1>
      <p>{t('editor.newRoom.intro')}</p>
      <form className="auth-form" onSubmit={(event) => void submit(event)}>
        <label htmlFor={titleId}>{t('editor.title')}</label>
        <input
          id={titleId}
          value={title}
          required
          maxLength={200}
          placeholder={t('editor.titlePlaceholder')}
          onChange={(event) => {
            setTitle(event.target.value)
          }}
        />
        <label htmlFor={subjectId}>{t('editor.subject')}</label>
        <select
          id={subjectId}
          value={subject}
          onChange={(event) => {
            setSubject(event.target.value as Subject)
          }}
        >
          {subjects.map((s) => (
            <option key={s} value={s}>
              {t(`subjects.${s}`)}
            </option>
          ))}
        </select>
        <label htmlFor={gradeId}>{t('editor.gradeLevel')}</label>
        <GradeLevelSelect id={gradeId} value={gradeLevel} onChange={setGradeLevel} />
        {errorCode !== null && (
          <p role="alert" className="error">
            {t(errorKey(errorCode))}
          </p>
        )}
        <div className="actions">
          <button type="submit" className="primary" disabled={busy}>
            {busy ? t('editor.newRoom.creating') : t('editor.newRoom.create')}
          </button>
          <Link className="button" to="/teacher">
            {t('editor.newRoom.cancel')}
          </Link>
        </div>
      </form>
    </main>
  )
}
