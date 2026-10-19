import { useState } from 'react'
import type { ObjectAsset } from './registry'

interface ObjectGraphicProps {
  asset: ObjectAsset
  open: boolean
  /** Empty by default: the clickable element around the graphic carries the accessible name. */
  alt?: string
}

/**
 * A room object in its closed or open state. Both images are always rendered (the inactive one hidden), so the
 * open image is already loaded when a lock is solved. The opening animation only plays when `open` changes while
 * the object is on screen, so reconnecting to a team with solved locks doesn't replay it.
 */
export function ObjectGraphic({ asset, open, alt = '' }: ObjectGraphicProps) {
  const [previousOpen, setPreviousOpen] = useState(open)
  const [opening, setOpening] = useState(false)

  if (open !== previousOpen) {
    setPreviousOpen(open)
    setOpening(open)
  }

  return (
    <span
      className="object-graphic"
      data-type={asset.type}
      data-opening={opening || undefined}
      onAnimationEnd={() => {
        setOpening(false)
      }}
    >
      <img src={asset.files.closed} alt={alt} hidden={open} draggable={false} />
      <img src={asset.files.open} alt={alt} hidden={!open} draggable={false} />
    </span>
  )
}
