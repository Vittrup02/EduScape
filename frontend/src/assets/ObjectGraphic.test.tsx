import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ObjectGraphic } from './ObjectGraphic'
import { assetsOfType } from './registry'

const [padlock] = assetsOfType('Lock')

function shownImage(): HTMLImageElement {
  return screen.getByRole('img', { name: 'Lås' })
}

describe('ObjectGraphic', () => {
  it('shows the image for the current state', () => {
    const { rerender } = render(<ObjectGraphic asset={padlock} open={false} alt="Lås" />)
    expect(shownImage()).toHaveAttribute('src', padlock.files.closed)

    rerender(<ObjectGraphic asset={padlock} open alt="Lås" />)
    expect(shownImage()).toHaveAttribute('src', padlock.files.open)
  })

  it('keeps the other state loaded but hidden', () => {
    const { container } = render(<ObjectGraphic asset={padlock} open={false} />)
    const images = container.querySelectorAll('img')

    expect(images).toHaveLength(2)
    expect(images[1]).toHaveAttribute('src', padlock.files.open)
    expect(images[1]).not.toBeVisible()
  })

  it('does not animate an object that is already open when it appears', () => {
    const { container } = render(<ObjectGraphic asset={padlock} open />)

    expect(container.firstElementChild).not.toHaveAttribute('data-opening')
  })

  it('animates once when the object opens', () => {
    const { container, rerender } = render(<ObjectGraphic asset={padlock} open={false} />)
    const graphic = container.firstElementChild as HTMLElement
    expect(graphic).toHaveAttribute('data-type', 'Lock')
    expect(graphic).not.toHaveAttribute('data-opening')

    rerender(<ObjectGraphic asset={padlock} open />)
    expect(graphic).toHaveAttribute('data-opening')

    fireEvent.animationEnd(graphic)
    expect(graphic).not.toHaveAttribute('data-opening')

    rerender(<ObjectGraphic asset={padlock} open />)
    expect(graphic).not.toHaveAttribute('data-opening')
  })

  it('does not animate when the object closes again', () => {
    const { container, rerender } = render(<ObjectGraphic asset={padlock} open />)

    rerender(<ObjectGraphic asset={padlock} open={false} />)
    expect(container.firstElementChild).not.toHaveAttribute('data-opening')
  })
})
