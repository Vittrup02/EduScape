import {
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  type RefObject,
} from 'react'
import { useTranslation } from 'react-i18next'
import type { EditorObject, EditorScene } from '../api/editor'
import { ObjectGraphic } from '../assets/ObjectGraphic'
import { backgroundImage, objectAsset, objectStyle } from '../assets/sceneLayout'

/** Pointer movement (px) before pressing an object counts as dragging it rather than selecting it. */
const dragThreshold = 3

interface ObjectDrag {
  objectId: string
  pointerId: number
  startX: number
  startY: number
  /** From the pointer to the object's centre, in percent of the stage, so the object doesn't jump to the pointer. */
  offsetX: number
  offsetY: number
  dragging: boolean
}

interface StageProps {
  stageRef: RefObject<HTMLElement | null>
  scene: EditorScene
  selectedId: string | null
  label: (object: EditorObject) => string
  onSelect: (objectId: string | null) => void
  /** `step` groups the changes of one drag (or one run of nudges) into one undo step. */
  onMove: (objectId: string, x: number, y: number, step: string) => void
  onMoveEnd: () => void
  onRemove: (objectId: string) => void
}

/**
 * The scene as students will see it, with every object visible (also those that appear later). Objects are moved
 * by dragging or with the arrow keys (Shift for bigger steps), and removed with Delete.
 */
export function Stage({ stageRef, scene, selectedId, label, onSelect, onMove, onMoveEnd, onRemove }: StageProps) {
  const { t } = useTranslation()
  const [drag, setDrag] = useState<ObjectDrag | null>(null)
  const [pressedId, setPressedId] = useState<string | null>(null)
  const dragCount = useRef(0)
  const suppressClick = useRef(false)

  const percentOf = (clientX: number, clientY: number) => {
    const rect = stageRef.current?.getBoundingClientRect()
    if (rect === undefined || rect.width === 0) {
      return null
    }
    return { x: ((clientX - rect.left) / rect.width) * 100, y: ((clientY - rect.top) / rect.height) * 100 }
  }

  const onPointerDown = (event: ReactPointerEvent<HTMLButtonElement>, object: EditorObject) => {
    const point = percentOf(event.clientX, event.clientY)
    if (event.button !== 0 || point === null) {
      return
    }
    event.currentTarget.setPointerCapture(event.pointerId)
    dragCount.current += 1
    setDrag({
      objectId: object.id,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      offsetX: object.x - point.x,
      offsetY: object.y - point.y,
      dragging: false,
    })
  }

  const onPointerMove = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (drag?.pointerId !== event.pointerId) {
      return
    }
    const dragging =
      drag.dragging || Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) > dragThreshold
    if (!dragging) {
      return
    }
    if (!drag.dragging) {
      setDrag({ ...drag, dragging: true })
      onSelect(drag.objectId)
    }
    const point = percentOf(event.clientX, event.clientY)
    if (point !== null) {
      onMove(drag.objectId, point.x + drag.offsetX, point.y + drag.offsetY, `drag:${String(dragCount.current)}`)
    }
  }

  const onPointerUp = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (drag?.pointerId !== event.pointerId) {
      return
    }
    if (drag.dragging) {
      suppressClick.current = true
      setPressedId(drag.objectId)
      onMoveEnd()
    }
    setDrag(null)
  }

  const onKeyDown = (event: ReactKeyboardEvent<HTMLButtonElement>, object: EditorObject) => {
    const step = event.shiftKey ? 5 : 1
    const moves: Record<string, [number, number]> = {
      ArrowLeft: [-step, 0],
      ArrowRight: [step, 0],
      ArrowUp: [0, -step],
      ArrowDown: [0, step],
    }
    const move = moves[event.key] as [number, number] | undefined
    if (move !== undefined) {
      event.preventDefault()
      onSelect(object.id)
      onMove(object.id, object.x + move[0], object.y + move[1], `nudge:${object.id}`)
    } else if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault()
      onRemove(object.id)
    } else if (event.key === 'Escape') {
      onSelect(null)
    }
  }

  const background = backgroundImage(scene.backgroundImageKey, scene.backgroundImageUrl)

  return (
    <section
      ref={stageRef}
      className="scene editor-stage"
      aria-label={t('editor.stage.label', { scene: scene.name })}
      onPointerDown={(event) => {
        if (event.target === event.currentTarget || event.target instanceof HTMLImageElement) {
          onSelect(null)
        }
      }}
    >
      {background !== undefined && <img className="scene-background" src={background} alt="" draggable={false} />}
      {scene.objects.length === 0 && <p className="stage-empty">{t('editor.stage.empty')}</p>}
      {scene.objects.map((object) => {
        const asset = objectAsset(object.type, object.graphicKey)
        const waits = object.unlockedById !== null
        return (
          <button
            key={object.id}
            type="button"
            className="room-object stage-object"
            aria-label={waits ? t('editor.stage.objectLater', { name: label(object) }) : label(object)}
            aria-pressed={object.id === selectedId}
            data-waits={waits || undefined}
            data-dragging={(drag?.dragging && drag.objectId === object.id) || undefined}
            data-pressed={pressedId === object.id || undefined}
            style={objectStyle(object.x, object.y, asset)}
            onPointerDown={(event) => {
              onPointerDown(event, object)
            }}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={() => {
              setDrag(null)
            }}
            onAnimationEnd={() => {
              setPressedId(null)
            }}
            onKeyDown={(event) => {
              onKeyDown(event, object)
            }}
            onBlur={onMoveEnd}
            onClick={() => {
              if (suppressClick.current) {
                suppressClick.current = false
                return
              }
              onSelect(object.id)
            }}
          >
            <ObjectGraphic asset={asset} open={false} />
          </button>
        )
      })}
    </section>
  )
}
