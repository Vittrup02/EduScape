import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { PlayState } from '../api/play'
import { playRoom, playState } from '../test/playRoom'
import { RoomView } from './RoomView'

const story = 'Det er 1943. I har gemt jer på loftet, og om lidt kommer soldaterne.'

function stateWithStory(): PlayState {
  return playState(
    { openedDoorIds: ['door'], solvedLockIds: ['code-lock', 'text-lock'] },
    {
      room: {
        ...playRoom,
        scenes: playRoom.scenes.map((scene, i) => (i === 0 ? { ...scene, narration: story } : scene)),
      },
    },
  )
}

function renderRoom(state: PlayState) {
  render(<RoomView state={state} deviceToken="token" online onEvent={() => undefined} />)
}

describe('a scene with a story', () => {
  it('shows the story when the student enters, then the room', () => {
    renderRoom(stateWithStory())

    const card = screen.getByRole('region', { name: 'Fortælling' })
    expect(card).toHaveTextContent(story)
    // The room waits behind the story.
    expect(screen.getByRole('button', { name: 'Brev' })).toBeDisabled()

    fireEvent.click(within(card).getByRole('button', { name: 'Videre' }))

    expect(screen.queryByRole('region', { name: 'Fortælling' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Brev' })).toBeEnabled()
  })

  it('can be read again, and does not come back by itself after the student has read it', () => {
    renderRoom(stateWithStory())
    fireEvent.click(screen.getByRole('button', { name: 'Videre' }))

    // Away to the library (no story there) and back again.
    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))
    expect(screen.queryByRole('region', { name: 'Fortælling' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Læs fortællingen igen' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Klasselokalet' }))
    expect(screen.queryByRole('region', { name: 'Fortælling' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Læs fortællingen igen' }))
    expect(screen.getByRole('region', { name: 'Fortælling' })).toHaveTextContent(story)
  })

  it('shows nothing extra in a scene without a story', () => {
    renderRoom(playState())

    expect(screen.queryByRole('region', { name: 'Fortælling' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Læs fortællingen igen' })).not.toBeInTheDocument()
  })
})
