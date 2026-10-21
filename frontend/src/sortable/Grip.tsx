import { useTranslation } from 'react-i18next'
import type { SortableHandle } from './SortableList'

/**
 * The handle an item in an editable list is dragged by, so that the item's own fields can still be typed in and
 * selected. Focused, the arrow keys move the item.
 */
export function Grip({ handle, item }: { handle: SortableHandle; item: string }) {
  const { t } = useTranslation()
  return (
    <button type="button" className="grip" aria-label={t('sortable.grip', { item })} {...handle.dragProps}>
      <span aria-hidden="true">⠿</span>
    </button>
  )
}
