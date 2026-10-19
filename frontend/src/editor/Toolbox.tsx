import { useRef, useState, type PointerEvent as ReactPointerEvent, type ReactNode, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import { ObjectGraphic } from '../assets/ObjectGraphic'
import { assets, type ObjectAsset } from '../assets/registry'
import { objectHeight, sceneAspectRatio, textBoxWidth } from '../assets/sceneLayout'
import { pointOnStage, type StagePoint } from './stagePoint'

/** A sticker from the asset library, or a text box for a piece of the story. */
export type ToolboxItem = { kind: 'asset'; asset: ObjectAsset } | { kind: 'textBox' }

/** The stickers the teacher can put on a scene: the asset library's objects, then the text box. */
const items: ToolboxItem[] = [
  ...assets.filter((a): a is ObjectAsset => a.type !== 'Background').map((asset) => ({ kind: 'asset' as const, asset })),
  { kind: 'textBox' },
]

function itemKey(item: ToolboxItem): string {
  return item.kind === 'asset' ? item.asset.key : 'text-box'
}

/** Pointer movement (px) before a press on a sticker counts as dragging rather than clicking. */
const dragThreshold = 6

interface Drag {
  item: ToolboxItem
  pointerId: number
  startX: number
  startY: number
  clientX: number
  clientY: number
  dragging: boolean
  /** Over the stage, where the sticker would land. */
  overStage: boolean
  /** The item's width on the stage in px, so the ghost shows the size it will have. */
  width: number
}

function itemWidth(stage: HTMLElement | null, item: ToolboxItem): number {
  const stageWidth = stage?.getBoundingClientRect().width ?? 0
  if (item.kind === 'textBox') {
    return (stageWidth * textBoxWidth) / 100
  }
  return (stageWidth * objectHeight[item.asset.type] * item.asset.aspectRatio) / sceneAspectRatio / 100
}

interface ToolboxProps {
  stageRef: RefObject<HTMLElement | null>
  /** `at` is null for a click or keyboard press: the object goes in the middle of the scene. */
  onAdd: (item: ToolboxItem, at: StagePoint | null) => void
}

/**
 * The teacher's sheet of stickers. Each can be dragged onto the scene, or clicked (or pressed with Enter) to put it
 * in the middle, so the editor also works without precise pointer control.
 */
export function Toolbox({ stageRef, onAdd }: ToolboxProps) {
  const { t } = useTranslation()
  const [drag, setDrag] = useState<Drag | null>(null)
  const suppressClick = useRef(false)

  const name = (item: ToolboxItem) => (item.kind === 'asset' ? t(item.asset.nameKey) : t('editor.toolbox.textBox'))

  const picture = (item: ToolboxItem): ReactNode =>
    item.kind === 'asset' ? (
      <ObjectGraphic asset={item.asset} open={false} />
    ) : (
      <span className="text-box-icon" aria-hidden="true">
        Aa
      </span>
    )

  const onPointerDown = (event: ReactPointerEvent<HTMLButtonElement>, item: ToolboxItem) => {
    if (event.button !== 0) {
      return
    }
    // A click swallowed after an earlier drag must not swallow this press too.
    suppressClick.current = false
    event.currentTarget.setPointerCapture(event.pointerId)
    setDrag({
      item,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      clientX: event.clientX,
      clientY: event.clientY,
      dragging: false,
      overStage: false,
      width: itemWidth(stageRef.current, item),
    })
  }

  const onPointerMove = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (drag?.pointerId !== event.pointerId) {
      return
    }
    const dragging =
      drag.dragging || Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) > dragThreshold
    setDrag({
      ...drag,
      clientX: event.clientX,
      clientY: event.clientY,
      dragging,
      overStage: pointOnStage(stageRef.current, event.clientX, event.clientY) !== null,
    })
  }

  const onPointerUp = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (drag?.pointerId !== event.pointerId) {
      return
    }
    if (drag.dragging) {
      suppressClick.current = true
      const at = pointOnStage(stageRef.current, event.clientX, event.clientY)
      if (at !== null) {
        onAdd(drag.item, at)
      }
    }
    setDrag(null)
  }

  return (
    <section className="toolbox" aria-labelledby="toolbox-title">
      <h2 id="toolbox-title" className="toolbox-title">
        {t('editor.toolbox.title')}
      </h2>
      <p className="toolbox-hint">{t('editor.toolbox.hint')}</p>
      <ul className="stickers">
        {items.map((item) => (
          <li key={itemKey(item)}>
            <button
              type="button"
              className="sticker"
              data-lifted={(drag?.dragging && itemKey(drag.item) === itemKey(item)) || undefined}
              aria-label={t('editor.toolbox.add', { name: name(item) })}
              onPointerDown={(event) => {
                onPointerDown(event, item)
              }}
              onPointerMove={onPointerMove}
              onPointerUp={onPointerUp}
              onPointerCancel={() => {
                setDrag(null)
              }}
              onClick={() => {
                if (suppressClick.current) {
                  suppressClick.current = false
                  return
                }
                onAdd(item, null)
              }}
            >
              {picture(item)}
              <span className="sticker-name">{name(item)}</span>
            </button>
          </li>
        ))}
      </ul>
      {drag?.dragging && (
        <div
          className="sticker-ghost"
          data-over-stage={drag.overStage || undefined}
          aria-hidden="true"
          style={{ left: drag.clientX, top: drag.clientY, width: drag.width > 0 ? drag.width : undefined }}
        >
          {drag.item.kind === 'asset' ? (
            <ObjectGraphic asset={drag.item.asset} open={false} />
          ) : (
            <span className="text-box">{t('editor.textBox.placeholder')}</span>
          )}
        </div>
      )}
    </section>
  )
}
