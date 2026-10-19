import { useTranslation } from 'react-i18next'
import type { RoomDocument, ValidationError } from '../api/editor'
import { errorKey } from '../i18n/errors'
import { findObject } from './editorDocument'
import { objectLabel } from './objectLabel'

interface ValidationListProps {
  document: RoomDocument
  errors: ValidationError[]
  onShow: (objectId: string) => void
}

/** What still keeps the room from being played, each with a way to go straight to the object. */
export function ValidationList({ document, errors, onShow }: ValidationListProps) {
  const { t } = useTranslation()

  if (errors.length === 0) {
    return (
      <p className="validation-ok" role="status">
        {t('editor.validation.ready')}
      </p>
    )
  }

  return (
    <section className="validation" aria-labelledby="validation-title">
      <h2 id="validation-title" className="panel-heading">
        {t('editor.validation.title')}
      </h2>
      <ul>
        {errors.map((error) => {
          const found = error.objectId === null ? undefined : findObject(document, error.objectId)
          return (
            <li key={`${error.code}:${error.objectId ?? ''}`}>
              {found === undefined ? (
                t(errorKey(error.code))
              ) : (
                <button
                  type="button"
                  className="quiet validation-item"
                  onClick={() => {
                    onShow(found.object.id)
                  }}
                >
                  <span className="validation-object">{objectLabel(t, document, found.object)}</span>
                  <span>{t(errorKey(error.code))}</span>
                </button>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}
