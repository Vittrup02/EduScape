import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionDetails } from '../api/sessions'
import { saveAuth } from '../api/tokenStorage'
import { AppRoutes } from '../App'
import { json, mockFetch } from '../test/fetchMock'

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

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
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
      '/api/teacher/sessions/s1': () => json(session),
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

describe('the session page', () => {
  beforeEach(() => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the PIN, where to join and the teams with their students', async () => {
    mockFetch({ '/api/teacher/sessions/s1': () => json(session) })
    renderAt('/teacher/sessions/s1')

    expect(await screen.findByLabelText('PIN-kode')).toHaveTextContent('042042')
    expect(screen.getByText(`Eleverne går ind på ${window.location.origin}/join og skriver PIN-koden.`)).toBeInTheDocument()
    const red = screen.getByText('Hold Rød').closest('li')
    expect(red).not.toBeNull()
    expect(within(red as HTMLElement).getByText('Ida')).toBeInTheDocument()
    expect(within(red as HTMLElement).getByText('Uden navn')).toBeInTheDocument()
    expect(screen.getByText('Ingen elever endnu.')).toBeInTheDocument()
  })

  it('closes the session only after confirming', async () => {
    let current: SessionDetails = session
    const fetchMock = mockFetch({
      '/api/teacher/sessions/s1': () => json(current),
      '/api/teacher/sessions/s1/close': () => {
        current = { ...session, status: 'Closed' }
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
  })

  it('locks and unlocks the session for new students', async () => {
    let current: SessionDetails = session
    mockFetch({
      '/api/teacher/sessions/s1': () => json(current),
      '/api/teacher/sessions/s1/lock': () => {
        current = { ...session, status: 'Locked' }
        return new Response(null, { status: 204 })
      },
    })
    renderAt('/teacher/sessions/s1')

    fireEvent.click(await screen.findByRole('button', { name: 'Lås for nye elever' }))

    expect(await screen.findByRole('button', { name: 'Åbn for nye elever' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Låst – nye elever kan ikke deltage.')
  })

  it('lists just the students in individual mode', async () => {
    mockFetch({
      '/api/teacher/sessions/s1': () =>
        json({
          ...session,
          mode: 'Individual',
          teams: [
            { id: 'a', color: 'Red', participants: [{ id: 'p1', nickname: 'Ida' }] },
            { id: 'b', color: 'Blue', participants: [{ id: 'p2', nickname: 'Bo' }] },
          ],
        }),
    })
    renderAt('/teacher/sessions/s1')

    expect(await screen.findByRole('heading', { name: 'Elever' })).toBeInTheDocument()
    expect(screen.getByText('Ida')).toBeInTheDocument()
    expect(screen.getByText('Bo')).toBeInTheDocument()
    expect(screen.queryByText('Hold Rød')).not.toBeInTheDocument()
  })

  it("shows not found for another teacher's session", async () => {
    mockFetch({})
    renderAt('/teacher/sessions/s1')

    expect(await screen.findByRole('alert')).toHaveTextContent('Det, du leder efter, findes ikke.')
  })
})
