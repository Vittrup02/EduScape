import type { TFunction } from 'i18next'
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

/** A problem in words; in a lock with several steps, it says which step it is in. */
function problemText(t: TFunction, document: RoomDocument, error: ValidationError): string {
  const problem = t(errorKey(error.code))
  const found = error.objectId === null ? undefined : findObject(document, error.objectId)
  const severalSteps = found?.object.type === 'Lock' && found.object.steps.length > 1
  return error.stepIndex !== null && severalSteps ? t('editor.validation.step', { n: error.stepIndex + 1, problem }) : problem
}

/** The problems grouped by object, in the order the server reported them; room-wide problems have no object. */
function groupByObject(
  t: TFunction,
  document: RoomDocument,
  errors: ValidationError[],
): { objectId: string | null; problems: string[] }[] {
  const groups: { objectId: string | null; problems: string[] }[] = []
  for (const error of errors) {
    const problem = problemText(t, document, error)
    const group = groups.find((g) => g.objectId === error.objectId)
    if (group === undefined) {
      groups.push({ objectId: error.objectId, problems: [problem] })
    } else if (!group.problems.includes(problem)) {
      group.problems.push(problem)
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
        {groupByObject(t, document, errors).map(({ objectId, problems }) => {
          const found = objectId === null ? undefined : findObject(document, objectId)
          const text = problems.join(' ')
          return (
            <li key={objectId ?? text}>
              {found === undefined ? (
                text
              ) : (
                <button
                  type="button"
                  className="quiet validation-item"
                  onClick={() => {
                    onShow(found.object.id)
                  }}
                >
                  <span className="validation-object">{objectLabel(t, document, found.object)}</span>
                  <span>{text}</span>
                </button>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}
