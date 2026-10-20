import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { loadDeviceToken, saveDeviceToken } from '../api/deviceStorage'
import type { PlayState } from '../api/play'
import { json, mockFetch, urlOf } from '../test/fetchMock'
import { configureNextHub } from '../test/playHubMock'
import { playState } from '../test/playRoom'
import { JoinPage } from './JoinPage'
import { PlayPage } from './PlayPage'

vi.mock('../api/playHub', () => import('../test/playHubMock'))

const teams = [
  { id: 'red', color: 'Red' },
  { id: 'blue', color: 'Blue' },
]

function stateFor(team: { id: string; color: string }, overrides: Partial<PlayState> = {}): PlayState {
  return playState({ id: team.id, color: team.color as PlayState['team']['color'] }, overrides)
}

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/join" element={<JoinPage />} />
        <Route path="/play" element={<PlayPage retryDelayMs={0} />} />
      </Routes>
    </MemoryRouter>,
  )
}

function bodyOf(init: RequestInit | undefined): unknown {
  return JSON.parse(init?.body as string)
}

function enterPin(pin: string) {
  fireEvent.change(screen.getByLabelText('PIN-kode'), { target: { value: pin } })
  fireEvent.click(screen.getByRole('button', { name: 'Videre' }))
}

describe('joining a session', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    saveDeviceToken(null)
  })

  it('goes PIN → team → nickname, keeps the device token and shows the team', async () => {
    const fetchMock = mockFetch({
      '/health': () => new Response(null, { status: 200 }),
      '/api/play/lookup': () => json({ mode: 'Team', teams }),
      '/api/play/join': () => json({ deviceToken: 'token-1', state: stateFor(teams[1]) }),
      '/api/play/reconnect': () => json(stateFor(teams[1])),
    })
    renderAt('/join')

    enterPin('042 042')
    fireEvent.click(await screen.findByRole('button', { name: 'Hold Blå' }))
    fireEvent.change(screen.getByLabelText('Kaldenavn (frivilligt)'), { target: { value: 'Ida' } })
    fireEvent.click(screen.getByRole('button', { name: 'Deltag' }))

    expect(await screen.findByRole('heading', { name: 'Du er på Hold Blå' })).toBeInTheDocument()
    expect(screen.getByText('Du er med som Ida.')).toBeInTheDocument()
    expect(loadDeviceToken()).toBe('token-1')
    const calls = Object.fromEntries(fetchMock.mock.calls.map(([url, init]) => [new URL(urlOf(url)).pathname, init]))
    expect(bodyOf(calls['/api/play/lookup'])).toEqual({ pin: '042042' })
    expect(bodyOf(calls['/api/play/join'])).toEqual({ pin: '042042', teamId: 'blue', nickname: 'Ida' })
    expect(bodyOf(calls['/api/play/reconnect'])).toEqual({ deviceToken: 'token-1' })
  })

  it('skips choosing a team in individual mode', async () => {
    const fetchMock = mockFetch({
      '/api/play/lookup': () => json({ mode: 'Individual', teams: [] }),
      '/api/play/join': () => json({ deviceToken: 'token-2', state: stateFor(teams[0], { mode: 'Individual', nickname: null }) }),
      '/api/play/reconnect': () => json(stateFor(teams[0], { mode: 'Individual', nickname: null })),
    })
    renderAt('/join')

    enterPin('123456')
    fireEvent.click(await screen.findByRole('button', { name: 'Deltag' }))

    expect(await screen.findByRole('heading', { name: 'Du spiller for dig selv' })).toBeInTheDocument()
    const join = fetchMock.mock.calls.find(([url]) => urlOf(url).endsWith('/api/play/join'))
    expect(bodyOf(join?.[1])).toEqual({ pin: '123456', teamId: null, nickname: '' })
  })

  it('can go back from the nickname to the team choice', async () => {
    mockFetch({ '/api/play/lookup': () => json({ mode: 'Team', teams }) })
    renderAt('/join')

    enterPin('042042')
    fireEvent.click(await screen.findByRole('button', { name: 'Hold Rød' }))
    fireEvent.click(screen.getByRole('button', { name: 'Tilbage' }))

    expect(screen.getByRole('heading', { name: 'Vælg dit hold' })).toBeInTheDocument()
  })

  it('shows a translated error for an unknown PIN', async () => {
    mockFetch({ '/api/play/lookup': () => json({ code: 'PIN_NOT_FOUND' }, 404) })
    renderAt('/join')

    enterPin('999999')

    expect(await screen.findByRole('alert')).toHaveTextContent('Der er ingen aktiv session med den PIN-kode.')
    expect(screen.getByLabelText('PIN-kode')).toBeInTheDocument()
  })

  it('sends a device that already joined straight back to its team', async () => {
    saveDeviceToken('token-1')
    const fetchMock = mockFetch({ '/api/play/reconnect': () => json(stateFor(teams[0])) })
    renderAt('/join')

    expect(await screen.findByRole('heading', { name: 'Du er på Hold Rød' })).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url]) => urlOf(url).endsWith('/api/play/lookup'))).toBe(false)
  })
})

describe('the play page', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    saveDeviceToken(null)
  })

  it('sends a device without a token to the join page', () => {
    mockFetch({})
    renderAt('/play')

    expect(screen.getByRole('heading', { name: 'Deltag i et escape room' })).toBeInTheDocument()
  })

  it('shows "connecting" while the backend wakes up and keeps trying', async () => {
    saveDeviceToken('token-1')
    let attempts = 0
    vi.stubGlobal(
      'fetch',
      vi.fn((url: RequestInfo | URL) => {
        if (new URL(urlOf(url)).pathname !== '/api/play/reconnect') {
          return Promise.resolve(new Response(null, { status: 204 }))
        }
        attempts++
        return attempts < 3 ? Promise.reject(new TypeError('Failed to fetch')) : Promise.resolve(json(stateFor(teams[0])))
      }),
    )
    renderAt('/play')

    expect(screen.getByRole('status')).toHaveTextContent('Forbinder...')
    expect(await screen.findByRole('heading', { name: 'Du er på Hold Rød' })).toBeInTheDocument()
    expect(attempts).toBe(3)
  })

  it('forgets the token when the session is closed', async () => {
    saveDeviceToken('token-1')
    mockFetch({ '/api/play/reconnect': () => json({ code: 'SESSION_CLOSED' }, 400) })
    renderAt('/play')

    expect(await screen.findByRole('alert')).toHaveTextContent('Sessionen er lukket.')
    expect(loadDeviceToken()).toBeNull()

    fireEvent.click(screen.getByRole('link', { name: 'Deltag i en ny session' }))
    expect(screen.getByRole('heading', { name: 'Deltag i et escape room' })).toBeInTheDocument()
  })

  it('forgets an unknown token', async () => {
    saveDeviceToken('token-1')
    mockFetch({ '/api/play/reconnect': () => json({ code: 'DEVICE_NOT_RECOGNIZED' }, 404) })
    renderAt('/play')

    expect(await screen.findByRole('alert')).toHaveTextContent('Vi kunne ikke finde dig i sessionen.')
    expect(loadDeviceToken()).toBeNull()
  })

  it('keeps trying to reach the realtime hub while the backend wakes up', async () => {
    saveDeviceToken('token-1')
    configureNextHub({ startFailures: 2 })
    mockFetch({ '/api/play/reconnect': () => json(stateFor(teams[0])) })
    renderAt('/play')

    expect(screen.getByRole('status')).toHaveTextContent('Forbinder...')
    expect(await screen.findByRole('heading', { name: 'Du er på Hold Rød' })).toBeInTheDocument()
  })

  it('ends when the hub reports the session closed', async () => {
    saveDeviceToken('token-1')
    configureNextHub({ subscribeResult: 'SESSION_CLOSED' })
    const fetchMock = mockFetch({ '/api/play/reconnect': () => json(stateFor(teams[0])) })
    renderAt('/play')

    expect(await screen.findByRole('alert')).toHaveTextContent('Sessionen er lukket.')
    expect(loadDeviceToken()).toBeNull()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('lets the student leave, e.g. on a shared tablet', async () => {
    saveDeviceToken('token-1')
    mockFetch({ '/api/play/reconnect': () => json(stateFor(teams[0])) })
    renderAt('/play')

    fireEvent.click(await screen.findByRole('button', { name: 'Forlad sessionen' }))

    expect(screen.getByRole('heading', { name: 'Deltag i et escape room' })).toBeInTheDocument()
    expect(loadDeviceToken()).toBeNull()
  })
})
