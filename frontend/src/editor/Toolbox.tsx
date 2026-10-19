import { useRef, useState, type PointerEvent as ReactPointerEvent, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import { ObjectGraphic } from '../assets/ObjectGraphic'
import { assets, type ObjectAsset } from '../assets/registry'
import { objectHeight, sceneAspectRatio } from '../assets/sceneLayout'
import { pointOnStage, type StagePoint } from './stagePoint'

/** The object stickers the teacher can put on a scene, in the order of the asset library. */
const objectAssets = assets.filter((a): a is ObjectAsset => a.type !== 'Background')

/** Pointer movement (px) before a press on a sticker counts as dragging rather than clicking. */
const dragThreshold = 6

interface Drag {
  asset: ObjectAsset
  pointerId: number
  startX: number
  startY: number
  clientX: number
  clientY: number
  dragging: boolean
  /** Over the stage, where the sticker would land. */
  overStage: boolean
  /** The sticker's width on the stage in px, so the ghost shows the size it will have. */
  width: number
}

function stickerWidth(stage: HTMLElement | null, asset: ObjectAsset): number {
  const stageWidth = stage?.getBoundingClientRect().width ?? 0
  return (stageWidth * objectHeight[asset.type] * asset.aspectRatio) / sceneAspectRatio / 100
}

interface ToolboxProps {
  stageRef: RefObject<HTMLElement | null>
  /** `at` is null for a click or keyboard press: the object goes in the middle of the scene. */
  onAdd: (asset: ObjectAsset, at: StagePoint | null) => void
}

/**
 * The teacher's sheet of stickers. Each can be dragged onto the scene, or clicked (or pressed with Enter) to put it
 * in the middle, so the editor also works without precise pointer control.
 */
export function Toolbox({ stageRef, onAdd }: ToolboxProps) {
  const { t } = useTranslation()
  const [drag, setDrag] = useState<Drag | null>(null)
  const suppressClick = useRef(false)

  const onPointerDown = (event: ReactPointerEvent<HTMLButtonElement>, asset: ObjectAsset) => {
    if (event.button !== 0) {
      return
    }
    // A click swallowed after an earlier drag must not swallow this press too.
    suppressClick.current = false
    event.currentTarget.setPointerCapture(event.pointerId)
    setDrag({
      asset,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      clientX: event.clientX,
      clientY: event.clientY,
      dragging: false,
      overStage: false,
      width: stickerWidth(stageRef.current, asset),
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
        onAdd(drag.asset, at)
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
        {objectAssets.map((asset) => (
          <li key={asset.key}>
            <button
              type="button"
              className="sticker"
              data-lifted={(drag?.dragging && drag.asset.key === asset.key) || undefined}
              aria-label={t('editor.toolbox.add', { name: t(asset.nameKey) })}
              onPointerDown={(event) => {
                onPointerDown(event, asset)
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
                onAdd(asset, null)
              }}
            >
              <ObjectGraphic asset={asset} open={false} />
              <span className="sticker-name">{t(asset.nameKey)}</span>
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
          <ObjectGraphic asset={drag.asset} open={false} />
        </div>
      )}
    </section>
  )
}
