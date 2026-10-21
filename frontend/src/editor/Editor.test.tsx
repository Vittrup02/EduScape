import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveDeviceToken } from '../api/deviceStorage'
import { saveAuth } from '../api/tokenStorage'
import { AppRoutes } from '../App'
import { editorRoom, editorRoomJson, editorStep } from '../test/editorRoom'
import { json, mockFetch, urlOf } from '../test/fetchMock'

const editorPath = '/api/teacher/rooms/room/editor'
const saveWait = { timeout: 3000 }

interface SentStep {
  id: string
  question: string
  answerType: string
  correctAnswer: string | null
  tolerance: number | null
  options: string[] | null
  acceptedAnswers: string[] | null
  acceptTypos: boolean
  orderItems: { id: string; text: string; imageKey: string | null }[] | null
}

interface SentObject {
  id: string
  type: string
  graphicKey: string
  steps?: SentStep[]
  openCondition?: string
  requiredLockIds?: string[]
}

interface SentRoom {
  revision: number
  title: string
  wrongAnswerCooldownSeconds: number
  scenes: { id: string; name: string; backgroundImageKey: string; objects: SentObject[] }[]
}

/** What the editor last saved; waits for the save after the latest change. */
async function lastSave(saved: SentRoom[], count = 1): Promise<SentRoom> {
  await waitFor(() => {
    expect(saved.length).toBeGreaterThanOrEqual(count)
  }, saveWait)
  return saved[saved.length - 1]
}

function sentObject(room: SentRoom, id: string): SentObject | undefined {
  return room.scenes.flatMap((s) => s.objects).find((o) => o.id === id)
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
    saveDeviceToken(null)
  })

  it('shows the scene, its objects and the map of doors', async () => {
    mockEditor()
    renderEditor()

    const stage = await screen.findByRole('region', { name: 'Scenen Gangen' })
    expect(within(stage).getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' })).toBeInTheDocument()
    expect(within(stage).getByRole('button', { name: 'Døren til Biblioteket' })).toBeInTheDocument()
    expect(screen.getByText('Gangen fører til Biblioteket')).toBeInTheDocument()
    expect(screen.getByLabelText('Navn')).toHaveValue('Gangen')
    expect(screen.getByText('Gemt')).toBeInTheDocument()
  })

  it('opens a small panel with only the settings that matter for the lock', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    const panel = within(screen.getByRole('complementary'))
    expect(panel.getByLabelText('Spørgsmål')).toHaveValue('Hvad er 3 · 4?')
    expect(panel.getByLabelText('Det rigtige svar')).toHaveValue('12')
    expect(panel.getByRole('checkbox', { name: 'Præcis kode' })).toBeChecked()
    expect(panel.queryByLabelText(/Godkend også svar/)).not.toBeInTheDocument()

    // Tolerance only for typed answers that are numbers, written "± 1,0" with a comma or a point.
    fireEvent.click(panel.getByRole('checkbox', { name: 'Præcis kode' }))
    const tolerance = panel.getByLabelText('Godkend også svar inden for')
    fireEvent.change(tolerance, { target: { value: '1,0' } })
    expect(panel.getByText('Svar fra 11,0 til 13,0 godkendes.')).toBeInTheDocument()
    fireEvent.change(tolerance, { target: { value: '0.5' } })
    expect(panel.getByText('Svar fra 11,5 til 12,5 godkendes.')).toBeInTheDocument()
    expect(sentObject(await lastSave(saved), 'lock')?.steps?.[0]).toMatchObject({ answerType: 'Text', tolerance: 0.5 })

    // A word instead: synonyms and small spelling mistakes, but no tolerance.
    fireEvent.change(panel.getByLabelText('Det rigtige svar'), { target: { value: 'Paris' } })
    expect(panel.queryByLabelText('Godkend også svar inden for')).not.toBeInTheDocument()
    expect(panel.getByRole('checkbox', { name: 'Accepter små stavefejl' })).not.toBeChecked()

    // Multiple choice starts with the answer already typed as the first option.
    fireEvent.click(panel.getByRole('radio', { name: /^Multiple choice/ }))
    expect(panel.getByLabelText('Svarmulighed 1')).toHaveValue('Paris')
    expect(panel.getByRole('radio', { name: 'Svarmulighed 1 er det rigtige svar' })).toBeChecked()

    // The hint field appears only when asked for.
    expect(panel.queryByLabelText('Hint')).not.toBeInTheDocument()
    fireEvent.click(panel.getByRole('button', { name: 'Tilføj et hint' }))
    expect(panel.getByLabelText('Hint')).toBeInTheDocument()
  })

  it('accepts synonyms and, if the teacher wants, small spelling mistakes', async () => {
    const saved = mockEditor(
      editorRoomJson(
        editorRoom({
          scenes: editorRoom().scenes.map((scene) => ({
            ...scene,
            objects: scene.objects.map((o) =>
              o.type === 'Lock' ? { ...o, steps: [editorStep({ answerType: 'Text', correctAnswer: 'Natrium' })] } : o,
            ),
          })),
        }),
      ),
    )
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    const panel = within(screen.getByRole('complementary'))
    fireEvent.click(panel.getByRole('checkbox', { name: 'Accepter små stavefejl' }))
    fireEvent.click(panel.getByRole('button', { name: 'Tilføj et andet rigtigt svar' }))
    fireEvent.change(panel.getByLabelText('Andet rigtigt svar 1'), { target: { value: 'Na' } })

    await waitFor(async () => {
      expect(sentObject(await lastSave(saved), 'lock')?.steps?.[0]).toMatchObject({
        acceptTypos: true,
        acceptedAnswers: ['Na'],
      })
    }, saveWait)
  })

  it('asks for the kind of puzzle on four cards when a lock is placed', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Tilføj Lås' }))
    const panel = within(screen.getByRole('complementary'))
    fireEvent.click(panel.getByRole('radio', { name: 'Kiste' }))
    const cards = within(panel.getByRole('region', { name: 'Hvilken slags gåde skal låsen have?' }))
    expect(cards.getAllByRole('button').map((b) => b.querySelector('.puzzle-card-name')?.textContent)).toEqual([
      'Multiple choice',
      'Skriv svaret',
      'Træk i rækkefølge',
      'Flere trin',
    ])

    fireEvent.click(cards.getByRole('button', { name: /^Flere trin/ }))
    const steps = within(panel.getByRole('list', { name: 'Trin' }))
    expect(steps.getByRole('button', { name: /^Trin 1/ })).toHaveAttribute('aria-expanded', 'true')
    expect(steps.getByRole('button', { name: /^Trin 2/ })).toHaveAttribute('aria-expanded', 'false')
    fireEvent.change(panel.getByLabelText('Spørgsmål'), { target: { value: 'Hvad er Na?' } })

    // A step of its own kind; the other steps keep theirs.
    fireEvent.click(steps.getByRole('button', { name: /^Trin 2/ }))
    fireEvent.click(panel.getByRole('radio', { name: /^Multiple choice/ }))
    fireEvent.click(panel.getByRole('button', { name: 'Tilføj et trin' }))

    const room = await lastSave(saved)
    const chest = room.scenes[0].objects.at(-1)
    expect(chest?.graphicKey).toBe('builtin:chest')
    expect(chest?.steps?.map((s) => [s.question, s.answerType])).toEqual([
      ['Hvad er Na?', 'Text'],
      ['', 'MultipleChoice'],
      ['', 'Text'],
    ])
  })

  it('sorts the steps of a lock by dragging (or with the arrow keys)', async () => {
    const saved = mockEditor(
      editorRoomJson(
        editorRoom({
          scenes: editorRoom().scenes.map((scene) => ({
            ...scene,
            objects: scene.objects.map((o) =>
              o.type === 'Lock'
                ? { ...o, steps: [editorStep({ id: 'one' }), editorStep({ id: 'two', question: 'Hvad er 12 + 5?' })] }
                : o,
            ),
          })),
        }),
      ),
    )
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    fireEvent.keyDown(screen.getByRole('button', { name: 'Flyt Trin 2' }), { key: 'ArrowUp' })

    const room = await lastSave(saved)
    expect(sentObject(room, 'lock')?.steps?.map((s) => s.id)).toEqual(['two', 'one'])
    expect(screen.getByRole('heading', { name: 'Låsen „Hvad er 12 + 5?“' })).toBeInTheDocument()
  })

  it('makes the order of the list the correct order of an order puzzle', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Tilføj Lås' }))
    const panel = within(screen.getByRole('complementary'))
    fireEvent.click(panel.getByRole('button', { name: /^Træk i rækkefølge/ }))
    const items = within(panel.getByRole('list', { name: 'Elementer i den rigtige rækkefølge' }))
    fireEvent.change(items.getByLabelText('Element 1'), { target: { value: 'Opvarmning' } })
    fireEvent.change(items.getByLabelText('Element 2'), { target: { value: 'Stofferne blandes' } })
    fireEvent.change(items.getByLabelText('Element 3'), { target: { value: 'Produkt dannes' } })
    // No pictures without image storage.
    expect(panel.queryByText('Tilføj billede')).not.toBeInTheDocument()

    // "Stofferne blandes" comes first: drag it to the top.
    fireEvent.keyDown(items.getByRole('button', { name: 'Flyt Stofferne blandes' }), { key: 'ArrowUp' })
    expect(items.getByLabelText('Element 1')).toHaveValue('Stofferne blandes')

    const room = await lastSave(saved)
    const step = room.scenes[0].objects.at(-1)?.steps?.[0]
    expect(step?.answerType).toBe('Order')
    expect(step?.orderItems?.map((i) => i.text)).toEqual(['Stofferne blandes', 'Opvarmning', 'Produkt dannes'])
    // Random ids (UUID v4), which say nothing about the order.
    expect(step?.orderItems?.every((i) => /^[0-9a-f]{8}-[0-9a-f]{4}-4/.test(i.id))).toBe(true)
  })

  it('changes the graphic of a lock without touching its puzzle', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    fireEvent.click(within(screen.getByRole('complementary')).getByRole('radio', { name: 'Pengeskab' }))

    const lock = sentObject(await lastSave(saved), 'lock')
    expect(lock?.graphicKey).toBe('builtin:safe')
    expect(lock?.steps?.map((s) => [s.question, s.correctAnswer])).toEqual([['Hvad er 3 · 4?', '12']])
  })

  it('opens a door when every puzzle in its scene is solved, or when the chosen locks are', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Døren til Biblioteket' }))
    const panel = within(screen.getByRole('complementary'))
    // Doors never wait to be shown; they are there from the start and open by themselves.
    expect(panel.queryByLabelText('Hvornår kan eleverne se den?')).not.toBeInTheDocument()
    expect(panel.getByRole('radio', { name: 'Når alle gåder i scenen er løst' })).toBeChecked()

    fireEvent.click(panel.getByRole('radio', { name: 'Når udvalgte låse er løst' }))
    fireEvent.click(panel.getByRole('checkbox', { name: 'Låsen „Hvad er 3 · 4?“' }))

    expect(sentObject(await lastSave(saved), 'door')).toMatchObject({
      openCondition: 'SelectedLocks',
      requiredLockIds: ['lock'],
    })
  })

  it('offers "Synlig fra start" or the locks and doors the object can wait for', async () => {
    mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Tilføj Brev' }))
    const select = within(screen.getByRole('complementary')).getByLabelText('Hvornår kan eleverne se den?')
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'Synlig fra start',
      'Vises når Låsen „Hvad er 3 · 4?“ er løst',
      'Vises når Døren til Biblioteket er åbnet',
    ])

    // The lock can't wait for the door, which opens once the lock is solved.
    fireEvent.click(screen.getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    const lockSelect = within(screen.getByRole('complementary')).getByLabelText('Hvornår kan eleverne se den?')
    expect(within(lockSelect).getAllByRole('option').map((o) => o.textContent)).toEqual(['Synlig fra start'])
  })

  it('sets how long locks wait after a wrong answer, for the whole room', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    const select = within(screen.getByRole('complementary')).getByLabelText('Ventetid efter forkert svar')
    expect(select).toHaveValue('15')
    fireEvent.change(select, { target: { value: '0' } })

    expect((await lastSave(saved)).wrongAnswerCooldownSeconds).toBe(0)
  })

  it('saves a change by itself, and undo takes it back and saves again', async () => {
    const saved = mockEditor()
    renderEditor()

    fireEvent.click(await screen.findByRole('button', { name: 'Tilføj Lås' }))
    const panel = screen.getByRole('complementary')
    expect(within(panel).getByRole('heading', { name: 'Lås 2' })).toBeInTheDocument()

    await waitFor(() => {
      expect(saved).toHaveLength(1)
    }, saveWait)
    expect(saved[0].revision).toBe(3)
    expect(saved[0].scenes[0].objects.map((o) => o.graphicKey)).toEqual([
      'builtin:padlock',
      'builtin:door',
      'builtin:padlock',
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
        editorRoom({ validationErrors: [{ code: 'DOOR_TARGET_MISSING', objectId: 'door', stepIndex: null }] }),
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
            { code: 'LOCK_QUESTION_MISSING', objectId: 'lock', stepIndex: 0 },
            { code: 'LOCK_ANSWER_MISSING', objectId: 'lock', stepIndex: 0 },
            { code: 'DOOR_TARGET_MISSING', objectId: 'door', stepIndex: null },
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

  it('says which step of a lock a problem is in', async () => {
    const room = editorRoom({
      validationErrors: [{ code: 'LOCK_ANSWER_MISSING', objectId: 'lock', stepIndex: 1 }],
      scenes: editorRoom().scenes.map((scene) => ({
        ...scene,
        objects: scene.objects.map((o) =>
          o.type === 'Lock' ? { ...o, steps: [editorStep(), editorStep({ id: 'second', correctAnswer: '' })] } : o,
        ),
      })),
    })
    mockEditor(editorRoomJson(room))
    renderEditor()

    const list = within(await screen.findByRole('region', { name: 'Det mangler, før rummet kan spilles' }))
    expect(list.getByRole('listitem')).toHaveTextContent('Trin 2: Låsen mangler det rigtige svar.')
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

  it('gives new scenes readable names and sorts them by dragging', async () => {
    const saved = mockEditor()
    renderEditor()

    const strip = within(await screen.findByRole('navigation', { name: 'Scener' }))
    fireEvent.click(strip.getByRole('button', { name: 'Ny scene' }))
    expect(screen.getByLabelText('Navn')).toHaveValue('Scene 3')

    // The arrow keys on a scene do what dragging does.
    fireEvent.keyDown(strip.getByRole('button', { name: /^Scene 3/ }), { key: 'ArrowLeft' })
    fireEvent.keyDown(strip.getByRole('button', { name: /^Scene 3/ }), { key: 'ArrowLeft' })

    const room = await lastSave(saved)
    expect(room.scenes.map((s) => s.name)).toEqual(['Scene 3', 'Gangen', 'Biblioteket'])
  })

  it('removes an object, and undo brings it back as it was', async () => {
    const saved = mockEditor()
    renderEditor()
    fireEvent.click(await screen.findByRole('button', { name: 'Døren til Biblioteket' }))
    const panel = within(screen.getByRole('complementary'))
    fireEvent.click(panel.getByRole('radio', { name: 'Når udvalgte låse er løst' }))
    fireEvent.click(panel.getByRole('checkbox', { name: 'Låsen „Hvad er 3 · 4?“' }))

    fireEvent.click(screen.getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' }))
    fireEvent.click(screen.getByRole('button', { name: 'Slet låsen' }))
    const stage = screen.getByRole('region', { name: 'Scenen Gangen' })
    expect(within(stage).queryByRole('button', { name: /Låsen/ })).not.toBeInTheDocument()
    // The door no longer waits for it.
    expect(sentObject(await lastSave(saved), 'door')?.requiredLockIds).toEqual([])

    fireEvent.click(screen.getByRole('button', { name: 'Fortryd' }))
    expect(within(stage).getByRole('button', { name: 'Låsen „Hvad er 3 · 4?“' })).toBeInTheDocument()
    await waitFor(async () => {
      expect(sentObject(await lastSave(saved), 'door')?.requiredLockIds).toEqual(['lock'])
    }, saveWait)
  })

  it('previews the room from the scene the teacher is working on, or from the start', async () => {
    mockEditor()
    const previews: unknown[] = []
    const fetchMock = vi.mocked(fetch)
    const editorFetch = fetchMock.getMockImplementation()
    fetchMock.mockImplementation(async (input, init) => {
      if (new URL(urlOf(input)).pathname === '/api/teacher/rooms/room/preview') {
        previews.push(JSON.parse(init?.body as string))
        return json({ deviceToken: 'preview-token' })
      }
      return (editorFetch as typeof fetch)(input, init)
    })
    const tab = { location: { href: '' }, close: vi.fn() }
    vi.stubGlobal('open', vi.fn(() => tab))
    renderEditor()

    // In the start scene there is only one way to start.
    await screen.findByRole('region', { name: 'Scenen Gangen' })
    expect(screen.queryByRole('button', { name: 'Start forfra' })).not.toBeInTheDocument()

    fireEvent.click(within(screen.getByRole('navigation', { name: 'Scener' })).getByRole('button', { name: /^Biblioteket/ }))
    expect(screen.getByText('Starter i Biblioteket. Låsene i scenerne før er løst.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Prøv rummet' }))
    await waitFor(() => {
      expect(tab.location.href).toBe('/play')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Start forfra' }))

    await waitFor(() => {
      expect(previews).toEqual([{ startSceneId: 'library' }, { startSceneId: null }])
    })
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
