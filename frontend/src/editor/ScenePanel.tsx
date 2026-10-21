import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { uploadContentTypes, type EditorScene } from '../api/editor'
import { assetsOfType } from '../assets/registry'
import { errorKey } from '../i18n/errors'
import { useImageUpload } from './useImageUpload'

const backgrounds = assetsOfType('Background')

interface ScenePanelProps {
  scene: EditorScene
  isStart: boolean
  canRemove: boolean
  uploadsEnabled: boolean
  onRename: (name: string) => void
  onNarration: (narration: string) => void
  onBackground: (key: string, url: string | null) => void
  onMakeStart: () => void
  onRemove: () => void
  onSeal: () => void
}

/** The current scene's settings, shown while no object is selected. */
export function ScenePanel({
  scene,
  isStart,
  canRemove,
  uploadsEnabled,
  onRename,
  onNarration,
  onBackground,
  onMakeStart,
  onRemove,
  onSeal,
}: ScenePanelProps) {
  const { t } = useTranslation()
  const nameId = useId()
  const narrationId = useId()
  const uploadId = useId()
  const { upload, uploading, errorCode: uploadError } = useImageUpload((image) => {
    onBackground(image.imageKey, image.imageUrl)
  })
  const isUploaded = backgrounds.every((b) => b.key !== scene.backgroundImageKey)

  return (
    <section className="scene-panel" aria-labelledby="scene-panel-title">
      <h2 id="scene-panel-title" className="panel-heading">
        {t('editor.scene.title')}
      </h2>
      <div className="field">
        <label htmlFor={nameId}>{t('editor.scene.name')}</label>
        <input
          id={nameId}
          value={scene.name}
          aria-invalid={scene.name.trim() === '' || undefined}
          onChange={(event) => {
            onRename(event.target.value)
          }}
          onBlur={onSeal}
        />
        {scene.name.trim() === '' && <p className="error">{t('editor.scene.nameRequired')}</p>}
      </div>

      <div className="field">
        <label htmlFor={narrationId}>{t('editor.scene.narration')}</label>
        <textarea
          id={narrationId}
          rows={4}
          value={scene.narration}
          placeholder={t('editor.scene.narrationPlaceholder')}
          onChange={(event) => {
            onNarration(event.target.value)
          }}
          onBlur={onSeal}
        />
        <p className="field-hint">{t('editor.scene.narrationHint')}</p>
      </div>

      <fieldset className="backgrounds">
        <legend>{t('editor.scene.background')}</legend>
        {backgrounds.map((background) => (
          <label key={background.key} className="background-choice">
            <input
              type="radio"
              name={`background-${scene.id}`}
              checked={scene.backgroundImageKey === background.key}
              onChange={() => {
                onBackground(background.key, null)
              }}
            />
            <img src={background.files.image} alt="" draggable={false} />
            <span>{t(background.nameKey)}</span>
          </label>
        ))}
        {isUploaded && scene.backgroundImageUrl !== null && (
          <label className="background-choice">
            <input type="radio" name={`background-${scene.id}`} checked readOnly />
            <img src={scene.backgroundImageUrl} alt="" draggable={false} />
            <span>{t('editor.scene.ownImage')}</span>
          </label>
        )}
      </fieldset>

      {uploadsEnabled && (
        <div className="field">
          <label htmlFor={uploadId} className="button upload-button" data-busy={uploading || undefined}>
            {uploading ? t('editor.scene.uploading') : t('editor.scene.upload')}
          </label>
          <input
            id={uploadId}
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
          <p className="field-hint">{t('editor.scene.uploadHint')}</p>
          {uploadError !== null && (
            <p role="alert" className="error">
              {t(errorKey(uploadError))}
            </p>
          )}
        </div>
      )}

      <div className="actions">
        {isStart ? (
          <p className="start-note">{t('editor.scene.isStart')}</p>
        ) : (
          <button type="button" onClick={onMakeStart}>
            {t('editor.scene.makeStart')}
          </button>
        )}
        {canRemove && (
          <button type="button" className="danger" onClick={onRemove}>
            {t('editor.scene.remove')}
          </button>
        )}
      </div>
    </section>
  )
}
