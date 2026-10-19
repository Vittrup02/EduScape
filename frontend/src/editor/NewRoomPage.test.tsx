import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveAuth } from '../api/tokenStorage'
import { AppRoutes } from '../App'
import { editorRoomJson } from '../test/editorRoom'
import { json, mockFetch } from '../test/fetchMock'

describe('a new room', () => {
  beforeEach(() => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('needs only a title, subject and grade level, then opens the editor', async () => {
    let created: unknown
    mockFetch({
      '/api/teacher/rooms': (init) => {
        created = JSON.parse(init?.body as string)
        return json({ id: 'room', title: 'Brøker på flugt', subject: 'Mathematics', gradeLevel: 'Hf1' }, 201)
      },
      '/api/teacher/rooms/room/editor': () => json(editorRoomJson()),
    })
    render(
      <MemoryRouter initialEntries={['/teacher/rooms/new']}>
        <AppRoutes />
      </MemoryRouter>,
    )

    fireEvent.change(screen.getByLabelText('Titel'), { target: { value: 'Brøker på flugt' } })
    fireEvent.change(screen.getByLabelText('Fag'), { target: { value: 'Mathematics' } })
    fireEvent.change(screen.getByLabelText('Klassetrin'), { target: { value: 'Hf1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Opret rummet' }))

    // The first scene is named in the teacher's language.
    await waitFor(() => {
      expect(created).toEqual({ title: 'Brøker på flugt', subject: 'Mathematics', gradeLevel: 'Hf1', sceneName: 'Scene 1' })
    })
    expect(await screen.findByRole('region', { name: 'Scenen Gangen' })).toBeInTheDocument()
  })

  it('groups grade levels into primary and upper-secondary school', () => {
    mockFetch({})
    render(
      <MemoryRouter initialEntries={['/teacher/rooms/new']}>
        <AppRoutes />
      </MemoryRouter>,
    )

    expect(screen.getByRole('group', { name: 'Folkeskole' })).toHaveTextContent('10. klasse')
    expect(screen.getByRole('group', { name: 'Ungdomsuddannelser' })).toHaveTextContent('2. hf')
  })
})
