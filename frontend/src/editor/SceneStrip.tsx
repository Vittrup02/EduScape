import { useTranslation } from 'react-i18next'
import type { RoomDocument } from '../api/editor'
import { backgroundImage } from '../assets/sceneLayout'
import { SortableList } from '../sortable/SortableList'

interface SceneStripProps {
  document: RoomDocument
  currentSceneId: string
  onOpen: (sceneId: string) => void
  onAdd: () => void
  onRemove: (sceneId: string) => void
  /** Moves a scene in the room's order. */
  onMove: (from: number, to: number) => void
}

/**
 * The room's scenes under the stage, in the room's order: open one to edit it, drag it to another place, add a new
 * one, or remove one with the cross in its corner (which can be undone). The last scene has no cross.
 */
export function SceneStrip({ document, currentSceneId, onOpen, onAdd, onRemove, onMove }: SceneStripProps) {
  const { t } = useTranslation()
  const canRemove = document.scenes.length > 1
  const sceneName = (name: string) => (name.trim() === '' ? t('editor.scene.unnamed') : name)

  return (
    <nav className="scene-strip" aria-label={t('editor.scenes')}>
      <SortableList
        items={document.scenes}
        label={t('editor.scenes')}
        orientation="horizontal"
        getKey={(scene) => scene.id}
        itemLabel={(scene) => sceneName(scene.name)}
        onMove={onMove}
        renderItem={(scene, _index, handle) => {
          const background = backgroundImage(scene.backgroundImageKey, scene.backgroundImageUrl)
          const name = sceneName(scene.name)
          return (
            <div className="scene-strip-item" data-dragging={handle.isDragging || undefined}>
              {/* The tab is what the scene is dragged by; a click without moving opens it. */}
              <button
                type="button"
                className="scene-tab"
                aria-current={scene.id === currentSceneId || undefined}
                {...handle.dragProps}
                onClick={() => {
                  onOpen(scene.id)
                }}
              >
                {background !== undefined && <img src={background} alt="" draggable={false} />}
                <span className="scene-tab-name">{name}</span>
                <span className="scene-tab-badges">
                  {scene.id === document.startSceneId && <span className="scene-tab-start">{t('editor.scene.startBadge')}</span>}
                  {scene.narration.trim() !== '' && <span className="scene-tab-story">{t('editor.scene.storyBadge')}</span>}
                </span>
              </button>
              {canRemove && (
                <button
                  type="button"
                  className="scene-tab-remove"
                  aria-label={t('editor.scene.removeNamed', { name })}
                  title={t('editor.scene.removeNamed', { name })}
                  onClick={() => {
                    onRemove(scene.id)
                  }}
                >
                  <span aria-hidden="true">×</span>
                </button>
              )}
            </div>
          )
        }}
        footer={
          <button type="button" className="scene-tab add-scene" onClick={onAdd}>
            {t('editor.scene.add')}
          </button>
        }
      />
    </nav>
  )
}
