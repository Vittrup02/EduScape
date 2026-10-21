import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

/** How far the pointer must move before a press becomes a drag, so a tap or click still works as one. */
const dragThreshold = 6

export interface SortableHandle {
  /** Props for the element the item is dragged by (a grip, or the whole item). */
  dragProps: {
    onPointerDown: (event: PointerEvent<HTMLElement>) => void
    onKeyDown: (event: KeyboardEvent<HTMLElement>) => void
    'aria-roledescription': string
    'aria-describedby': string
  }
  /** Small "move up" and "move down" buttons, the alternative to dragging. Null when the list has them turned off. */
  moveButtons: ReactNode
  isDragging: boolean
}

interface SortableListProps<T> {
  items: readonly T[]
  getKey: (item: T, index: number) => string
  /** What a screen reader says about the item when it moves, e.g. its text. */
  itemLabel: (item: T, index: number) => string
  onMove: (from: number, to: number) => void
  renderItem: (item: T, index: number, handle: SortableHandle) => ReactNode
  /** The accessible name of the list. */
  label: string
  orientation?: 'vertical' | 'horizontal'
  /** Show small move buttons next to each item; dragging and the arrow keys work either way. */
  moveButtons?: boolean
  className?: string
  /** Rendered after the items, e.g. an "add" button, but not sortable itself. */
  footer?: ReactNode
}

interface Drag {
  from: number
  pointerId: number
  startX: number
  startY: number
  /** Null until the pointer has moved far enough to count as a drag. */
  to: number | null
  offset: number
}

/**
 * A list sorted by dragging, with the mouse or a finger alike (pointer events), where the order of the list is what
 * counts: the teacher's steps, options and items, the scenes, and the students' order puzzles. Each item decides what
 * it is dragged by. A drag starts only after the pointer moves a little, so items can still be clicked. The arrow keys
 * on the drag element, or the optional move buttons, do the same without dragging.
 */
export function SortableList<T>({
  items,
  getKey,
  itemLabel,
  onMove,
  renderItem,
  label,
  orientation = 'vertical',
  moveButtons = false,
  className,
  footer,
}: SortableListProps<T>) {
  const { t } = useTranslation()
  const listRef = useRef<HTMLUListElement>(null)
  const [drag, setDrag] = useState<Drag | null>(null)
  const [announcement, setAnnouncement] = useState('')
  // After a move with the keyboard, focus follows the item to its new place. A new object for every move, so moving
  // the same item twice focuses it twice.
  const [focusRequest, setFocusRequest] = useState<{ key: string } | null>(null)
  const horizontal = orientation === 'horizontal'
  const instructionsId = `${useStableId()}-instructions`

  useEffect(() => {
    if (focusRequest === null || listRef.current === null) {
      return
    }
    const item = Array.from(listRef.current.querySelectorAll<HTMLElement>('[data-sortable-key]')).find(
      (element) => element.dataset.sortableKey === focusRequest.key,
    )
    item?.querySelector<HTMLElement>('[aria-roledescription]')?.focus()
  }, [focusRequest])

  const move = (from: number, to: number, viaKeyboard: boolean) => {
    if (to < 0 || to >= items.length || to === from) {
      return
    }
    if (viaKeyboard) {
      setFocusRequest({ key: getKey(items[from], from) })
    }
    setAnnouncement(t('sortable.moved', { item: itemLabel(items[from], from), position: to + 1, count: items.length }))
    onMove(from, to)
  }

  /**
   * The index the dragged item would land on, from the pointer's position over the other items. A horizontal list
   * may wrap onto several rows: an item on a row above the pointer counts as passed, whatever its x.
   */
  const targetIndex = (from: number, clientX: number, pointerY: number): number => {
    const list = listRef.current
    const elements = Array.from(list?.querySelectorAll<HTMLElement>(':scope > [data-sortable-key]') ?? [])
    // A finger drifting above or below a horizontal list still points at its first or last row.
    const listBox = list?.getBoundingClientRect()
    const clientY =
      horizontal && listBox !== undefined ? Math.min(Math.max(pointerY, listBox.top), listBox.bottom) : pointerY
    let target = from
    elements.forEach((element, index) => {
      if (index === from) {
        return
      }
      const box = element.getBoundingClientRect()
      const passed = horizontal
        ? clientY > box.bottom || (clientY >= box.top && clientX > box.left + box.width / 2)
        : clientY > box.top + box.height / 2
      if (index < from && !passed) {
        target = Math.min(target, index)
      }
      if (index > from && passed) {
        target = Math.max(target, index)
      }
    })
    return target
  }

  const handleFor = (index: number): SortableHandle => {
    const onPointerDown = (event: PointerEvent<HTMLElement>) => {
      if (event.button !== 0) {
        return
      }
      setDrag({ from: index, pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, to: null, offset: 0 })
    }
    const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
      const back = horizontal ? 'ArrowLeft' : 'ArrowUp'
      const forward = horizontal ? 'ArrowRight' : 'ArrowDown'
      if (event.key === back || event.key === forward) {
        event.preventDefault()
        move(index, index + (event.key === back ? -1 : 1), true)
      }
    }
    return {
      dragProps: {
        onPointerDown,
        onKeyDown,
        'aria-roledescription': t('sortable.roleDescription'),
        'aria-describedby': instructionsId,
      },
      moveButtons: moveButtons ? (
        <span className="sortable-move">
          <button
            type="button"
            className="quiet"
            disabled={index === 0}
            aria-label={t('sortable.moveUp', { item: itemLabel(items[index], index) })}
            onClick={() => {
              move(index, index - 1, false)
            }}
          >
            <span aria-hidden="true">{horizontal ? '←' : '↑'}</span>
          </button>
          <button
            type="button"
            className="quiet"
            disabled={index === items.length - 1}
            aria-label={t('sortable.moveDown', { item: itemLabel(items[index], index) })}
            onClick={() => {
              move(index, index + 1, false)
            }}
          >
            <span aria-hidden="true">{horizontal ? '→' : '↓'}</span>
          </button>
        </span>
      ) : null,
      isDragging: drag !== null && drag.from === index && drag.to !== null,
    }
  }

  const onPointerMove = (event: PointerEvent<HTMLUListElement>) => {
    if (drag === null || event.pointerId !== drag.pointerId) {
      return
    }
    const dx = event.clientX - drag.startX
    const dy = event.clientY - drag.startY
    if (drag.to === null && Math.hypot(dx, dy) < dragThreshold) {
      return
    }
    if (drag.to === null) {
      // From here on the pointer belongs to the list, even when it leaves it.
      try {
        listRef.current?.setPointerCapture(event.pointerId)
      } catch {
        // Not every environment supports pointer capture; the drag still works while the pointer is over the list.
      }
    }
    event.preventDefault()
    setDrag({ ...drag, to: targetIndex(drag.from, event.clientX, event.clientY), offset: horizontal ? dx : dy })
  }

  const onPointerUp = (event: PointerEvent<HTMLUListElement>) => {
    if (drag === null || event.pointerId !== drag.pointerId) {
      return
    }
    setDrag(null)
    // Decided from where the pointer is let go, not from the last move: moves are rendered when React gets to them,
    // and a quick finger can lift before the last one is.
    const dragged =
      drag.to !== null || Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= dragThreshold
    if (dragged) {
      // A drag is not a click on whatever was under the pointer.
      event.preventDefault()
      suppressNextClick(listRef.current)
      move(drag.from, targetIndex(drag.from, event.clientX, event.clientY), false)
    }
  }

  const dropBefore = drag?.to == null ? null : drag.to > drag.from ? drag.to + 1 : drag.to

  return (
    <>
      <ul
        ref={listRef}
        className={['sortable', horizontal ? 'sortable-horizontal' : '', className ?? ''].join(' ').trim()}
        aria-label={label}
        data-dragging={drag?.to != null || undefined}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={() => {
          setDrag(null)
        }}
      >
        {items.map((item, index) => {
          const handle = handleFor(index)
          const key = getKey(item, index)
          return (
            <li
              key={key}
              data-sortable-key={key}
              className="sortable-item"
              data-dragging={handle.isDragging || undefined}
              data-drop-before={dropBefore === index || undefined}
              data-drop-after={dropBefore === items.length && index === items.length - 1 ? true : undefined}
              style={
                handle.isDragging && drag !== null
                  ? { transform: horizontal ? `translateX(${String(drag.offset)}px)` : `translateY(${String(drag.offset)}px)` }
                  : undefined
              }
            >
              {renderItem(item, index, handle)}
            </li>
          )
        })}
        {footer !== undefined && <li className="sortable-footer">{footer}</li>}
      </ul>
      <p id={instructionsId} className="visually-hidden">
        {t(horizontal ? 'sortable.instructionsHorizontal' : 'sortable.instructions')}
      </p>
      <p className="visually-hidden" aria-live="polite">
        {announcement}
      </p>
    </>
  )
}

/** Swallows the click the browser fires after a drag ends on a button or link inside the list. */
function suppressNextClick(element: HTMLElement | null) {
  if (element === null) {
    return
  }
  const swallow = (event: MouseEvent) => {
    event.stopPropagation()
    event.preventDefault()
  }
  element.addEventListener('click', swallow, { capture: true, once: true })
  // If no click follows (e.g. the pointer ended outside the button), the next real click must still work.
  setTimeout(() => {
    element.removeEventListener('click', swallow, { capture: true })
  }, 0)
}

let nextId = 0

/** A stable id for this list instance (useId gives ':r1:'-style ids, which are not valid in CSS selectors). */
function useStableId(): string {
  const [id] = useState(() => `sortable-${String(++nextId)}`)
  return id
}
