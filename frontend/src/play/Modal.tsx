import { useEffect, useEffectEvent, useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

interface ModalProps {
  title: string
  onClose: () => void
  children: ReactNode
}

/**
 * The pop-up an object opens. Closes with the close button, Escape or a click outside, and gives focus back to
 * whatever had it before (the object that was clicked).
 */
export function Modal({ title, onClose, children }: ModalProps) {
  const { t } = useTranslation()
  const titleId = useId()
  const close = useEffectEvent(onClose)

  // Runs once per opening, so re-renders (e.g. a teammate's progress arriving) don't move the focus.
  useEffect(() => {
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        close()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      opener?.focus()
    }
  }, [])

  return (
    <div
      className="modal-backdrop"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 id={titleId}>{title}</h2>
        {children}
        <button type="button" className="modal-close" onClick={onClose}>
          {t('play.close')}
        </button>
      </div>
    </div>
  )
}
