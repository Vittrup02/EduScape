import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveDeviceToken } from '../api/deviceStorage'
import type { AnswerResult, PlayState, TeamState } from '../api/play'
import { json, mockFetch, urlOf } from '../test/fetchMock'
import { latestHub } from '../test/playHubMock'
import { playState } from '../test/playRoom'
import { PlayPage } from './PlayPage'

vi.mock('../api/playHub', () => import('../test/playHubMock'))

const codeLock = 'Lås: Hvad giver regnestykket?'
const codeLockOpen = 'Åben lås: Hvad giver regnestykket?'
const textLock = 'Lås: Hvad er 10 / 3?'

function bodyOf(init: RequestInit | undefined): unknown {
  return JSON.parse(init?.body as string)
}

/** Serves `state` on reconnect and the given answers in order; records every call. */
function serve(state: () => PlayState, answers: AnswerResult[] = [], extra: Parameters<typeof mockFetch>[0] = {}) {
  return mockFetch({
    '/api/play/reconnect': () => json(state()),
    '/api/play/answer': () => json(answers.shift() ?? { outcome: 'Wrong', hint: null }),
    ...extra,
  })
}

function callsTo(fetchMock: ReturnType<typeof mockFetch>, path: string) {
  return fetchMock.mock.calls.filter(([url]) => new URL(urlOf(url)).pathname === path)
}

async function renderPlay(team: Partial<TeamState> = {}) {
  render(
    <MemoryRouter initialEntries={['/play']}>
      <Routes>
        <Route path="/play" element={<PlayPage retryDelayMs={0} />} />
      </Routes>
    </MemoryRouter>,
  )
  await screen.findByRole('heading', { name: 'Du er på Hold Rød' })
  return playState(team)
}

function answer(value: string) {
  fireEvent.change(screen.getByLabelText('Dit svar'), { target: { value } })
  fireEvent.click(screen.getByRole('button', { name: 'Prøv' }))
}

describe('playing a room', () => {
  beforeEach(() => {
    saveDeviceToken('token-1')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    saveDeviceToken(null)
  })

  it('shows the start scene with the objects that are available from the start', async () => {
    serve(() => playState())
    await renderPlay()

    const scene = screen.getByRole('region', { name: 'Klasselokalet' })
    expect(within(scene).getByRole('button', { name: codeLock })).toBeInTheDocument()
    expect(within(scene).getByRole('button', { name: 'Brev' })).toBeInTheDocument()
    expect(within(scene).queryByRole('button', { name: textLock })).not.toBeInTheDocument()
    expect(within(scene).queryByRole('button', { name: /Dør/ })).not.toBeInTheDocument()
    expect(latestHub().subscribedTokens).toEqual(['token-1'])
  })

  it('places objects by their position in percent', async () => {
    serve(() => playState())
    await renderPlay()

    const lock = screen.getByRole('button', { name: codeLock })
    expect(lock.style.left).toBe('40%')
    expect(lock.style.top).toBe('45%')
  })

  it('shows the letter text', async () => {
    serve(() => playState())
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: 'Brev' }))

    expect(screen.getByRole('dialog', { name: 'Brev' })).toHaveTextContent('Tallet står på tavlen: 3 · 4 + 5')
    fireEvent.click(screen.getByRole('button', { name: 'Luk' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('sends the answer to the server, says when it is wrong and opens the lock when it is right', async () => {
    const fetchMock = serve(() => playState(), [
      { outcome: 'Wrong', hint: null },
      { outcome: 'Correct', hint: null },
    ])
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: codeLock }))
    expect(screen.getByRole('dialog', { name: 'Lås' })).toHaveTextContent('Hvad giver regnestykket?')
    answer('18')

    expect(await screen.findByRole('alert')).toHaveTextContent('Forkert svar. Prøv igen.')
    expect(screen.getByLabelText('Dit svar')).toHaveValue('')
    answer(' 17 ')

    expect(await screen.findByRole('button', { name: codeLockOpen })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: textLock })).toBeInTheDocument()
    expect(callsTo(fetchMock, '/api/play/answer').map(([, init]) => bodyOf(init))).toEqual([
      { deviceToken: 'token-1', lockId: 'code-lock', answer: '18' },
      { deviceToken: 'token-1', lockId: 'code-lock', answer: ' 17 ' },
    ])
  })

  it('shows the hint once the team has earned it', async () => {
    serve(() => playState(), [{ outcome: 'Wrong', hint: 'Gang først.' }])
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: codeLock }))
    answer('1')

    expect(await screen.findByText('Gang først.')).toBeInTheDocument()
    expect(screen.getByText('Hint:')).toBeInTheDocument()
  })

  it('offers the options of a multiple-choice lock as buttons', async () => {
    const fetchMock = serve(
      () => playState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] }),
      [{ outcome: 'Correct', hint: null }],
    )
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))

    fireEvent.click(screen.getByRole('button', { name: 'Lås: Hvilket tal er et primtal?' }))
    const options = screen.getByRole('group', { name: 'Vælg et svar' })
    fireEvent.click(within(options).getByRole('button', { name: '17' }))

    expect(await screen.findByRole('button', { name: 'Åben lås: Hvilket tal er et primtal?' })).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, '/api/play/answer')[0][1])).toEqual({
      deviceToken: 'token-1',
      lockId: 'choice-lock',
      answer: '17',
    })
  })

  it("follows the team: a teammate's solved lock opens here too", async () => {
    serve(() => playState())
    await renderPlay()

    act(() => {
      latestHub().handlers.lockSolved('code-lock')
    })

    expect(screen.getByRole('button', { name: codeLockOpen })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: textLock })).toBeInTheDocument()
  })

  it("shows a hint a teammate earned while the lock's dialog is open", async () => {
    serve(() => playState())
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: codeLock }))

    act(() => {
      latestHub().handlers.hintRevealed('code-lock', 'Gang først.')
    })

    expect(screen.getByRole('dialog')).toHaveTextContent('Gang først.')
  })

  it('tells the student when a teammate solved the open lock first', async () => {
    serve(() => playState())
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: codeLock }))

    act(() => {
      latestHub().handlers.lockSolved('code-lock')
    })

    expect(screen.getByRole('dialog')).toHaveTextContent('Låsen er åbnet.')
    expect(screen.queryByLabelText('Dit svar')).not.toBeInTheDocument()
  })

  it('shows "connecting" while the connection is lost and fetches the whole state again afterwards', async () => {
    let state = playState()
    const fetchMock = serve(() => state)
    await renderPlay()

    act(() => {
      latestHub().drop()
    })
    expect(screen.getByRole('status')).toHaveTextContent('Forbindelsen blev afbrudt. Forbinder igen...')
    expect(screen.getByRole('button', { name: codeLock })).toBeDisabled()

    // Meanwhile a teammate solved the lock; the event was missed while offline.
    state = playState({ solvedLockIds: ['code-lock'] })
    act(() => {
      latestHub().restore()
    })

    expect(await screen.findByRole('button', { name: codeLockOpen })).toBeEnabled()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(latestHub().subscribedTokens).toEqual(['token-1', 'token-1'])
    expect(callsTo(fetchMock, '/api/play/reconnect')).toHaveLength(2)
  })

  it('opens a door for the team, moves into the next scene and lets the student go back', async () => {
    const fetchMock = serve(() => playState({ solvedLockIds: ['code-lock', 'text-lock'] }), [], {
      '/api/play/doors/open': () => new Response(null, { status: 204 }),
    })
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: 'Dør til Biblioteket' }))

    expect(await screen.findByRole('region', { name: 'Biblioteket' })).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, '/api/play/doors/open')[0][1])).toEqual({ deviceToken: 'token-1', doorId: 'door' })

    const scenes = screen.getByRole('navigation', { name: 'Scener' })
    fireEvent.click(within(scenes).getByRole('button', { name: 'Klasselokalet' }))
    expect(screen.getByRole('button', { name: 'Åben dør til Biblioteket' })).toBeInTheDocument()
  })

  it('keeps the exit door shut until every lock is solved', async () => {
    serve(() => playState({ solvedLockIds: ['code-lock', 'choice-lock'], openedDoorIds: ['door'] }), [], {
      '/api/play/exit': () => json({ code: 'EXIT_DOOR_LOCKED' }, 400),
    })
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))

    fireEvent.click(screen.getByRole('button', { name: 'Slutdør' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Slutdøren er stadig låst.')
  })

  it('lets the team break out through the exit door', async () => {
    serve(() => playState({ solvedLockIds: ['code-lock', 'text-lock', 'choice-lock'], openedDoorIds: ['door'] }), [], {
      '/api/play/exit': () => new Response(null, { status: 204 }),
    })
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))

    fireEvent.click(screen.getByRole('button', { name: 'Slutdør' }))

    expect(await screen.findByRole('heading', { name: 'I er brudt ud!' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Biblioteket' })).not.toBeInTheDocument()
  })

  it('shows the escape to teammates as well', async () => {
    serve(() => playState())
    await renderPlay()

    act(() => {
      latestHub().handlers.teamEscaped()
    })

    expect(screen.getByRole('heading', { name: 'I er brudt ud!' })).toBeInTheDocument()
  })
})
