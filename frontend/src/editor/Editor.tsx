import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import type { EditorObject, EditorRoom, RoomDocument } from '../api/editor'
import { subjects, type Subject } from '../api/teacher'

import {
  addObject,
  addScene,
  createObject,
  createTextBox,
  createScene,
  findObject,
  findScene,
  moveObject,
  removeObject,
  moveScene,
  removeScene,
  setRoomDetails,
  setStartScene,
  updateObject,
  updateScene,
} from './editorDocument'
import { GradeLevelSelect } from './GradeLevelSelect'
import { useHistory } from './history'
import type { ObjectChange } from './Field'
import { Inspector } from './Inspector'
import { objectLabel } from './objectLabel'
import { PreviewButton } from './PreviewButton'
import { SceneMap } from './SceneMap'
import { ScenePanel } from './ScenePanel'
import { SceneStrip } from './SceneStrip'
import { Stage } from './Stage'
import type { StagePoint } from './stagePoint'
import { Toolbox, type ToolboxItem } from './Toolbox'
import { useAutosave, type SaveStatus } from './useAutosave'
import { ValidationList } from './ValidationList'

function toDocument(room: EditorRoom): RoomDocument {
  return {
    title: room.title,
    subject: room.subject,
    gradeLevel: room.gradeLevel,
    startSceneId: room.startSceneId,
    scenes: room.scenes,
    wrongAnswerCooldownSeconds: room.wrongAnswerCooldownSeconds,
  }
}

const statusKeys: Record<SaveStatus, `editor.save.${SaveStatus}`> = {
  saved: 'editor.save.saved',
  pending: 'editor.save.pending',
  saving: 'editor.save.saving',
  waiting: 'editor.save.waiting',
  retrying: 'editor.save.retrying',
  conflict: 'editor.save.conflict',
}

/**
 * The room editor: stickers on the left, the scene in the middle, and on the right a small panel for whatever is
 * selected, the map of scenes and what is still missing. Every change can be undone, and is saved by itself.
 */
export function Editor({ room }: { room: EditorRoom }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const history = useHistory(toDocument(room))
  const document = history.present
  const autosave = useAutosave(room.id, document, room)
  const [sceneId, setSceneId] = useState(room.startSceneId)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  // Shown right after a scene is removed, until anything else changes the room.
  const [removedScene, setRemovedScene] = useState<{ name: string; document: RoomDocument } | null>(null)
  const stageRef = useRef<HTMLElement | null>(null)
  const titleId = useId()
  const subjectId = useId()
  const gradeId = useId()
  const { apply, undo, redo, seal } = history

  // Undo can bring back a removed scene or remove the one on screen; fall back to the start scene.
  const scene = findScene(document, sceneId) ?? findScene(document, document.startSceneId) ?? document.scenes[0]
  const selected = selectedId === null ? undefined : findObject(document, selectedId)

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey)) {
        return
      }
      const key = event.key.toLowerCase()
      if (key === 'z' || key === 'y') {
        event.preventDefault()
        if (key === 'y' || event.shiftKey) {
          redo()
        } else {
          undo()
        }
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [undo, redo])

  const openScene = (id: string) => {
    seal()
    setSceneId(id)
    setSelectedId(null)
  }

  const showObject = (objectId: string) => {
    const found = findObject(document, objectId)
    if (found !== undefined) {
      setSceneId(found.scene.id)
      setSelectedId(objectId)
    }
  }

  const add = (item: ToolboxItem, at: StagePoint | null) => {
    // Several clicks in a row fan out a little instead of stacking exactly on top of each other.
    const offset = (scene.objects.length % 5) * 4
    const x = at?.x ?? 40 + offset
    const y = at?.y ?? 45 + offset
    const object = item.kind === 'asset' ? createObject(item.asset, x, y, document, scene.id) : createTextBox(x, y)
    apply((d) => addObject(d, scene.id, object))
    setSelectedId(object.id)
  }

  const newScene = (): string => {
    const created = createScene(document, (n) => t('editor.scene.defaultName', { n }))
    apply((d) => addScene(d, created))
    return created.id
  }

  const changeSelected: ObjectChange = useCallback(
    (update, field) => {
      if (selectedId !== null) {
        apply((d) => updateObject(d, selectedId, update), field === undefined ? undefined : `${selectedId}:${field}`)
      }
    },
    [apply, selectedId],
  )

  const removeSceneWithUndo = (sceneId: string) => {
    const removed = findScene(document, sceneId)
    const next = removeScene(document, sceneId)
    if (removed === undefined || next === document) {
      return
    }
    apply(() => next)
    setRemovedScene({ name: removed.name, document: next })
    if (selected?.scene.id === sceneId) {
      setSelectedId(null)
    }
  }

  const remove = (objectId: string) => {
    apply((d) => removeObject(d, objectId))
    setSelectedId(null)
  }

  const label = (object: EditorObject) => objectLabel(t, document, object)

  return (
    <main className="editor">
      <div className="editor-bar">
        <Link to="/teacher" className="back-link">
          {t('editor.back')}
        </Link>
        <div className="room-details-form">
          <label htmlFor={titleId} className="visually-hidden">
            {t('editor.title')}
          </label>
          <input
            id={titleId}
            className="room-title-input"
            value={document.title}
            placeholder={t('editor.titlePlaceholder')}
            aria-invalid={document.title.trim() === '' || undefined}
            onChange={(event) => {
              apply((d) => setRoomDetails(d, { title: event.target.value }), 'title')
            }}
            onBlur={seal}
          />
          <label htmlFor={subjectId} className="visually-hidden">
            {t('editor.subject')}
          </label>
          <select
            id={subjectId}
            value={document.subject}
            onChange={(event) => {
              apply((d) => setRoomDetails(d, { subject: event.target.value as Subject }))
            }}
          >
            {subjects.map((subject) => (
              <option key={subject} value={subject}>
                {t(`subjects.${subject}`)}
              </option>
            ))}
          </select>
          <label htmlFor={gradeId} className="visually-hidden">
            {t('editor.gradeLevel')}
          </label>
          <GradeLevelSelect
            id={gradeId}
            value={document.gradeLevel}
            onChange={(gradeLevel) => {
              apply((d) => setRoomDetails(d, { gradeLevel }))
            }}
          />
        </div>
        {/* Once nothing is missing, the teacher can go straight on to the class; the last change is saved first. */}
        {autosave.validationErrors.length === 0 && (
          <button
            type="button"
            onClick={() =>
              void autosave.flush().then(async (saved) => {
                if (saved) {
                  await navigate(`/teacher/rooms/${room.id}/start`)
                }
              })
            }
          >
            {t('teacher.startSession')}
          </button>
        )}
        <PreviewButton
          roomId={room.id}
          scene={scene}
          isStartScene={scene.id === document.startSceneId}
          flush={autosave.flush}
        />
        {document.title.trim() === '' && <p className="error">{t('editor.titleRequired')}</p>}
        {autosave.status === 'conflict' && (
          <p role="alert" className="error">
            {t('errors.ROOM_CHANGED')}{' '}
            <button
              type="button"
              onClick={() => {
                window.location.reload()
              }}
            >
              {t('editor.reload')}
            </button>
          </p>
        )}
      </div>

      <div className="workbench">
        <Toolbox stageRef={stageRef} onAdd={add} />

        <div className="stage-column">
          <div className="stage-toolbar">
            <button type="button" onClick={undo} disabled={!history.canUndo} aria-keyshortcuts="Control+Z">
              {t('editor.undo')}
            </button>
            <button type="button" onClick={redo} disabled={!history.canRedo} aria-keyshortcuts="Control+Y">
              {t('editor.redo')}
            </button>
            <p role="status" className="save-status" data-status={autosave.status}>
              {t(statusKeys[autosave.status])}
            </p>
          </div>
          <Stage
            stageRef={stageRef}
            scene={scene}
            selectedId={selectedId}
            label={label}
            onSelect={setSelectedId}
            onMove={(objectId, x, y, step) => {
              apply((d) => moveObject(d, objectId, x, y), step)
            }}
            onMoveEnd={seal}
            onRemove={remove}
          />
          <SceneStrip
            document={document}
            currentSceneId={scene.id}
            onOpen={openScene}
            onAdd={() => {
              openScene(newScene())
            }}
            onRemove={removeSceneWithUndo}
            onMove={(from, to) => {
              apply((d) => moveScene(d, from, to))
            }}
          />
          {removedScene?.document === document && (
            <p role="status" className="undo-note">
              {t('editor.scene.removed', { name: removedScene.name })}{' '}
              <button type="button" className="quiet" onClick={undo}>
                {t('editor.undo')}
              </button>
            </p>
          )}
        </div>

        <aside className="panel">
          {selected === undefined ? (
            <ScenePanel
              key={scene.id}
              scene={scene}
              isStart={scene.id === document.startSceneId}
              canRemove={document.scenes.length > 1}
              uploadsEnabled={room.uploadsEnabled}
              onRename={(name) => {
                apply((d) => updateScene(d, scene.id, { name }), `${scene.id}:name`)
              }}
              onNarration={(narration) => {
                apply((d) => updateScene(d, scene.id, { narration }), `${scene.id}:narration`)
              }}
              onBackground={(backgroundImageKey, backgroundImageUrl) => {
                apply((d) => updateScene(d, scene.id, { backgroundImageKey, backgroundImageUrl }))
              }}
              onMakeStart={() => {
                apply((d) => setStartScene(d, scene.id))
              }}
              onRemove={() => {
                removeSceneWithUndo(scene.id)
              }}
              onSeal={seal}
            />
          ) : (
            <Inspector
              key={selected.object.id}
              document={document}
              object={selected.object}
              sceneId={selected.scene.id}
              uploadsEnabled={room.uploadsEnabled}
              onChange={changeSelected}
              onCooldownChange={(wrongAnswerCooldownSeconds) => {
                apply((d) => setRoomDetails(d, { wrongAnswerCooldownSeconds }))
              }}
              onSeal={seal}
              onRemove={() => {
                remove(selected.object.id)
              }}
              onClose={() => {
                setSelectedId(null)
              }}
              onAddScene={newScene}
            />
          )}
          <SceneMap document={document} currentSceneId={scene.id} onOpen={openScene} />
          <ValidationList document={document} errors={autosave.validationErrors} onShow={showObject} />
        </aside>
      </div>
    </main>
  )
}
