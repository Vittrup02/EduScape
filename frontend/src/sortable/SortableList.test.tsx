import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { SortableList } from './SortableList'

const rowHeight = 40

/** jsdom has no layout: give each item a box, stacked from the top, so the list can tell where the pointer is. */
function layOut(list: HTMLElement) {
  list.querySelectorAll<HTMLElement>(':scope > [data-sortable-key]').forEach((item, index) => {
    vi.spyOn(item, 'getBoundingClientRect').mockReturnValue(
      DOMRect.fromRect({ x: 0, y: index * rowHeight, width: 200, height: rowHeight }),
    )
  })
}

function Steps({ onClick = () => undefined, wholeItem = false }: { onClick?: (item: string) => void; wholeItem?: boolean }) {
  const [items, setItems] = useState(['Afvej', 'Opløs', 'Filtrér', 'Tør'])
  return (
    <SortableList
      items={items}
      getKey={(item) => item}
      itemLabel={(item) => item}
      label="Trin"
      moveButtons
      onMove={(from, to) => {
        setItems((current) => {
          const next = [...current]
          next.splice(to, 0, ...next.splice(from, 1))
          return next
        })
      }}
      renderItem={(item, _, handle) =>
        wholeItem ? (
          <button
            type="button"
            {...handle.dragProps}
            onClick={() => {
              onClick(item)
            }}
          >
            {item}
          </button>
        ) : (
          <>
            <span className="grip" aria-label={`Træk ${item}`} tabIndex={0} {...handle.dragProps} />
            <span>{item}</span>
            {handle.moveButtons}
          </>
        )
      }
    />
  )
}

function order(): string[] {
  return screen.getAllByRole('listitem').map((item) => item.textContent.replace(/[↑↓]/g, ''))
}

function drag(element: HTMLElement, fromY: number, toY: number, pointerType = 'mouse') {
  const list = screen.getByRole('list', { name: 'Trin' })
  fireEvent.pointerDown(element, { pointerId: 1, button: 0, clientX: 10, clientY: fromY, pointerType })
  fireEvent.pointerMove(list, { pointerId: 1, clientX: 10, clientY: (fromY + toY) / 2, pointerType })
  fireEvent.pointerMove(list, { pointerId: 1, clientX: 10, clientY: toY, pointerType })
  fireEvent.pointerUp(list, { pointerId: 1, clientX: 10, clientY: toY, pointerType })
}

describe('a sortable list', () => {
  it('moves an item to where it is dragged with the mouse', () => {
    render(<Steps />)
    layOut(screen.getByRole('list', { name: 'Trin' }))

    // From the middle of the first row to below the middle of the third.
    drag(screen.getByLabelText('Træk Afvej'), 20, 2 * rowHeight + 30)

    expect(order()).toEqual(['Opløs', 'Filtrér', 'Afvej', 'Tør'])
  })

  it('moves an item dragged with a finger, upwards too', () => {
    render(<Steps />)
    layOut(screen.getByRole('list', { name: 'Trin' }))

    drag(screen.getByLabelText('Træk Tør'), 3 * rowHeight + 20, 5, 'touch')

    expect(order()).toEqual(['Tør', 'Afvej', 'Opløs', 'Filtrér'])
  })

  it('treats a press without movement as a click, and a drag not as one', () => {
    const onClick = vi.fn()
    render(<Steps wholeItem onClick={onClick} />)
    const list = screen.getByRole('list', { name: 'Trin' })
    layOut(list)

    const first = screen.getByRole('button', { name: 'Afvej' })
    fireEvent.pointerDown(first, { pointerId: 1, button: 0, clientX: 10, clientY: 20 })
    fireEvent.pointerMove(list, { pointerId: 1, clientX: 12, clientY: 22 })
    fireEvent.pointerUp(list, { pointerId: 1, clientX: 12, clientY: 22 })
    fireEvent.click(first)
    expect(onClick).toHaveBeenCalledWith('Afvej')
    expect(order()).toEqual(['Afvej', 'Opløs', 'Filtrér', 'Tør'])

    onClick.mockClear()
    drag(first, 20, rowHeight + 30)
    fireEvent.click(screen.getByRole('button', { name: 'Afvej' }))
    expect(onClick).not.toHaveBeenCalled()
    expect(order()).toEqual(['Opløs', 'Afvej', 'Filtrér', 'Tør'])
  })

  it('can be sorted with the arrow keys, keeping the focus on the moved item', () => {
    render(<Steps />)

    fireEvent.keyDown(screen.getByLabelText('Træk Opløs'), { key: 'ArrowDown' })

    expect(order()).toEqual(['Afvej', 'Filtrér', 'Opløs', 'Tør'])
    expect(screen.getByLabelText('Træk Opløs')).toHaveFocus()
    expect(screen.getByText('Opløs er nu nummer 3 af 4.')).toBeInTheDocument()

    fireEvent.keyDown(screen.getByLabelText('Træk Afvej'), { key: 'ArrowUp' })
    expect(order()).toEqual(['Afvej', 'Filtrér', 'Opløs', 'Tør'])
  })

  it('sorts a horizontal list that wraps onto several rows, also when the finger drifts above it', () => {
    function Scenes() {
      const [items, setItems] = useState(['Gangen', 'Laboratoriet', 'Kælderen', 'Loftet'])
      return (
        <SortableList
          items={items}
          getKey={(item) => item}
          itemLabel={(item) => item}
          label="Scener"
          orientation="horizontal"
          onMove={(from, to) => {
            setItems((current) => {
              const next = [...current]
              next.splice(to, 0, ...next.splice(from, 1))
              return next
            })
          }}
          renderItem={(item, _, handle) => (
            <button type="button" {...handle.dragProps}>
              {item}
            </button>
          )}
        />
      )
    }
    render(<Scenes />)
    const list = screen.getByRole('list', { name: 'Scener' })
    // Two rows of two scenes, 100 × 40 each.
    vi.spyOn(list, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 0, width: 200, height: 80 }))
    list.querySelectorAll<HTMLElement>(':scope > [data-sortable-key]').forEach((item, index) => {
      vi.spyOn(item, 'getBoundingClientRect').mockReturnValue(
        DOMRect.fromRect({ x: (index % 2) * 100, y: Math.floor(index / 2) * 40, width: 100, height: 40 }),
      )
    })
    const dragTo = (name: string, from: { x: number; y: number }, to: { x: number; y: number }) => {
      fireEvent.pointerDown(screen.getByRole('button', { name }), { pointerId: 1, button: 0, clientX: from.x, clientY: from.y })
      fireEvent.pointerMove(list, { pointerId: 1, clientX: to.x, clientY: to.y })
      fireEvent.pointerUp(list, { pointerId: 1, clientX: to.x, clientY: to.y })
    }
    const scenes = () => screen.getAllByRole('button').map((b) => b.textContent)

    // From the first row to past the middle of the last scene, on the second row.
    dragTo('Gangen', { x: 50, y: 20 }, { x: 170, y: 60 })
    expect(scenes()).toEqual(['Laboratoriet', 'Kælderen', 'Loftet', 'Gangen'])

    // Above the list, the pointer still counts as on the first row: before the middle of its first scene.
    dragTo('Kælderen', { x: 50, y: 60 }, { x: 20, y: -30 })
    expect(scenes()).toEqual(['Kælderen', 'Laboratoriet', 'Loftet', 'Gangen'])
  })

  it('offers move buttons as well, which stop at the ends', () => {
    render(<Steps />)

    fireEvent.click(screen.getByRole('button', { name: 'Flyt Filtrér op' }))

    expect(order()).toEqual(['Afvej', 'Filtrér', 'Opløs', 'Tør'])
    expect(screen.getByRole('button', { name: 'Flyt Afvej op' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Flyt Tør ned' })).toBeDisabled()
  })
})
