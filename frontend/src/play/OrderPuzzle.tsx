import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { PlayOrderItem } from '../api/play'
import { SortableList } from '../sortable/SortableList'

interface OrderPuzzleProps {
  /** Shuffled by the server; never in the right order to begin with. */
  items: PlayOrderItem[]
  disabled: boolean
  onSubmit: (order: string[]) => void
}

/**
 * The student drags the cards into the right order, with the mouse or a finger (or the arrow keys), and tries it.
 * Only the server knows the right order.
 */
export function OrderPuzzle({ items, disabled, onSubmit }: OrderPuzzleProps) {
  const { t } = useTranslation()
  const [order, setOrder] = useState(items)

  return (
    <div className="order-puzzle">
      <p className="field-hint">{t('play.order.instructions')}</p>
      <SortableList
        items={order}
        label={t('play.order.label')}
        getKey={(item) => item.id}
        itemLabel={(item) => item.text}
        className="order-cards"
        onMove={(from, to) => {
          setOrder((current) => {
            const next = [...current]
            next.splice(to, 0, ...next.splice(from, 1))
            return next
          })
        }}
        renderItem={(item, index, handle) => (
          <div className="order-card" tabIndex={0} aria-label={`${String(index + 1)}. ${item.text}`} {...handle.dragProps}>
            <span className="order-number" aria-hidden="true">
              {index + 1}
            </span>
            {item.imageUrl !== null && <img src={item.imageUrl} alt="" draggable={false} />}
            <span className="order-text">{item.text}</span>
          </div>
        )}
      />
      <button
        type="button"
        className="primary"
        disabled={disabled}
        onClick={() => {
          onSubmit(order.map((item) => item.id))
        }}
      >
        {t('play.order.submit')}
      </button>
    </div>
  )
}
