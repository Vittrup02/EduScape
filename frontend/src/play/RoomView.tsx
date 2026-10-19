import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ObjectGraphic } from '../assets/ObjectGraphic'
import { assetsOfType, findAsset, type ObjectAsset, type ObjectAssetType } from '../assets/registry'
import { ApiError } from '../api/client'
import { openDoor, openExitDoor, type PlayObject, type PlayState } from '../api/play'
import { errorKey } from '../i18n/errors'
import { LockDialog } from './LockDialog'
import { Modal } from './Modal'
import {
  canOpenExitDoor,
  findScene,
  isAvailable,
  isCompleted,
  revealedHint,
  unlockedScenes,
  type TeamEvent,
} from './progress'

/** Object height in percent of the scene's height; doors stand taller than locks and letters. */
const objectHeight: Record<ObjectAssetType, number> = { Lock: 16, Letter: 14, Door: 34, ExitDoor: 34 }

/** Scenes are 16:9, like the built-in backgrounds. */
const sceneAspectRatio = 16 / 9

function percent(value: number): string {
  return `${String(value)}%`
}

/** The object's built-in graphic. Unknown keys (e.g. uploads, which can't be shown yet) get the type's default. */
function objectAsset(object: PlayObject): ObjectAsset {
  const asset = findAsset(object.graphicKey)
  return asset !== undefined && asset.type === object.type ? asset : assetsOfType(object.type)[0]
}

function backgroundImage(key: string): string | undefined {
  const asset = findAsset(key)
  return asset?.type === 'Background' ? asset.files.image : undefined
}

interface RoomViewProps {
  state: PlayState
  deviceToken: string
  /** False while reconnecting: the room stays visible, but nothing can be done. */
  online: boolean
  onEvent: (event: TeamEvent) => void
}

/**
 * The scene the student is in, with its objects placed on the background. Progress belongs to the team; which
 * scene the student looks at is the student's own choice among the team's unlocked scenes.
 */
export function RoomView({ state, deviceToken, online, onEvent }: RoomViewProps) {
  const { t } = useTranslation()
  const { room, team } = state
  const [sceneId, setSceneId] = useState(room.startSceneId)
  const [openObjectId, setOpenObjectId] = useState<string | null>(null)
  const [readLetterIds, setReadLetterIds] = useState<ReadonlySet<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const scene = findScene(room, sceneId)
  if (scene === undefined) {
    return (
      <p role="alert" className="error">
        {t('play.noRoom')}
      </p>
    )
  }

  const scenes = unlockedScenes(room, team)
  const visibleObjects = scene.objects.filter((o) => isAvailable(room, team, o))
  const openObject = visibleObjects.find((o) => o.id === openObjectId)

  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    setErrorCode(null)
    try {
      await action()
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
    } finally {
      setBusy(false)
    }
  }

  const isOpen = (object: PlayObject): boolean => {
    switch (object.type) {
      case 'Letter':
        return readLetterIds.has(object.id)
      case 'ExitDoor':
        return canOpenExitDoor(room, team, object)
      default:
        return isCompleted(object, team)
    }
  }

  const label = (object: PlayObject): string => {
    switch (object.type) {
      case 'Lock':
        return t(isOpen(object) ? 'play.object.lockOpen' : 'play.object.lock', { question: object.question })
      case 'Letter':
        return t('play.object.letter')
      case 'Door': {
        const target = findScene(room, object.targetSceneId)?.name ?? ''
        return t(isOpen(object) ? 'play.object.doorOpen' : 'play.object.door', { scene: target })
      }
      case 'ExitDoor':
        return t('play.object.exitDoor')
    }
  }

  const activate = (object: PlayObject) => {
    setErrorCode(null)
    switch (object.type) {
      case 'Lock':
        setOpenObjectId(object.id)
        break
      case 'Letter':
        setOpenObjectId(object.id)
        setReadLetterIds((ids) => new Set(ids).add(object.id))
        break
      case 'Door': {
        const target = object.targetSceneId
        if (target === null) {
          break
        }
        if (isCompleted(object, team)) {
          setSceneId(target)
          break
        }
        void run(async () => {
          await openDoor(deviceToken, object.id)
          onEvent({ type: 'doorOpened', doorId: object.id })
          setSceneId(target)
        })
        break
      }
      case 'ExitDoor':
        void run(async () => {
          await openExitDoor(deviceToken, object.id)
          onEvent({ type: 'teamEscaped' })
        })
        break
    }
  }

  const closeDialog = () => {
    setOpenObjectId(null)
  }
  const background = backgroundImage(scene.backgroundImageKey)

  return (
    <>
      {scenes.length > 1 && (
        <nav className="scene-nav" aria-label={t('play.scenes')}>
          {scenes.map((s) => (
            <button
              key={s.id}
              type="button"
              aria-current={s.id === scene.id || undefined}
              onClick={() => {
                setSceneId(s.id)
                setErrorCode(null)
              }}
            >
              {s.name}
            </button>
          ))}
        </nav>
      )}
      <section className="scene" aria-label={scene.name}>
        {background !== undefined && <img className="scene-background" src={background} alt="" draggable={false} />}
        {visibleObjects.map((object) => {
          const asset = objectAsset(object)
          const height = objectHeight[object.type]
          return (
            <button
              key={object.id}
              type="button"
              className="room-object"
              aria-label={label(object)}
              disabled={!online || busy}
              style={{
                left: percent(object.x),
                top: percent(object.y),
                width: percent((height * asset.aspectRatio) / sceneAspectRatio),
              }}
              onClick={() => {
                activate(object)
              }}
            >
              <ObjectGraphic asset={asset} open={isOpen(object)} />
            </button>
          )
        })}
      </section>
      {errorCode !== null && (
        <p role="alert" className="error">
          {t(errorKey(errorCode))}
        </p>
      )}
      {openObject?.type === 'Lock' && (
        <LockDialog
          lock={openObject}
          solved={isCompleted(openObject, team)}
          hint={revealedHint(team, openObject.id)}
          deviceToken={deviceToken}
          online={online}
          onEvent={onEvent}
          onClose={closeDialog}
        />
      )}
      {openObject?.type === 'Letter' && (
        <Modal title={t('play.letter.title')} onClose={closeDialog}>
          <p className="letter-text">{openObject.text}</p>
        </Modal>
      )}
    </>
  )
}
