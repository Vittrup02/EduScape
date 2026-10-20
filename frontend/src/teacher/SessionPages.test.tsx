import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type {
  DashboardParticipant,
  DashboardTeam,
  SessionDashboard,
  SessionDetails,
  TeamColor,
} from '../api/sessions'
import { saveAuth } from '../api/tokenStorage'
import { AppRoutes } from '../App'
import { json, mockFetch, urlOf } from '../test/fetchMock'
import { configureNextTeacherHub, latestTeacherHub } from '../test/teacherHubMock'
import { SessionPage } from './SessionPage'

vi.mock('../api/teacherHub', () => import('../test/teacherHubMock'))

const room = { id: 'r1', title: 'Brøkernes hemmelighed', subject: 'Mathematics', gradeLevel: 'Grade7' }

const session: SessionDetails = {
  id: 's1',
  pin: '042042',
  mode: 'Team',
  status: 'Open',
  roomTitle: 'Brøkernes hemmelighed',
  className: '9.A',
  startedAt: '2026-10-19T08:00:00Z',
  expiresAt: '2026-10-19T20:00:00Z',
  teams: [
    { id: 'red', color: 'Red', participants: [{ id: 'p1', nickname: 'Ida' }, { id: 'p2', nickname: null }] },
    { id: 'blue', color: 'Blue', participants: [] },
  ],
}

const serverNow = '2026-10-19T08:30:00Z'

function minutesBeforeNow(minutes: number): string {
  return new Date(Date.parse(serverNow) - minutes * 60_000).toISOString()
}

function student(id: string, nickname: string | null, sceneId = 'classroom'): DashboardParticipant {
  return { id, nickname, sceneId, sceneEnteredAt: minutesBeforeNow(3) }
}

function team(id: string, color: TeamColor, overrides: Partial<DashboardTeam> = {}): DashboardTeam {
  return {
    id,
    color,
    solvedLockIds: [],
    escapedAt: null,
    currentLock: null,
    participantCount: 0,
    participants: [],
    ...overrides,
  }
}

/** Red: Ida in the library and a student without a name; one of two locks solved. Blue: nobody yet. */
const dashboard: SessionDashboard = {
  id: 's1',
  pin: '042042',
  mode: 'Team',
  status: 'Open',
  roomTitle: 'Brøkernes hemmelighed',
  className: '9.A',
  startedAt: '2026-10-19T08:00:00Z',
  expiresAt: '2026-10-19T20:00:00Z',
  closedAt: null,
  serverNow,
  scenes: [
    { id: 'classroom', name: 'Klasselokalet' },
    { id: 'library', name: 'Biblioteket' },
  ],
  locks: [
    { id: 'lock-1', sceneId: 'classroom', question: 'Hvad giver regnestykket?' },
    { id: 'lock-2', sceneId: 'classroom', question: 'Hvad er 10 / 3?' },
  ],
  teams: [
    team('red', 'Red', {
      solvedLockIds: ['lock-1'],
      participantCount: 2,
      participants: [student('p1', 'Ida', 'library'), student('p2', null)],
    }),
    team('blue', 'Blue'),
  ],
}

/** The dashboard with one team changed. */
function withTeam(id: string, changes: Partial<DashboardTeam>): SessionDashboard {
  return { ...dashboard, teams: dashboard.teams.map((t) => (t.id === id ? { ...t, ...changes } : t)) }
}

function teamCard(name: string): HTMLElement {
  const card = screen.getByRole('heading', { name }).closest('li')
  if (card === null) {
    throw new Error(`No card for ${name}`)
  }
  return card
}

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
    </MemoryRouter>,
  )
}

/** The dashboard alone, retrying at once instead of waiting for the backend. */
function renderDashboard() {
  render(
    <MemoryRouter initialEntries={['/teacher/sessions/s1']}>
      <Routes>
        <Route path="/teacher/sessions/:sessionId" element={<SessionPage retryDelayMs={0} />} />
      </Routes>
    </MemoryRouter>,
  )
}

function bodyOf(init: RequestInit | undefined): unknown {
  return JSON.parse(init?.body as string)
}

describe('starting a session', () => {
  beforeEach(() => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('asks for the number of teams only in team mode', async () => {
    mockFetch({ '/api/teacher/rooms/r1': () => json(room), '/api/teacher/classes': () => json([]) })
    renderAt('/teacher/rooms/r1/start')

    expect(await screen.findByText('Brøkernes hemmelighed')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Antal hold' })).toHaveTextContent('4')

    fireEvent.click(screen.getByRole('radio', { name: /Hver for sig/ }))
    expect(screen.queryByRole('group', { name: 'Antal hold' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('radio', { name: /På hold/ }))
    expect(screen.getByRole('group', { name: 'Antal hold' })).toBeInTheDocument()
  })

  it('keeps the number of teams between 2 and 8', async () => {
    mockFetch({ '/api/teacher/rooms/r1': () => json(room), '/api/teacher/classes': () => json([]) })
    renderAt('/teacher/rooms/r1/start')
    const fewer = await screen.findByRole('button', { name: 'Færre hold' })
    const more = screen.getByRole('button', { name: 'Flere hold' })

    fireEvent.click(fewer)
    fireEvent.click(fewer)
    expect(screen.getByRole('group', { name: 'Antal hold' })).toHaveTextContent('2')
    expect(fewer).toBeDisabled()

    for (let i = 0; i < 10; i++) {
      fireEvent.click(more)
    }
    expect(screen.getByRole('group', { name: 'Antal hold' })).toHaveTextContent('8')
    expect(more).toBeDisabled()
  })

  it('starts the session for a template and opens it', async () => {
    const fetchMock = mockFetch({
      '/api/teacher/templates/t1': () => json({ ...room, id: 't1' }),
      '/api/teacher/classes': () => json(['9.A', '8.B']),
      '/api/teacher/sessions': () => json(session, 201),
      '/api/teacher/sessions/s1/dashboard': () => json(dashboard),
    })
    renderAt('/teacher/templates/t1/start')

    fireEvent.change(await screen.findByLabelText('Klasse'), { target: { value: '9.A' } })
    fireEvent.click(screen.getByRole('button', { name: 'Flere hold' }))
    fireEvent.click(screen.getByRole('button', { name: 'Start session' }))

    expect(await screen.findByText('042042')).toBeInTheDocument()
    const start = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')
    expect(bodyOf(start?.[1])).toEqual({ roomId: 't1', className: '9.A', mode: 'Team', teamCount: 5 })
  })

  it('suggests the classes the teacher has used before', async () => {
    mockFetch({ '/api/teacher/rooms/r1': () => json(room), '/api/teacher/classes': () => json(['8.B', '9.A']) })
    renderAt('/teacher/rooms/r1/start')

    const input = await screen.findByLabelText('Klasse')
    const options = document.querySelectorAll(`datalist#${CSS.escape(input.getAttribute('list') ?? '')} option`)
    expect(Array.from(options, (option) => option.getAttribute('value'))).toEqual(['8.B', '9.A'])
  })

  it('shows a translated error when the class is missing', async () => {
    mockFetch({
      '/api/teacher/rooms/r1': () => json(room),
      '/api/teacher/classes': () => json([]),
      '/api/teacher/sessions': () => json({ code: 'VALUE_REQUIRED' }, 400),
    })
    renderAt('/teacher/rooms/r1/start')

    fireEvent.click(await screen.findByRole('button', { name: 'Start session' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Feltet skal udfyldes.')
  })
})

describe('the live dashboard', () => {
  beforeEach(() => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the PIN, where to join and every team with its progress and students', async () => {
    mockFetch({ '/api/teacher/sessions/s1/dashboard': () => json(dashboard) })
    renderAt('/teacher/sessions/s1')

    expect(await screen.findByLabelText('PIN-kode')).toHaveTextContent('042042')
    expect(screen.getByText(`Eleverne går ind på ${window.location.origin}/join og skriver PIN-koden.`)).toBeInTheDocument()
    const red = teamCard('Hold Rød')
    expect(red).toHaveTextContent('1 af 2 låse løst')
    expect(within(red).getByText('Ida')).toBeInTheDocument()
    expect(within(red).getByText('i Biblioteket')).toBeInTheDocument()
    expect(within(red).getByText('Uden navn')).toBeInTheDocument()
    expect(within(teamCard('Hold Blå')).getByText('Ingen elever endnu.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Opdater' })).not.toBeInTheDocument()
  })

  it('wakes the backend as soon as it opens', async () => {
    const fetchMock = mockFetch({
      '/health': () => new Response(null, { status: 200 }),
      '/api/teacher/sessions/s1/dashboard': () => json(dashboard),
    })
    renderAt('/teacher/sessions/s1')

    await screen.findByLabelText('PIN-kode')
    expect(fetchMock.mock.calls.some(([url]) => new URL(urlOf(url)).pathname === '/health')).toBe(true)
  })

  it('watches the session and fetches it again whenever the server says it changed', async () => {
    let current = dashboard
    mockFetch({ '/api/teacher/sessions/s1/dashboard': () => json(current) })
    renderAt('/teacher/sessions/s1')
    await screen.findByLabelText('PIN-kode')
    await waitFor(() => {
      expect(latestTeacherHub().watchedSessions).toEqual(['s1'])
    })

    current = withTeam('blue', { participants: [student('p3', 'Bo')], participantCount: 1 })
    act(() => {
      latestTeacherHub().handlers.dashboardChanged()
    })

    expect(await within(teamCard('Hold Blå')).findByText('Bo')).toBeInTheDocument()
  })

  it('shows "connecting" until the realtime connection is up, and keeps trying', async () => {
    configureNextTeacherHub({ startFailures: 2 })
    mockFetch({ '/api/teacher/sessions/s1/dashboard': () => json(dashboard) })
    renderDashboard()

    expect(await screen.findByText(/Forbinder til live-overblikket/)).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.queryByText(/Forbinder til live-overblikket/)).not.toBeInTheDocument()
    })
    expect(latestTeacherHub().watchedSessions).toEqual(['s1'])
  })

  it('shows "connecting" while the connection is lost and catches up afterwards', async () => {
    let current = dashboard
    mockFetch({ '/api/teacher/sessions/s1/dashboard': () => json(current) })
    renderAt('/teacher/sessions/s1')
    await screen.findByLabelText('PIN-kode')
    await waitFor(() => {
      expect(screen.queryByText(/Forbinder til live-overblikket/)).not.toBeInTheDocument()
    })

    act(() => {
      latestTeacherHub().drop()
    })
    expect(screen.getByText(/Forbinder til live-overblikket/)).toBeInTheDocument()

    current = withTeam('red', { solvedLockIds: ['lock-1', 'lock-2'] })
    act(() => {
      latestTeacherHub().restore()
    })

    expect(await screen.findByText('2 af 2 låse løst')).toBeInTheDocument()
    expect(screen.queryByText(/Forbinder til live-overblikket/)).not.toBeInTheDocument()
  })

  it('tells which lock a team is working on, and warns once it has been stuck for 5 minutes', async () => {
    let current = withTeam('red', {
      currentLock: { lockId: 'lock-2', stuckSince: minutesBeforeNow(2), wrongAttempts: 1 },
    })
    mockFetch({ '/api/teacher/sessions/s1/dashboard': () => json(current) })
    renderAt('/teacher/sessions/s1')

    const working = await screen.findByText(/Arbejder på „Hvad er 10 \/ 3\?“/)
    expect(working).toHaveTextContent('· 1 forkert svar')
    expect(working).not.toHaveAttribute('data-stuck')

    current = withTeam('red', {
      currentLock: { lockId: 'lock-2', stuckSince: minutesBeforeNow(7), wrongAttempts: 4 },
    })
    act(() => {
      latestTeacherHub().handlers.dashboardChanged()
    })

    const stuck = await screen.findByText(/Har siddet fast på „Hvad er 10 \/ 3\?“ i 7 minutter/)
    expect(stuck).toHaveTextContent('· 4 forkerte svar')
    expect(stuck).toHaveAttribute('data-stuck')
  })

  it('moves a student to another team', async () => {
    const fetchMock = mockFetch({
      '/api/teacher/sessions/s1/dashboard': () => json(dashboard),
      '/api/teacher/sessions/s1/participants/p1/move': () => new Response(null, { status: 204 }),
    })
    renderAt('/teacher/sessions/s1')

    fireEvent.change(await screen.findByRole('combobox', { name: 'Flyt Ida til et andet hold' }), {
      target: { value: 'blue' },
    })

    await waitFor(() => {
      const move = fetchMock.mock.calls.find(([url]) => urlOf(url).endsWith('/participants/p1/move'))
      expect(bodyOf(move?.[1])).toEqual({ teamId: 'blue' })
    })
  })

  it('shows the PIN full screen for the board and closes it again', async () => {
    mockFetch({ '/api/teacher/sessions/s1/dashboard': () => json(dashboard) })
    renderAt('/teacher/sessions/s1')

    fireEvent.click(await screen.findByRole('button', { name: 'Vis PIN-koden på tavlen' }))
    expect(screen.getByRole('dialog', { name: 'PIN-kode' })).toHaveTextContent('042042')

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Vis PIN-koden på tavlen' }))
    fireEvent.click(screen.getByRole('button', { name: 'Luk' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('closes the session only after confirming', async () => {
    let current = dashboard
    const fetchMock = mockFetch({
      '/api/teacher/sessions/s1/dashboard': () => json(current),
      '/api/teacher/sessions/s1/close': () => {
        current = { ...dashboard, status: 'Closed' }
        return new Response(null, { status: 204 })
      },
    })
    renderAt('/teacher/sessions/s1')

    fireEvent.click(await screen.findByRole('button', { name: 'Luk session' }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent('Vil du lukke sessionen?')

    // Changing one's mind leaves the session open.
    fireEvent.click(screen.getByRole('button', { name: 'Fortryd' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)

    fireEvent.click(screen.getByRole('button', { name: 'Luk session' }))
    fireEvent.click(screen.getByRole('button', { name: 'Ja, luk sessionen' }))

    expect(await screen.findByText('Sessionen er lukket. PIN-koden virker ikke længere.')).toBeInTheDocument()
    expect(screen.queryByText('042042')).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  })

  it('locks and unlocks the session for new students', async () => {
    let current = dashboard
    mockFetch({
      '/api/teacher/sessions/s1/dashboard': () => json(current),
      '/api/teacher/sessions/s1/lock': () => {
        current = { ...dashboard, status: 'Locked' }
        return new Response(null, { status: 204 })
      },
    })
    renderAt('/teacher/sessions/s1')

    fireEvent.click(await screen.findByRole('button', { name: 'Lås for nye elever' }))

    expect(await screen.findByRole('button', { name: 'Åbn for nye elever' })).toBeInTheDocument()
    expect(screen.getByText(/Låst – nye elever kan ikke deltage\./)).toBeInTheDocument()
  })

  it('shows one card per student in individual mode, without moving', async () => {
    mockFetch({
      '/api/teacher/sessions/s1/dashboard': () =>
        json({
          ...dashboard,
          mode: 'Individual',
          teams: [
            team('a', 'Red', { participants: [student('p1', 'Ida')], participantCount: 1 }),
            team('b', 'Blue', { participants: [student('p2', 'Bo')], participantCount: 1 }),
          ],
        }),
    })
    renderAt('/teacher/sessions/s1')

    expect(await screen.findByRole('heading', { name: 'Elever' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Ida' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Bo' })).toBeInTheDocument()
    expect(screen.queryByText('Hold Rød')).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  })

  it("shows the results without names once the students' data is erased", async () => {
    mockFetch({
      '/api/teacher/sessions/s1/dashboard': () =>
        json({
          ...withTeam('red', { participants: [], participantCount: 2, escapedAt: '2026-10-19T08:23:00Z' }),
          status: 'Closed',
        }),
    })
    renderAt('/teacher/sessions/s1')

    await screen.findByRole('heading', { name: 'Hold Rød' })
    const red = teamCard('Hold Rød')
    expect(red).toHaveTextContent('2 elever deltog')
    expect(red).toHaveTextContent('Brudt ud efter 23 minutter')
  })

  it("shows not found for another teacher's session", async () => {
    mockFetch({})
    renderAt('/teacher/sessions/s1')

    expect(await screen.findByRole('alert')).toHaveTextContent('Det, du leder efter, findes ikke.')
  })
})
