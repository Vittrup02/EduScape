import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import type { EditorObject, EditorRoom, RoomDocument } from '../api/editor'
import { subjects, type Subject } from '../api/teacher'
import type { ObjectAsset } from '../assets/registry'
import {
  addObject,
  addScene,
  createObject,
  createScene,
  findObject,
  findScene,
  moveObject,
  removeObject,
  removeScene,
  setRoomDetails,
  setStartScene,
  updateObject,
  updateScene,
} from './editorDocument'
import { GradeLevelSelect } from './GradeLevelSelect'
import { useHistory } from './history'
import { Inspector, type ObjectChange } from './Inspector'
import { objectLabel } from './objectLabel'
import { PreviewButton } from './PreviewButton'
import { SceneMap } from './SceneMap'
import { ScenePanel } from './ScenePanel'
import { SceneStrip } from './SceneStrip'
import { Stage } from './Stage'
import type { StagePoint } from './stagePoint'
import { Toolbox } from './Toolbox'
import { useAutosave, type SaveStatus } from './useAutosave'
import { ValidationList } from './ValidationList'

function toDocument(room: EditorRoom): RoomDocument {
  return {
    title: room.title,
    subject: room.subject,
    gradeLevel: room.gradeLevel,
    startSceneId: room.startSceneId,
    scenes: room.scenes,
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
  const history = useHistory(toDocument(room))
  const document = history.present
  const autosave = useAutosave(room.id, document, room)
  const [sceneId, setSceneId] = useState(room.startSceneId)
  const [selectedId, setSelectedId] = useState<string | null>(null)
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

  const add = (asset: ObjectAsset, at: StagePoint | null) => {
    // Several clicks in a row fan out a little instead of stacking exactly on top of each other.
    const offset = (scene.objects.length % 5) * 4
    const object = createObject(asset, at?.x ?? 40 + offset, at?.y ?? 45 + offset, document, scene.id)
    apply((d) => addObject(d, scene.id, object))
    setSelectedId(object.id)
  }

  const newScene = (): string => {
    const created = createScene(document, t('editor.scene.defaultName', { n: document.scenes.length + 1 }))
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
        <PreviewButton roomId={room.id} flush={autosave.flush} />
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
          />
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
              onBackground={(backgroundImageKey, backgroundImageUrl) => {
                apply((d) => updateScene(d, scene.id, { backgroundImageKey, backgroundImageUrl }))
              }}
              onMakeStart={() => {
                apply((d) => setStartScene(d, scene.id))
              }}
              onRemove={() => {
                apply((d) => removeScene(d, scene.id))
              }}
              onSeal={seal}
            />
          ) : (
            <Inspector
              key={selected.object.id}
              document={document}
              object={selected.object}
              sceneId={selected.scene.id}
              onChange={changeSelected}
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
