import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ObjectGraphic } from '../assets/ObjectGraphic'
import { backgroundImage, objectAsset, objectStyle, textBoxStyle } from '../assets/sceneLayout'
import { ApiError } from '../api/client'
import { openExitDoor, reportScene, type PlayObject, type PlayState, type PlayTextBox } from '../api/play'
import { errorKey } from '../i18n/errors'
import { LockDialog } from './LockDialog'
import { Modal } from './Modal'
import {
  canOpenExitDoor,
  currentStep,
  findScene,
  isAvailable,
  isCompleted,
  isSceneUnlocked,
  unlockedScenes,
  type TeamEvent,
} from './progress'

/** Everything on a scene that students click; text boxes are only read. */
type ClickableObject = Exclude<PlayObject, PlayTextBox>

interface RoomViewProps {
  state: PlayState
  deviceToken: string
  /** False while reconnecting: the room stays visible, but nothing can be done. */
  online: boolean
  onEvent: (event: TeamEvent) => void
}

/**
 * The scene the student is in, with its objects placed on the background. Progress belongs to the team; which
 * scene the student looks at is the student's own choice among the team's unlocked scenes. Doors open by themselves
 * when the team meets their condition (every device animates it); the student then walks through by clicking, so
 * nobody is moved away in the middle of a puzzle.
 */
export function RoomView({ state, deviceToken, online, onEvent }: RoomViewProps) {
  const { t } = useTranslation()
  const { room, team } = state
  // Back where the student was before a reload, or where a teacher's preview starts.
  const [chosenSceneId, setSceneId] = useState(state.currentSceneId ?? room.startSceneId)
  // Said quietly when a student tries a door that is still shut; it is not an error.
  const [closedDoorId, setClosedDoorId] = useState<string | null>(null)
  // After the teacher moves the student to another team, that team may not have unlocked the scene yet.
  const sceneId =
    chosenSceneId !== null && isSceneUnlocked(room, team, chosenSceneId) ? chosenSceneId : room.startSceneId
  const [openObjectId, setOpenObjectId] = useState<string | null>(null)
  const [readLetterIds, setReadLetterIds] = useState<ReadonlySet<string>>(new Set())
  // Scenes whose story the student has read on this device; the story opens again on request.
  const [readNarrationIds, setReadNarrationIds] = useState<ReadonlySet<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  // The teacher's dashboard shows where each student is. Reported again after every reconnect and team change;
  // a failed report only means the dashboard is a little behind.
  useEffect(() => {
    if (online && sceneId !== null) {
      reportScene(deviceToken, sceneId).catch(() => undefined)
    }
  }, [deviceToken, sceneId, online, team.id])

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

  const isOpen = (object: ClickableObject): boolean => {
    switch (object.type) {
      case 'Letter':
        return readLetterIds.has(object.id)
      case 'ExitDoor':
        return canOpenExitDoor(room, team, object)
      default:
        return isCompleted(object, team)
    }
  }

  const label = (object: ClickableObject): string => {
    switch (object.type) {
      case 'Lock': {
        // Named by the step the team is on, so the label follows the puzzle.
        const step = object.steps.at(Math.min(currentStep(team, object), object.steps.length - 1))
        return t(isOpen(object) ? 'play.object.lockOpen' : 'play.object.lock', { question: step?.question ?? '' })
      }
      case 'Letter':
        return t('play.object.letter')
      case 'Door': {
        const target = findScene(room, object.targetSceneId)?.name ?? ''
        return t(isOpen(object) ? 'play.object.doorOpen' : 'play.object.door', { scene: target })
      }
      case 'ExitDoor':
        return t(isOpen(object) ? 'play.object.exitDoorOpen' : 'play.object.exitDoor')
    }
  }

  const activate = (object: ClickableObject) => {
    setErrorCode(null)
    setClosedDoorId(null)
    switch (object.type) {
      case 'Lock':
        setOpenObjectId(object.id)
        break
      case 'Letter':
        setOpenObjectId(object.id)
        setReadLetterIds((ids) => new Set(ids).add(object.id))
        break
      case 'Door':
        if (!isCompleted(object, team)) {
          setClosedDoorId(object.id)
        } else if (object.targetSceneId !== null) {
          setSceneId(object.targetSceneId)
        }
        break
      case 'ExitDoor':
        if (!canOpenExitDoor(room, team, object)) {
          setClosedDoorId(object.id)
          break
        }
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
  const background = backgroundImage(scene.backgroundImageKey, scene.backgroundImageUrl)
  const narration = scene.narration?.trim() ? scene.narration : null
  const showNarration = narration !== null && !readNarrationIds.has(scene.id)
  const setNarrationRead = (read: boolean) => {
    setReadNarrationIds((ids) => {
      const next = new Set(ids)
      if (read) {
        next.add(scene.id)
      } else {
        next.delete(scene.id)
      }
      return next
    })
  }

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
                setClosedDoorId(null)
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
          if (object.type === 'TextBox') {
            return (
              <p key={object.id} className="room-text-box text-box" style={textBoxStyle(object.x, object.y)}>
                {object.text}
              </p>
            )
          }
          const asset = objectAsset(object.type, object.graphicKey)
          return (
            <button
              key={object.id}
              type="button"
              className="room-object"
              aria-label={label(object)}
              disabled={!online || busy || showNarration}
              style={objectStyle(object.x, object.y, asset)}
              onClick={() => {
                activate(object)
              }}
            >
              <ObjectGraphic asset={asset} open={isOpen(object)} />
            </button>
          )
        })}
        {showNarration && (
          <div className="narration">
            <section className="narration-card" aria-label={t('play.narration.label')}>
              <p className="narration-text">{narration}</p>
              <button
                type="button"
                className="primary"
                onClick={() => {
                  setNarrationRead(true)
                }}
              >
                {t('play.narration.continue')}
              </button>
            </section>
          </div>
        )}
      </section>
      {narration !== null && !showNarration && (
        <button
          type="button"
          className="quiet"
          onClick={() => {
            setNarrationRead(false)
          }}
        >
          {t('play.narration.reread')}
        </button>
      )}
      {closedDoorId !== null && !team.openedDoorIds.includes(closedDoorId) && (
        <p role="status" className="door-closed">
          {t('play.door.closed')}
        </p>
      )}
      {errorCode !== null && (
        <p role="alert" className="error">
          {t(errorKey(errorCode))}
        </p>
      )}
      {openObject?.type === 'Lock' && (
        <LockDialog
          lock={openObject}
          team={team}
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
