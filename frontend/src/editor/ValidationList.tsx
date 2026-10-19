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

/** The problems grouped by object, in the order the server reported them; room-wide problems have no object. */
function groupByObject(errors: ValidationError[]): { objectId: string | null; codes: string[] }[] {
  const groups: { objectId: string | null; codes: string[] }[] = []
  for (const error of errors) {
    const group = groups.find((g) => g.objectId === error.objectId)
    if (group === undefined) {
      groups.push({ objectId: error.objectId, codes: [error.code] })
    } else if (!group.codes.includes(error.code)) {
      group.codes.push(error.code)
    }
  }
  return groups
}

/** What still keeps the room from being played: one line per object, with a way to go straight to it. */
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
        {groupByObject(errors).map(({ objectId, codes }) => {
          const found = objectId === null ? undefined : findObject(document, objectId)
          const problems = codes.map((code) => t(errorKey(code))).join(' ')
          return (
            <li key={objectId ?? codes.join()}>
              {found === undefined ? (
                problems
              ) : (
                <button
                  type="button"
                  className="quiet validation-item"
                  onClick={() => {
                    onShow(found.object.id)
                  }}
                >
                  <span className="validation-object">{objectLabel(t, document, found.object)}</span>
                  <span>{problems}</span>
                </button>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}
