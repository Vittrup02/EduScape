import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { EditorDoor, EditorLetter, EditorLock, EditorObject, EditorTextBox, RoomDocument } from '../api/editor'
import type { AnswerType } from '../api/play'
import { ObjectGraphic } from '../assets/ObjectGraphic'
import { assetsOfType } from '../assets/registry'
import { unlockedByChoices } from './editorDocument'
import { objectLabel } from './objectLabel'

/** A change to the selected object; `field` groups typing in one field into one undo step. */
export type ObjectChange = <T extends EditorObject>(update: (object: T) => T, field?: string) => void

interface InspectorProps {
  document: RoomDocument
  object: EditorObject
  sceneId: string
  onChange: ObjectChange
  /** Ends the current undo step, e.g. when a field loses focus. */
  onSeal: () => void
  onRemove: () => void
  onClose: () => void
  /** Adds a scene and returns its id, for a door that should lead to a new scene. */
  onAddScene: () => string
}

/** "3,5" and "3.5" both mean 3.5, like the answers students type. */
function parseNumber(text: string): number | null {
  const trimmed = text.trim().replace(',', '.')
  if (trimmed === '' || !/^[+-]?(\d+\.?\d*|\.\d+)$/.test(trimmed)) {
    return null
  }
  return Number(trimmed)
}

function Field({ label, hint, children }: { label: string; hint?: string; children: (id: string) => ReactNode }) {
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children(id)}
      {hint !== undefined && <p className="field-hint">{hint}</p>}
    </div>
  )
}

/**
 * The small panel for the selected object. It shows only what matters for the object's type: a lock's question and
 * answer, a letter's text or where a door leads; tolerance and answer options only for the answer types they apply to.
 */
export function Inspector({ document, object, sceneId, onChange, onSeal, onRemove, onClose, onAddScene }: InspectorProps) {
  const { t } = useTranslation()

  return (
    <section className="inspector" aria-labelledby="inspector-title">
      <div className="panel-heading">
        <h2 id="inspector-title">{objectLabel(t, document, object)}</h2>
        <button type="button" className="quiet" onClick={onClose}>
          {t('editor.inspector.done')}
        </button>
      </div>

      {object.type === 'Lock' && <LockFields lock={object} onChange={onChange} onSeal={onSeal} />}
      {object.type === 'Letter' && <LetterFields letter={object} onChange={onChange} onSeal={onSeal} />}
      {object.type === 'Door' && (
        <DoorFields document={document} door={object} sceneId={sceneId} onChange={onChange} onAddScene={onAddScene} />
      )}
      {object.type === 'ExitDoor' && <p className="field-hint">{t('editor.exitDoor.explanation')}</p>}
      {object.type === 'TextBox' && <TextBoxFields textBox={object} onChange={onChange} onSeal={onSeal} />}

      <UnlockedBySelect document={document} object={object} onChange={onChange} />

      <button type="button" className="danger" onClick={onRemove}>
        {t(`editor.inspector.remove${object.type}`)}
      </button>
    </section>
  )
}

const answerTypes: AnswerType[] = ['Code', 'Text', 'MultipleChoice']

function LockFields({ lock, onChange, onSeal }: { lock: EditorLock; onChange: ObjectChange; onSeal: () => void }) {
  const { t } = useTranslation()
  const lockAssets = assetsOfType('Lock')
  const [showHint, setShowHint] = useState(lock.hint !== null)
  const [toleranceText, setToleranceText] = useState(lock.tolerance === null ? '' : String(lock.tolerance))
  const [shownTolerance, setShownTolerance] = useState(lock.tolerance)

  // Undo and redo change the tolerance from outside; show their value unless it is what the teacher is typing.
  if (lock.tolerance !== shownTolerance) {
    setShownTolerance(lock.tolerance)
    if (parseNumber(toleranceText) !== lock.tolerance) {
      setToleranceText(lock.tolerance === null ? '' : String(lock.tolerance))
    }
  }

  const update = (changes: Partial<EditorLock>, field?: string) => {
    onChange<EditorLock>((l) => ({ ...l, ...changes }), field)
  }

  const chooseAnswerType = (answerType: AnswerType) => {
    if (answerType === lock.answerType) {
      return
    }
    // Multiple choice starts with the current answer as the first option, so nothing typed is lost.
    const options = answerType === 'MultipleChoice' && lock.options.length < 2 ? [lock.correctAnswer, ''] : lock.options
    update({ answerType, options })
  }

  const numericAnswer = parseNumber(lock.correctAnswer) !== null

  return (
    <>
      <fieldset>
        <legend>{t('editor.lock.look')}</legend>
        <div className="look-choices">
          {lockAssets.map((asset) => (
            <label key={asset.key} className="look-choice">
              <input
                type="radio"
                name={`look-${lock.id}`}
                checked={lock.graphicKey === asset.key}
                onChange={() => {
                  update({ graphicKey: asset.key })
                }}
              />
              <ObjectGraphic asset={asset} open={false} />
              <span>{t(asset.nameKey)}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <Field label={t('editor.lock.question')} hint={t('editor.lock.questionHint')}>
        {(id) => (
          <textarea
            id={id}
            rows={3}
            value={lock.question}
            onChange={(event) => {
              update({ question: event.target.value }, 'question')
            }}
            onBlur={onSeal}
          />
        )}
      </Field>

      <fieldset className="answer-types">
        <legend>{t('editor.lock.answerType')}</legend>
        {answerTypes.map((answerType) => (
          <label key={answerType} className="answer-type">
            <input
              type="radio"
              name={`answer-type-${lock.id}`}
              checked={lock.answerType === answerType}
              onChange={() => {
                chooseAnswerType(answerType)
              }}
            />
            <span className="answer-type-name">{t(`editor.lock.answerTypes.${answerType}`)}</span>
            <span className="answer-type-description">{t(`editor.lock.answerTypeDescriptions.${answerType}`)}</span>
          </label>
        ))}
      </fieldset>

      {lock.answerType === 'MultipleChoice' ? (
        <OptionsEditor lock={lock} onChange={onChange} onSeal={onSeal} />
      ) : (
        <Field label={t('editor.lock.correctAnswer')}>
          {(id) => (
            <input
              id={id}
              value={lock.correctAnswer}
              autoComplete="off"
              onChange={(event) => {
                update({ correctAnswer: event.target.value }, 'answer')
              }}
              onBlur={onSeal}
            />
          )}
        </Field>
      )}

      {lock.answerType === 'Text' && numericAnswer && (
        <Field label={t('editor.lock.tolerance')} hint={t('editor.lock.toleranceHint')}>
          {(id) => (
            <input
              id={id}
              inputMode="decimal"
              className="short-input"
              value={toleranceText}
              onChange={(event) => {
                setToleranceText(event.target.value)
                const value = parseNumber(event.target.value)
                update({ tolerance: value !== null && value >= 0 ? value : null }, 'tolerance')
              }}
              onBlur={onSeal}
            />
          )}
        </Field>
      )}

      {showHint ? (
        <>
          <Field label={t('editor.lock.hint')} hint={t('editor.lock.hintHint')}>
            {(id) => (
              <textarea
                id={id}
                rows={2}
                value={lock.hint ?? ''}
                onChange={(event) => {
                  update({ hint: event.target.value }, 'hint')
                }}
                onBlur={onSeal}
              />
            )}
          </Field>
          <button
            type="button"
            className="quiet add"
            onClick={() => {
              setShowHint(false)
              update({ hint: null })
            }}
          >
            {t('editor.lock.removeHint')}
          </button>
        </>
      ) : (
        <button
          type="button"
          className="quiet add"
          onClick={() => {
            setShowHint(true)
          }}
        >
          {t('editor.lock.addHint')}
        </button>
      )}
    </>
  )
}

function OptionsEditor({ lock, onChange, onSeal }: { lock: EditorLock; onChange: ObjectChange; onSeal: () => void }) {
  const { t } = useTranslation()
  const update = (changes: Partial<EditorLock>, field?: string) => {
    onChange<EditorLock>((l) => ({ ...l, ...changes }), field)
  }

  return (
    <fieldset className="options">
      <legend>{t('editor.lock.options')}</legend>
      <p className="field-hint">{t('editor.lock.optionsHint')}</p>
      {lock.options.map((option, index) => {
        const correct = option !== '' && option === lock.correctAnswer
        return (
          // Options have no ids of their own; the position is what the teacher edits.
          <div key={index} className="option-row">
            <input
              type="radio"
              name={`correct-${lock.id}`}
              checked={correct}
              aria-label={t('editor.lock.markCorrect', { n: index + 1 })}
              onChange={() => {
                update({ correctAnswer: option })
              }}
            />
            <input
              value={option}
              aria-label={t('editor.lock.option', { n: index + 1 })}
              onChange={(event) => {
                const options = lock.options.map((o, i) => (i === index ? event.target.value : o))
                update({ options, correctAnswer: correct ? event.target.value : lock.correctAnswer }, `option:${String(index)}`)
              }}
              onBlur={onSeal}
            />
            <button
              type="button"
              className="quiet"
              aria-label={t('editor.lock.removeOption', { n: index + 1 })}
              disabled={lock.options.length <= 2}
              onClick={() => {
                update({
                  options: lock.options.filter((_, i) => i !== index),
                  correctAnswer: correct ? '' : lock.correctAnswer,
                })
              }}
            >
              {t('editor.lock.removeOptionShort')}
            </button>
          </div>
        )
      })}
      <button
        type="button"
        className="quiet add"
        onClick={() => {
          update({ options: [...lock.options, ''] })
        }}
      >
        {t('editor.lock.addOption')}
      </button>
    </fieldset>
  )
}

function LetterFields({ letter, onChange, onSeal }: { letter: EditorLetter; onChange: ObjectChange; onSeal: () => void }) {
  const { t } = useTranslation()
  return (
    <Field label={t('editor.letter.text')} hint={t('editor.letter.textHint')}>
      {(id) => (
        <textarea
          id={id}
          rows={5}
          value={letter.text}
          onChange={(event) => {
            onChange<EditorLetter>((l) => ({ ...l, text: event.target.value }), 'text')
          }}
          onBlur={onSeal}
        />
      )}
    </Field>
  )
}

function TextBoxFields({ textBox, onChange, onSeal }: { textBox: EditorTextBox; onChange: ObjectChange; onSeal: () => void }) {
  const { t } = useTranslation()
  return (
    <Field label={t('editor.textBox.text')} hint={t('editor.textBox.textHint')}>
      {(id) => (
        <textarea
          id={id}
          rows={4}
          maxLength={1000}
          value={textBox.text}
          placeholder={t('editor.textBox.placeholder')}
          onChange={(event) => {
            onChange<EditorTextBox>((b) => ({ ...b, text: event.target.value }), 'text')
          }}
          onBlur={onSeal}
        />
      )}
    </Field>
  )
}

const newSceneValue = '__new'

function DoorFields({
  document,
  door,
  sceneId,
  onChange,
  onAddScene,
}: {
  document: RoomDocument
  door: EditorDoor
  sceneId: string
  onChange: ObjectChange
  onAddScene: () => string
}) {
  const { t } = useTranslation()
  const targets = document.scenes.filter((s) => s.id !== sceneId)

  return (
    <Field label={t('editor.door.target')}>
      {(id) => (
        <select
          id={id}
          value={door.targetSceneId ?? ''}
          onChange={(event) => {
            const value = event.target.value
            const targetSceneId = value === newSceneValue ? onAddScene() : value === '' ? null : value
            onChange<EditorDoor>((d) => ({ ...d, targetSceneId }))
          }}
        >
          <option value="">{t('editor.door.noTarget')}</option>
          {targets.map((scene) => (
            <option key={scene.id} value={scene.id}>
              {scene.name}
            </option>
          ))}
          <option value={newSceneValue}>{t('editor.door.newScene')}</option>
        </select>
      )}
    </Field>
  )
}

const visibleFromStart = ''

/** "Synlig fra start" or "Vises når … er løst": the only choice there is, so it is a plain dropdown. */
function UnlockedBySelect({
  document,
  object,
  onChange,
}: {
  document: RoomDocument
  object: EditorObject
  onChange: ObjectChange
}) {
  const { t } = useTranslation()
  const choices = unlockedByChoices(document, object.id)

  return (
    <Field label={t('editor.unlockedBy.label')} hint={t('editor.unlockedBy.hint')}>
      {(id) => (
        <select
          id={id}
          value={object.unlockedById ?? visibleFromStart}
          onChange={(event) => {
            const unlockedById = event.target.value === visibleFromStart ? null : event.target.value
            onChange((o) => ({ ...o, unlockedById }))
          }}
        >
          <option value={visibleFromStart}>{t('editor.unlockedBy.fromStart')}</option>
          {choices.map((choice) => (
            <option key={choice.id} value={choice.id}>
              {t(choice.type === 'Door' ? 'editor.unlockedBy.whenOpened' : 'editor.unlockedBy.whenSolved', {
                name: objectLabel(t, document, choice),
              })}
            </option>
          ))}
        </select>
      )}
    </Field>
  )
}
