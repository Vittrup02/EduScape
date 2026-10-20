import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveAuth } from '../api/tokenStorage'
import { AppRoutes } from '../App'
import { editorRoom, editorRoomJson } from '../test/editorRoom'
import { json, mockFetch } from '../test/fetchMock'

const editorPath = '/api/teacher/rooms/room/editor'
const saveWait = { timeout: 3000 }

interface SentRoom {
  revision: number
  title: string
  scenes: { id: string; backgroundImageKey: string; objects: { id: string; type: string; graphicKey: string }[] }[]
}

function renderEditor() {
  render(
    <MemoryRouter initialEntries={['/teacher/rooms/room/edit']}>
      <AppRoutes />
    </MemoryRouter>,
  )
}

/** The editor endpoint: GET answers with the room, PUT records what was saved and answers with `save`. */
function mockEditor(room = editorRoomJson(), save: (sent: SentRoom) => Response = (sent) =>
  json({ revision: sent.revision + 1, validationErrors: [] })) {
  const saved: SentRoom[] = []
  mockFetch({
    [editorPath]: (init) => {
      if (init?.method === 'PUT') {
        const sent = JSON.parse(init.body as string) as SentRoom
        saved.push(sent)
        return save(sent)
      }
      return json(room)
    },
  })
  return saved
}

describe('the room editor', () => {
  beforeEach(() => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the scene, its objects and the map of doors', async () => {
    mockEditor()
    renderEditor()

    const stage = await screen.findByRole('region', { name: 'Scenen Gangen' })
    expect(within(stage).getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' })).toBeInTheDocument()
    expect(within(stage).getByRole('button', { name: 'Døren til Biblioteket (vises senere)' })).toBeInTheDocument()
    expect(screen.getByText('Gangen fører til Biblioteket')).toBeInTheDocument()
    expect(screen.getByLabelText('Navn')).toHaveValue('Gangen')
    expect(screen.getByText('Gemt')).toBeInTheDocument()
  })

  it('opens a small panel with only the settings that matter for the lock', async () => {
    mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    const panel = screen.getByRole('complementary')
    expect(within(panel).getByLabelText('Spørgsmål')).toHaveValue('Hvad er 3 · 4?')
    expect(within(panel).getByLabelText('Det rigtige svar')).toHaveValue('12')
    expect(within(panel).queryByLabelText(/Godkend også svar/)).not.toBeInTheDocument()

    // Tolerance only for text answers that are numbers.
    fireEvent.click(within(panel).getByRole('radio', { name: /^Tekst eller tal/ }))
    expect(within(panel).getByLabelText(/Godkend også svar/)).toBeInTheDocument()
    fireEvent.change(within(panel).getByLabelText('Det rigtige svar'), { target: { value: 'Paris' } })
    expect(within(panel).queryByLabelText(/Godkend også svar/)).not.toBeInTheDocument()

    // Multiple choice starts with the answer already typed as the first option.
    fireEvent.click(within(panel).getByRole('radio', { name: /^Vælg mellem svar/ }))
    expect(within(panel).getByLabelText('Svarmulighed 1')).toHaveValue('Paris')
    expect(within(panel).getByRole('radio', { name: 'Svarmulighed 1 er det rigtige svar' })).toBeChecked()

    // The hint field appears only when asked for.
    expect(within(panel).queryByLabelText('Hint')).not.toBeInTheDocument()
    fireEvent.click(within(panel).getByRole('button', { name: 'Tilføj et hint' }))
    expect(within(panel).getByLabelText('Hint')).toBeInTheDocument()
  })

  it('offers "Synlig fra start" or the locks and doors the object can wait for', async () => {
    mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Døren til Biblioteket (vises senere)' }))
    const select = within(screen.getByRole('complementary')).getByLabelText('Hvornår kan eleverne se den?')
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'Synlig fra start',
      'Vises når Låsen „Hvad er 3 · 4?“ er løst',
    ])
    expect(select).toHaveValue('lock')

    // The lock can't wait for the door, which already waits for it.
    fireEvent.click(screen.getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    const lockSelect = within(screen.getByRole('complementary')).getByLabelText('Hvornår kan eleverne se den?')
    expect(within(lockSelect).getAllByRole('option').map((o) => o.textContent)).toEqual(['Synlig fra start'])
  })

  it('saves a change by itself, and undo takes it back and saves again', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Tilføj Kodelås' }))
    const panel = screen.getByRole('complementary')
    expect(within(panel).getByRole('heading', { name: 'Lås 2' })).toBeInTheDocument()

    await waitFor(() => {
      expect(saved).toHaveLength(1)
    }, saveWait)
    expect(saved[0].revision).toBe(3)
    expect(saved[0].scenes[0].objects.map((o) => o.graphicKey)).toEqual([
      'builtin:padlock',
      'builtin:door',
      'builtin:code-lock',
    ])
    expect(await screen.findByText('Gemt')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Fortryd' }))
    expect(screen.queryByRole('button', { name: 'Lås 2' })).not.toBeInTheDocument()
    await waitFor(() => {
      expect(saved).toHaveLength(2)
    }, saveWait)
    expect(saved[1].revision).toBe(4)
    expect(saved[1].scenes[0].objects).toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: 'Gentag' }))
    expect(within(screen.getByRole('region', { name: 'Scenen Gangen' })).getByRole('button', { name: 'Lås 2' })).toBeInTheDocument()
  })

  it('saves the story text of a scene, and none when it is left empty', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.change(await screen.findByLabelText('Fortællertekst'), { target: { value: 'Det er 1943.' } })

    await waitFor(() => {
      expect(saved).toHaveLength(1)
    }, saveWait)
    const [hall, library] = saved[0].scenes as unknown as { narration: string | null }[]
    expect(hall.narration).toBe('Det er 1943.')
    expect(library.narration).toBeNull()
    expect(within(screen.getByRole('navigation', { name: 'Scener' })).getByText('Fortælling')).toBeInTheDocument()
  })

  it('adds a text box whose text is written straight onto the scene', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Tilføj Tekstfelt' }))
    const panel = within(screen.getByRole('complementary'))
    expect(panel.getByRole('heading', { name: 'Tekstfelt 1' })).toBeInTheDocument()
    fireEvent.change(panel.getByLabelText('Tekst'), { target: { value: 'Vagten er gået til frokost.' } })

    const stage = within(screen.getByRole('region', { name: 'Scenen Gangen' }))
    expect(stage.getByRole('button', { name: 'Tekstfeltet „Vagten er gået til frokost.“' })).toHaveTextContent(
      'Vagten er gået til frokost.',
    )
    await waitFor(() => {
      expect(saved).toHaveLength(1)
    }, saveWait)
    const sentObjects = saved[0].scenes[0].objects as unknown as { type: string; text?: string; graphicKey: string }[]
    expect(sentObjects.at(-1)).toMatchObject({ type: 'TextBox', text: 'Vagten er gået til frokost.', graphicKey: 'builtin:text-box' })
  })

  it('waits with saving while the title is empty', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.change(await screen.findByLabelText('Titel'), { target: { value: ' ' } })

    expect(screen.getByText('Rummet skal have en titel.')).toBeInTheDocument()
    expect(screen.getByText('Ikke gemt – udfyld de tomme felter')).toBeInTheDocument()
    await new Promise((resolve) => setTimeout(resolve, 1000))
    expect(saved).toHaveLength(0)
  })

  it('tells the teacher to reload when the room was saved somewhere else', async () => {
    mockEditor(editorRoomJson(), () => json({ code: 'ROOM_CHANGED' }, 409))
    renderEditor()

    fireEvent.change(await screen.findByLabelText('Titel'), { target: { value: 'Ny titel' } })

    expect(await screen.findByText(/Rummet er blevet ændret et andet sted/, undefined, saveWait)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Genindlæs' })).toBeInTheDocument()
  })

  it('lists what is missing and goes straight to the object', async () => {
    mockEditor(
      editorRoomJson(
        editorRoom({ validationErrors: [{ code: 'DOOR_TARGET_MISSING', objectId: 'door' }] }),
      ),
    )
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: /Døren fører ikke til nogen scene/ }))

    expect(within(screen.getByRole('complementary')).getByLabelText('Døren fører til')).toBeInTheDocument()
    // A room with problems can't be started yet, so the editor doesn't offer it.
    expect(screen.queryByRole('button', { name: 'Start session' })).not.toBeInTheDocument()
  })

  it('goes straight on to starting a session once nothing is missing', async () => {
    mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Start session' }))

    expect(await screen.findByRole('heading', { name: 'Start en session' })).toBeInTheDocument()
  })

  it('lists each unfinished object once, with all its problems', async () => {
    mockEditor(
      editorRoomJson(
        editorRoom({
          validationErrors: [
            { code: 'LOCK_QUESTION_MISSING', objectId: 'lock' },
            { code: 'LOCK_ANSWER_MISSING', objectId: 'lock' },
            { code: 'DOOR_TARGET_MISSING', objectId: 'door' },
          ],
        }),
      ),
    )
    renderEditor()

    const list = within(await screen.findByRole('region', { name: 'Det mangler, før rummet kan spilles' }))
    const items = list.getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Låsen mangler et spørgsmål. Låsen mangler det rigtige svar.')
  })

  it('removes a scene with the cross in its corner, and offers to undo it', async () => {
    mockEditor()
    renderEditor()

    const strip = within(await screen.findByRole('navigation', { name: 'Scener' }))
    fireEvent.click(strip.getByRole('button', { name: 'Slet Biblioteket' }))

    expect(strip.queryByRole('button', { name: /Biblioteket/ })).not.toBeInTheDocument()
    // The last scene can't be removed.
    expect(strip.queryByRole('button', { name: 'Slet Gangen' })).not.toBeInTheDocument()
    const note = screen.getByText('Biblioteket er slettet.')
    fireEvent.click(within(note).getByRole('button', { name: 'Fortryd' }))

    expect(strip.getByRole('button', { name: 'Slet Biblioteket' })).toBeInTheDocument()
    expect(screen.queryByText('Biblioteket er slettet.')).not.toBeInTheDocument()
    expect(screen.getByText('Gangen fører til Biblioteket')).toBeInTheDocument()
  })

  it('removes an object, and undo brings it back as it was', async () => {
    mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    fireEvent.click(screen.getByRole('button', { name: 'Slet låsen' }))
    const stage = screen.getByRole('region', { name: 'Scenen Gangen' })
    expect(within(stage).queryByRole('button', { name: /Låsen/ })).not.toBeInTheDocument()
    // The door no longer waits for anything.
    expect(within(stage).getByRole('button', { name: 'Døren til Biblioteket' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Fortryd' }))
    expect(within(stage).getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' })).toBeInTheDocument()
    expect(within(stage).getByRole('button', { name: 'Døren til Biblioteket (vises senere)' })).toBeInTheDocument()
  })

  it('moves the selected object with the arrow keys', async () => {
    mockEditor()
    renderEditor()

    const lock = await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' })
    fireEvent.keyDown(lock, { key: 'ArrowRight' })
    fireEvent.keyDown(lock, { key: 'ArrowDown', shiftKey: true })

    expect(lock.style.left).toBe('31%')
    expect(lock.style.top).toBe('45%')
  })

  it('shows images it does not know as the defaults, and saves them that way', async () => {
    const room = editorRoom()
    room.scenes[0] = {
      ...room.scenes[0],
      backgroundImageKey: 'demo/classroom.jpg',
      objects: room.scenes[0].objects.map((o) => ({ ...o, graphicKey: 'demo/old.png' })),
    }
    const saved = mockEditor(editorRoomJson(room))
    renderEditor()

    fireEvent.change(await screen.findByLabelText('Titel'), { target: { value: 'Ny titel' } })

    await waitFor(() => {
      expect(saved).toHaveLength(1)
    }, saveWait)
    expect(saved[0].scenes[0].backgroundImageKey).toBe('builtin:classroom')
    expect(saved[0].scenes[0].objects.map((o) => o.graphicKey)).toEqual(['builtin:padlock', 'builtin:door'])
  })
})
