import { useTranslation } from 'react-i18next'
import type {
  DoorOpenCondition,
  EditorDoor,
  EditorDoorway,
  EditorLetter,
  EditorLock,
  EditorObject,
  EditorTextBox,
  RoomDocument,
} from '../api/editor'
import { ObjectGraphic } from '../assets/ObjectGraphic'
import { assetsOfType } from '../assets/registry'
import { findObject, lockChoices, unlockedByChoices } from './editorDocument'
import { Field, type ObjectChange } from './Field'
import { objectLabel } from './objectLabel'
import { PuzzleEditor } from './PuzzleEditor'

interface InspectorProps {
  document: RoomDocument
  object: EditorObject
  sceneId: string
  uploadsEnabled: boolean
  onChange: ObjectChange
  /** Changes how long every lock in the room waits after a wrong answer. */
  onCooldownChange: (seconds: number) => void
  /** Ends the current undo step, e.g. when a field loses focus. */
  onSeal: () => void
  onRemove: () => void
  onClose: () => void
  /** Adds a scene and returns its id, for a door that should lead to a new scene. */
  onAddScene: () => string
}

/**
 * The small panel for the selected object. It shows only what matters for the object's type: a lock's graphic and
 * puzzle, a letter's text, where a door leads and when it opens.
 */
export function Inspector({
  document,
  object,
  sceneId,
  uploadsEnabled,
  onChange,
  onCooldownChange,
  onSeal,
  onRemove,
  onClose,
  onAddScene,
}: InspectorProps) {
  const { t } = useTranslation()
  const isDoorway = object.type === 'Door' || object.type === 'ExitDoor'

  return (
    <section className="inspector" aria-labelledby="inspector-title">
      <div className="panel-heading">
        <h2 id="inspector-title">{objectLabel(t, document, object)}</h2>
        <button type="button" className="quiet" onClick={onClose}>
          {t('editor.inspector.done')}
        </button>
      </div>

      {object.type === 'Lock' && (
        <>
          <LookChooser lock={object} onChange={onChange} />
          <PuzzleEditor lock={object} uploadsEnabled={uploadsEnabled} onChange={onChange} onSeal={onSeal} />
          {object.steps.length > 0 && (
            <CooldownSelect seconds={document.wrongAnswerCooldownSeconds} onChange={onCooldownChange} />
          )}
        </>
      )}
      {object.type === 'Letter' && <LetterFields letter={object} onChange={onChange} onSeal={onSeal} />}
      {object.type === 'Door' && (
        <DoorFields document={document} door={object} sceneId={sceneId} onChange={onChange} onAddScene={onAddScene} />
      )}
      {object.type === 'ExitDoor' && <p className="field-hint">{t('editor.exitDoor.explanation')}</p>}
      {isDoorway && <OpenConditionFields document={document} doorway={object} sceneId={sceneId} onChange={onChange} />}
      {object.type === 'TextBox' && <TextBoxFields textBox={object} onChange={onChange} onSeal={onSeal} />}

      {!isDoorway && <UnlockedBySelect document={document} object={object} onChange={onChange} />}

      <button type="button" className="danger" onClick={onRemove}>
        {t(`editor.inspector.remove${object.type}`)}
      </button>
    </section>
  )
}

/** The lock's graphic, from the asset library. Changing it never touches the puzzle. */
function LookChooser({ lock, onChange }: { lock: EditorLock; onChange: ObjectChange }) {
  const { t } = useTranslation()
  return (
    <fieldset>
      <legend>{t('editor.lock.look')}</legend>
      <div className="look-choices">
        {assetsOfType('Lock').map((asset) => (
          <label key={asset.key} className="look-choice">
            <input
              type="radio"
              name={`look-${lock.id}`}
              checked={lock.graphicKey === asset.key}
              onChange={() => {
                onChange<EditorLock>((l) => ({ ...l, graphicKey: asset.key }))
              }}
            />
            <ObjectGraphic asset={asset} open={false} />
            <span>{t(asset.nameKey)}</span>
          </label>
        ))}
      </div>
    </fieldset>
  )
}

/** The usual choices for the wait after a wrong answer, in seconds; 0 turns it off. */
const cooldownChoices = [0, 5, 10, 15, 30, 60]

/** How long a lock stays locked for the team after a wrong answer. It is the same for every lock in the room. */
function CooldownSelect({ seconds, onChange }: { seconds: number; onChange: (seconds: number) => void }) {
  const { t } = useTranslation()
  const choices = cooldownChoices.includes(seconds) ? cooldownChoices : [...cooldownChoices, seconds].sort((a, b) => a - b)
  return (
    <Field label={t('editor.cooldown.label')} hint={t('editor.cooldown.hint')}>
      {(id) => (
        <select
          id={id}
          value={seconds}
          onChange={(event) => {
            onChange(Number(event.target.value))
          }}
        >
          {choices.map((choice) => (
            <option key={choice} value={choice}>
              {choice === 0 ? t('editor.cooldown.off') : t('editor.cooldown.seconds', { count: choice })}
            </option>
          ))}
        </select>
      )}
    </Field>
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

const openConditions: DoorOpenCondition[] = ['AllInScene', 'SelectedLocks']

/**
 * When a door (or the exit door) opens by itself for the team: once every puzzle in its scene is solved (the default),
 * or once the locks the teacher ticks are solved.
 */
function OpenConditionFields({
  document,
  doorway,
  sceneId,
  onChange,
}: {
  document: RoomDocument
  doorway: EditorDoorway
  sceneId: string
  onChange: ObjectChange
}) {
  const { t } = useTranslation()
  const locks = lockChoices(document, sceneId)

  const lockName = (lock: EditorLock) => {
    const name = objectLabel(t, document, lock)
    const scene = findObject(document, lock.id)?.scene
    return scene === undefined || scene.id === sceneId ? name : t('editor.doorway.inScene', { name, scene: scene.name })
  }

  return (
    <fieldset className="open-condition">
      <legend>{t(doorway.type === 'ExitDoor' ? 'editor.doorway.exitCondition' : 'editor.doorway.condition')}</legend>
      {openConditions.map((condition) => (
        <label key={condition} className="answer-type">
          <input
            type="radio"
            name={`open-${doorway.id}`}
            checked={doorway.openCondition === condition}
            onChange={() => {
              onChange<EditorDoorway>((d) => ({ ...d, openCondition: condition }))
            }}
          />
          <span className="answer-type-name">{t(`editor.doorway.conditions.${condition}`)}</span>
        </label>
      ))}
      {doorway.openCondition === 'SelectedLocks' &&
        (locks.length === 0 ? (
          <p className="field-hint">{t('editor.doorway.noLocks')}</p>
        ) : (
          <fieldset className="required-locks">
            <legend>{t('editor.doorway.locks')}</legend>
            {locks.map((lock) => (
              <label key={lock.id} className="checkbox">
                <input
                  type="checkbox"
                  checked={doorway.requiredLockIds.includes(lock.id)}
                  onChange={(event) => {
                    const checked = event.target.checked
                    onChange<EditorDoorway>((d) => ({
                      ...d,
                      requiredLockIds: checked
                        ? [...d.requiredLockIds.filter((id) => id !== lock.id), lock.id]
                        : d.requiredLockIds.filter((id) => id !== lock.id),
                    }))
                  }}
                />
                {lockName(lock)}
              </label>
            ))}
          </fieldset>
        ))}
      <p className="field-hint">{t('editor.doorway.hint')}</p>
    </fieldset>
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
