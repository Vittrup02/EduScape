import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { uploadContentTypes, type EditorLock, type EditorOrderItem, type EditorStep } from '../api/editor'
import { errorKey } from '../i18n/errors'
import { formatDecimal } from '../i18n/format'
import { Grip } from '../sortable/Grip'
import { SortableList } from '../sortable/SortableList'
import { Field, type ObjectChange } from './Field'
import { randomId } from './ids'
import {
  changeStepKind,
  createOrderItem,
  createPuzzle,
  createStep,
  moveItem,
  parseNumber,
  puzzleKinds,
  stepKind,
  stepKinds,
  type PuzzleKind,
} from './puzzle'
import { useImageUpload } from './useImageUpload'

/** The server's limits (Lock.MaxSteps and LockStep.Max*); the editor stops offering more before them. */
const maxSteps = 10
const maxOptions = 10
const maxAcceptedAnswers = 10
const maxOrderItems = 12

/** Changes one step; `field` groups typing in one field into one undo step. */
type StepChange = (update: (step: EditorStep) => EditorStep, field?: string) => void

interface PuzzleEditorProps {
  lock: EditorLock
  uploadsEnabled: boolean
  onChange: ObjectChange
  onSeal: () => void
}

/**
 * A lock's puzzle. A new lock asks for the kind of puzzle on four big cards; after that the teacher fills in the one
 * step, or the steps of a puzzle with several, which are sorted by dragging. The lock's graphic is chosen apart from
 * this, so it can change without touching the puzzle.
 */
export function PuzzleEditor({ lock, uploadsEnabled, onChange, onSeal }: PuzzleEditorProps) {
  const { t } = useTranslation()
  const [openStepId, setOpenStepId] = useState<string | null>(lock.steps[0]?.id ?? null)

  const setSteps = (update: (steps: EditorStep[]) => EditorStep[]) => {
    onChange<EditorLock>((l) => ({ ...l, steps: update(l.steps) }))
  }
  const changeStep =
    (stepId: string): StepChange =>
    (update, field) => {
      onChange<EditorLock>(
        (l) => ({ ...l, steps: l.steps.map((s) => (s.id === stepId ? update(s) : s)) }),
        field === undefined ? undefined : `${stepId}:${field}`,
      )
    }
  const addStep = () => {
    const step = createStep('Typed')
    setSteps((steps) => [...steps, step])
    setOpenStepId(step.id)
  }

  if (lock.steps.length === 0) {
    return (
      <PuzzleCards
        onPick={(kind) => {
          const steps = createPuzzle(kind)
          setSteps(() => steps)
          setOpenStepId(steps[0].id)
        }}
      />
    )
  }

  if (lock.steps.length === 1) {
    const step = lock.steps[0]
    return (
      <>
        <StepFields step={step} uploadsEnabled={uploadsEnabled} onChange={changeStep(step.id)} onSeal={onSeal} />
        <button type="button" className="quiet add" onClick={addStep}>
          {t('editor.puzzle.addStep')}
        </button>
      </>
    )
  }

  const stepName = (index: number) => t('editor.puzzle.step', { n: index + 1 })
  return (
    <section className="puzzle-steps" aria-labelledby={`steps-${lock.id}`}>
      <h3 id={`steps-${lock.id}`}>{t('editor.puzzle.steps')}</h3>
      <p className="field-hint">{t('editor.puzzle.stepsHint')}</p>
      <SortableList
        items={lock.steps}
        label={t('editor.puzzle.steps')}
        getKey={(step) => step.id}
        itemLabel={(_, index) => stepName(index)}
        onMove={(from, to) => {
          setSteps((steps) => moveItem(steps, from, to))
        }}
        renderItem={(step, index, handle) => {
          const open = step.id === openStepId
          const question = step.question.trim()
          return (
            <div className="step-card" data-open={open || undefined}>
              <div className="step-card-header">
                <Grip handle={handle} item={stepName(index)} />
                <button
                  type="button"
                  className="step-toggle"
                  aria-expanded={open}
                  onClick={() => {
                    setOpenStepId(open ? null : step.id)
                  }}
                >
                  <span className="step-name">{stepName(index)}</span>
                  <span className="step-summary">
                    {t(`editor.puzzle.kinds.${stepKind(step)}`)} · {question === '' ? t('editor.puzzle.noQuestion') : question}
                  </span>
                </button>
              </div>
              {open && (
                <div className="step-card-body">
                  <StepFields step={step} uploadsEnabled={uploadsEnabled} onChange={changeStep(step.id)} onSeal={onSeal} />
                  <button
                    type="button"
                    className="danger"
                    onClick={() => {
                      setSteps((steps) => steps.filter((s) => s.id !== step.id))
                    }}
                  >
                    {t('editor.puzzle.removeStep', { n: index + 1 })}
                  </button>
                </div>
              )}
            </div>
          )
        }}
        footer={
          lock.steps.length < maxSteps ? (
            <button type="button" className="quiet add" onClick={addStep}>
              {t('editor.puzzle.addStep')}
            </button>
          ) : undefined
        }
      />
    </section>
  )
}

/** The four kinds of puzzle, as big cards: the first thing a teacher decides about a new lock. */
function PuzzleCards({ onPick }: { onPick: (kind: PuzzleKind) => void }) {
  const { t } = useTranslation()
  const headingId = useId()
  return (
    <section className="puzzle-cards" aria-labelledby={headingId}>
      <h3 id={headingId}>{t('editor.puzzle.choose')}</h3>
      <ul>
        {puzzleKinds.map((kind) => (
          <li key={kind}>
            <button
              type="button"
              className="puzzle-card"
              data-kind={kind}
              onClick={() => {
                onPick(kind)
              }}
            >
              <span className="puzzle-card-name">{t(`editor.puzzle.kinds.${kind}`)}</span>
              <span className="puzzle-card-description">{t(`editor.puzzle.kindDescriptions.${kind}`)}</span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

interface StepFieldsProps {
  step: EditorStep
  uploadsEnabled: boolean
  onChange: StepChange
  onSeal: () => void
}

/** One step: how students answer it, the question, the answer and the hint. */
function StepFields({ step, uploadsEnabled, onChange, onSeal }: StepFieldsProps) {
  const { t } = useTranslation()
  const kind = stepKind(step)

  return (
    <>
      <fieldset className="answer-types">
        <legend>{t('editor.lock.answerType')}</legend>
        {stepKinds.map((choice) => (
          <label key={choice} className="answer-type">
            <input
              type="radio"
              name={`answer-type-${step.id}`}
              checked={kind === choice}
              onChange={() => {
                onChange((s) => changeStepKind(s, choice))
              }}
            />
            <span className="answer-type-name">{t(`editor.puzzle.kinds.${choice}`)}</span>
            <span className="answer-type-description">{t(`editor.puzzle.kindDescriptions.${choice}`)}</span>
          </label>
        ))}
      </fieldset>

      <Field label={t('editor.lock.question')} hint={t('editor.lock.questionHint')}>
        {(id) => (
          <textarea
            id={id}
            rows={3}
            value={step.question}
            onChange={(event) => {
              const question = event.target.value
              onChange((s) => ({ ...s, question }), 'question')
            }}
            onBlur={onSeal}
          />
        )}
      </Field>

      {kind === 'Typed' && <TypedAnswer step={step} onChange={onChange} onSeal={onSeal} />}
      {kind === 'MultipleChoice' && <OptionsEditor step={step} onChange={onChange} onSeal={onSeal} />}
      {kind === 'Order' && (
        <OrderItemsEditor step={step} uploadsEnabled={uploadsEnabled} onChange={onChange} onSeal={onSeal} />
      )}

      <HintField step={step} onChange={onChange} onSeal={onSeal} />
    </>
  )
}

interface StepPartProps {
  step: EditorStep
  onChange: StepChange
  onSeal: () => void
}

/**
 * A typed answer: free text (with synonyms and forgiven spelling mistakes if the teacher wants), a number (with a
 * tolerance), or an exact code. Only the settings that fit the answer are shown.
 */
function TypedAnswer({ step, onChange, onSeal }: StepPartProps) {
  const { t } = useTranslation()
  const isCode = step.answerType === 'Code'
  const numeric = parseNumber(step.correctAnswer) !== null

  return (
    <>
      <Field label={t('editor.lock.correctAnswer')}>
        {(id) => (
          <input
            id={id}
            value={step.correctAnswer}
            autoComplete="off"
            onChange={(event) => {
              const correctAnswer = event.target.value
              onChange((s) => ({ ...s, correctAnswer }), 'answer')
            }}
            onBlur={onSeal}
          />
        )}
      </Field>

      <div className="field">
        <label className="checkbox">
          <input
            type="checkbox"
            checked={isCode}
            onChange={(event) => {
              const answerType = event.target.checked ? 'Code' : 'Text'
              onChange((s) => ({ ...s, answerType }))
            }}
          />
          {t('editor.puzzle.exactCode')}
        </label>
        <p className="field-hint">{t(isCode ? 'editor.puzzle.exactCodeHint' : 'editor.puzzle.textHint')}</p>
      </div>

      {!isCode && numeric && <ToleranceField step={step} onChange={onChange} onSeal={onSeal} />}
      {!isCode && !numeric && (
        <>
          <AcceptedAnswers step={step} onChange={onChange} onSeal={onSeal} />
          <div className="field">
            <label className="checkbox">
              <input
                type="checkbox"
                checked={step.acceptTypos}
                onChange={(event) => {
                  const acceptTypos = event.target.checked
                  onChange((s) => ({ ...s, acceptTypos }))
                }}
              />
              {t('editor.puzzle.acceptTypos')}
            </label>
            <p className="field-hint">{t('editor.puzzle.acceptTyposHint')}</p>
          </div>
        </>
      )}
    </>
  )
}

/**
 * The tolerance of a numeric answer, written as "± 1,0" in the teacher's language; a comma and a point both work.
 * The hint shows which answers that accepts.
 */
function ToleranceField({ step, onChange, onSeal }: StepPartProps) {
  const { t, i18n } = useTranslation()
  const format = (value: number | null) => (value === null ? '' : formatDecimal(value, i18n.language))
  const [text, setText] = useState(() => format(step.tolerance))
  const [shownTolerance, setShownTolerance] = useState(step.tolerance)

  // Undo and redo change the tolerance from outside; show their value unless it is what the teacher is typing.
  if (step.tolerance !== shownTolerance) {
    setShownTolerance(step.tolerance)
    if (parseNumber(text) !== step.tolerance) {
      setText(format(step.tolerance))
    }
  }

  const answer = parseNumber(step.correctAnswer)
  const typed = parseNumber(text)
  const invalid = text.trim() !== '' && (typed === null || typed < 0)
  const range =
    answer !== null && step.tolerance !== null && step.tolerance > 0
      ? t('editor.puzzle.toleranceRange', { from: format(answer - step.tolerance), to: format(answer + step.tolerance) })
      : t('editor.puzzle.toleranceHint', { example: format(1) })

  return (
    <Field label={t('editor.puzzle.tolerance')} hint={range}>
      {(id) => (
        <span className="tolerance-input">
          <span aria-hidden="true">±</span>
          <input
            id={id}
            inputMode="decimal"
            className="short-input"
            value={text}
            placeholder={format(0)}
            aria-invalid={invalid || undefined}
            onChange={(event) => {
              setText(event.target.value)
              const value = parseNumber(event.target.value)
              const tolerance = value !== null && value >= 0 ? value : null
              onChange((s) => ({ ...s, tolerance }), 'tolerance')
            }}
            onBlur={onSeal}
          />
        </span>
      )}
    </Field>
  )
}

/** Other answers that are right too, e.g. synonyms. Shown once the teacher adds the first one. */
function AcceptedAnswers({ step, onChange, onSeal }: StepPartProps) {
  const { t } = useTranslation()
  const setAnswers = (update: (answers: string[]) => string[], field?: string) => {
    onChange((s) => ({ ...s, acceptedAnswers: update(s.acceptedAnswers) }), field)
  }
  const add = (
    <button
      type="button"
      className="quiet add"
      onClick={() => {
        setAnswers((answers) => [...answers, ''])
      }}
    >
      {t('editor.puzzle.addAcceptedAnswer')}
    </button>
  )

  if (step.acceptedAnswers.length === 0) {
    return add
  }

  return (
    <fieldset className="accepted-answers">
      <legend>{t('editor.puzzle.acceptedAnswers')}</legend>
      <p className="field-hint">{t('editor.puzzle.acceptedAnswersHint')}</p>
      {step.acceptedAnswers.map((answer, index) => (
        // Alternatives have no ids and aren't sorted; the position is what the teacher edits.
        <div key={index} className="option-row">
          <input
            value={answer}
            autoComplete="off"
            aria-label={t('editor.puzzle.acceptedAnswer', { n: index + 1 })}
            onChange={(event) => {
              const value = event.target.value
              setAnswers((answers) => answers.map((a, i) => (i === index ? value : a)), `accepted:${String(index)}`)
            }}
            onBlur={onSeal}
          />
          <button
            type="button"
            className="quiet"
            aria-label={t('editor.puzzle.removeAcceptedAnswer', { n: index + 1 })}
            onClick={() => {
              setAnswers((answers) => answers.filter((_, i) => i !== index))
            }}
          >
            {t('editor.lock.removeOptionShort')}
          </button>
        </div>
      ))}
      {step.acceptedAnswers.length < maxAcceptedAnswers && add}
    </fieldset>
  )
}

/** The choices of a multiple-choice step, sorted by dragging; the teacher marks the right one. */
function OptionsEditor({ step, onChange, onSeal }: StepPartProps) {
  const { t } = useTranslation()
  const optionName = (text: string, index: number) => text.trim() || t('editor.lock.option', { n: index + 1 })

  return (
    <fieldset className="options">
      <legend>{t('editor.lock.options')}</legend>
      <p className="field-hint">{t('editor.lock.optionsHint')}</p>
      <SortableList
        items={step.options}
        label={t('editor.lock.options')}
        getKey={(option) => option.id}
        itemLabel={(option, index) => optionName(option.text, index)}
        onMove={(from, to) => {
          onChange((s) => ({ ...s, options: moveItem(s.options, from, to) }))
        }}
        renderItem={(option, index, handle) => {
          const correct = option.text !== '' && option.text === step.correctAnswer
          return (
            <div className="option-row">
              <Grip handle={handle} item={optionName(option.text, index)} />
              <input
                type="radio"
                name={`correct-${step.id}`}
                checked={correct}
                aria-label={t('editor.lock.markCorrect', { n: index + 1 })}
                onChange={() => {
                  onChange((s) => ({ ...s, correctAnswer: option.text }))
                }}
              />
              <input
                value={option.text}
                aria-label={t('editor.lock.option', { n: index + 1 })}
                onChange={(event) => {
                  const text = event.target.value
                  onChange(
                    (s) => ({
                      ...s,
                      options: s.options.map((o) => (o.id === option.id ? { ...o, text } : o)),
                      correctAnswer: correct ? text : s.correctAnswer,
                    }),
                    `option:${option.id}`,
                  )
                }}
                onBlur={onSeal}
              />
              <button
                type="button"
                className="quiet"
                aria-label={t('editor.lock.removeOption', { n: index + 1 })}
                disabled={step.options.length <= 2}
                onClick={() => {
                  onChange((s) => ({
                    ...s,
                    options: s.options.filter((o) => o.id !== option.id),
                    correctAnswer: correct ? '' : s.correctAnswer,
                  }))
                }}
              >
                {t('editor.lock.removeOptionShort')}
              </button>
            </div>
          )
        }}
        footer={
          step.options.length < maxOptions ? (
            <button
              type="button"
              className="quiet add"
              onClick={() => {
                onChange((s) => ({ ...s, options: [...s.options, { id: randomId(), text: '' }] }))
              }}
            >
              {t('editor.lock.addOption')}
            </button>
          ) : undefined
        }
      />
    </fieldset>
  )
}

/**
 * The items of an order step. The teacher drags them into the right order: the order of the list is the correct
 * order, which students never see. Each item has a text and, if uploads are on, an optional picture.
 */
function OrderItemsEditor({ step, uploadsEnabled, onChange, onSeal }: StepPartProps & { uploadsEnabled: boolean }) {
  const { t } = useTranslation()
  const itemName = (item: EditorOrderItem, index: number) => item.text.trim() || t('editor.puzzle.orderItem', { n: index + 1 })
  const changeItem = (itemId: string, changes: Partial<EditorOrderItem>, field?: string) => {
    onChange(
      (s) => ({ ...s, orderItems: s.orderItems.map((i) => (i.id === itemId ? { ...i, ...changes } : i)) }),
      field === undefined ? undefined : `item:${itemId}:${field}`,
    )
  }

  return (
    <fieldset className="order-items">
      <legend>{t('editor.puzzle.orderItems')}</legend>
      <p className="field-hint">{t('editor.puzzle.orderItemsHint')}</p>
      <SortableList
        items={step.orderItems}
        label={t('editor.puzzle.orderItems')}
        getKey={(item) => item.id}
        itemLabel={itemName}
        onMove={(from, to) => {
          onChange((s) => ({ ...s, orderItems: moveItem(s.orderItems, from, to) }))
        }}
        renderItem={(item, index, handle) => (
          <OrderItemRow
            item={item}
            index={index}
            grip={<Grip handle={handle} item={itemName(item, index)} />}
            uploadsEnabled={uploadsEnabled}
            canRemove={step.orderItems.length > 2}
            onChange={(changes, field) => {
              changeItem(item.id, changes, field)
            }}
            onRemove={() => {
              onChange((s) => ({ ...s, orderItems: s.orderItems.filter((i) => i.id !== item.id) }))
            }}
            onSeal={onSeal}
          />
        )}
        footer={
          step.orderItems.length < maxOrderItems ? (
            <button
              type="button"
              className="quiet add"
              onClick={() => {
                onChange((s) => ({ ...s, orderItems: [...s.orderItems, createOrderItem()] }))
              }}
            >
              {t('editor.puzzle.addOrderItem')}
            </button>
          ) : undefined
        }
      />
    </fieldset>
  )
}

interface OrderItemRowProps {
  item: EditorOrderItem
  index: number
  grip: ReactNode
  uploadsEnabled: boolean
  canRemove: boolean
  onChange: (changes: Partial<EditorOrderItem>, field?: string) => void
  onRemove: () => void
  onSeal: () => void
}

function OrderItemRow({ item, index, grip, uploadsEnabled, canRemove, onChange, onRemove, onSeal }: OrderItemRowProps) {
  const { t } = useTranslation()
  const uploadId = useId()
  const { upload, uploading, errorCode } = useImageUpload((image) => {
    onChange({ imageKey: image.imageKey, imageUrl: image.imageUrl })
  })
  const n = index + 1

  return (
    <div className="order-item-row">
      {grip}
      <span className="order-number" aria-hidden="true">
        {n}
      </span>
      <input
        value={item.text}
        autoComplete="off"
        aria-label={t('editor.puzzle.orderItem', { n })}
        onChange={(event) => {
          onChange({ text: event.target.value }, 'text')
        }}
        onBlur={onSeal}
      />
      {item.imageKey !== null ? (
        <span className="order-item-image">
          {item.imageUrl !== null && <img src={item.imageUrl} alt="" draggable={false} />}
          <button
            type="button"
            className="quiet"
            aria-label={t('editor.puzzle.removeImage', { n })}
            onClick={() => {
              onChange({ imageKey: null, imageUrl: null })
            }}
          >
            {t('editor.puzzle.removeImageShort')}
          </button>
        </span>
      ) : (
        uploadsEnabled && (
          <span className="order-item-image">
            <label htmlFor={uploadId} className="button quiet upload-button" data-busy={uploading || undefined}>
              {uploading ? t('editor.scene.uploading') : t('editor.puzzle.addImageShort')}
            </label>
            <input
              id={uploadId}
              aria-label={t('editor.puzzle.addImage', { n })}
              className="visually-hidden"
              type="file"
              accept={uploadContentTypes.join(',')}
              disabled={uploading}
              onChange={(event) => {
                const file = event.target.files?.[0]
                event.target.value = ''
                if (file !== undefined) {
                  void upload(file)
                }
              }}
            />
          </span>
        )
      )}
      <button
        type="button"
        className="quiet"
        aria-label={t('editor.puzzle.removeOrderItem', { n })}
        disabled={!canRemove}
        onClick={onRemove}
      >
        {t('editor.lock.removeOptionShort')}
      </button>
      {errorCode !== null && (
        <p role="alert" className="error">
          {t(errorKey(errorCode))}
        </p>
      )}
    </div>
  )
}

/** The step's hint, shown to the team after three wrong answers; hidden until the teacher wants one. */
function HintField({ step, onChange, onSeal }: StepPartProps) {
  const { t } = useTranslation()
  const [showHint, setShowHint] = useState(step.hint !== null)

  if (!showHint) {
    return (
      <button
        type="button"
        className="quiet add"
        onClick={() => {
          setShowHint(true)
        }}
      >
        {t('editor.lock.addHint')}
      </button>
    )
  }

  return (
    <>
      <Field label={t('editor.lock.hint')} hint={t('editor.lock.hintHint')}>
        {(id) => (
          <textarea
            id={id}
            rows={2}
            value={step.hint ?? ''}
            onChange={(event) => {
              const hint = event.target.value
              onChange((s) => ({ ...s, hint }), 'hint')
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
          onChange((s) => ({ ...s, hint: null }))
        }}
      >
        {t('editor.lock.removeHint')}
      </button>
    </>
  )
}
