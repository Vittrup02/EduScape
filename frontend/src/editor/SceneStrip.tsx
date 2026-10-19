import { useTranslation } from 'react-i18next'
import type { RoomDocument } from '../api/editor'
import { backgroundImage } from '../assets/sceneLayout'

interface SceneStripProps {
  document: RoomDocument
  currentSceneId: string
  onOpen: (sceneId: string) => void
  onAdd: () => void
}

/** The room's scenes under the stage: open one to edit it, or add a new one. */
export function SceneStrip({ document, currentSceneId, onOpen, onAdd }: SceneStripProps) {
  const { t } = useTranslation()

  return (
    <nav className="scene-strip" aria-label={t('editor.scenes')}>
      <ul>
        {document.scenes.map((scene) => {
          const background = backgroundImage(scene.backgroundImageKey, scene.backgroundImageUrl)
          return (
            <li key={scene.id}>
              <button
                type="button"
                className="scene-tab"
                aria-current={scene.id === currentSceneId || undefined}
                onClick={() => {
                  onOpen(scene.id)
                }}
              >
                {background !== undefined && <img src={background} alt="" draggable={false} />}
                <span className="scene-tab-name">{scene.name.trim() === '' ? t('editor.scene.unnamed') : scene.name}</span>
                {scene.id === document.startSceneId && <span className="scene-tab-start">{t('editor.scene.startBadge')}</span>}
              </button>
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
