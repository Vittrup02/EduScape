import { useTranslation } from 'react-i18next'
import type { RoomDocument } from '../api/editor'
import { backgroundImage } from '../assets/sceneLayout'

interface SceneStripProps {
  document: RoomDocument
  currentSceneId: string
  onOpen: (sceneId: string) => void
  onAdd: () => void
  onRemove: (sceneId: string) => void
}

/**
 * The room's scenes under the stage: open one to edit it, add a new one, or remove one with the cross in its
 * corner (which can be undone). The last scene has no cross.
 */
export function SceneStrip({ document, currentSceneId, onOpen, onAdd, onRemove }: SceneStripProps) {
  const { t } = useTranslation()
  const canRemove = document.scenes.length > 1

  return (
    <nav className="scene-strip" aria-label={t('editor.scenes')}>
      <ul>
        {document.scenes.map((scene) => {
          const background = backgroundImage(scene.backgroundImageKey, scene.backgroundImageUrl)
          const name = scene.name.trim() === '' ? t('editor.scene.unnamed') : scene.name
          return (
            <li key={scene.id} className="scene-strip-item">
              <button
                type="button"
                className="scene-tab"
                aria-current={scene.id === currentSceneId || undefined}
                onClick={() => {
                  onOpen(scene.id)
                }}
              >
                {background !== undefined && <img src={background} alt="" draggable={false} />}
                <span className="scene-tab-name">{name}</span>
                {scene.id === document.startSceneId && <span className="scene-tab-start">{t('editor.scene.startBadge')}</span>}
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
            </li>
          )
        })}
        <li>
          <button type="button" className="scene-tab add-scene" onClick={onAdd}>
            {t('editor.scene.add')}
          </button>
        </li>
      </ul>
    </nav>
  )
}
