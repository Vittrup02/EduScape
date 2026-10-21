import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveDeviceToken } from '../api/deviceStorage'
import type { AnswerResult, PlayLock, PlayRoom, PlayState, TeamState } from '../api/play'
import { json, mockFetch, urlOf } from '../test/fetchMock'
import { latestHub } from '../test/playHubMock'
import { playRoom, playState, step } from '../test/playRoom'
import { PlayPage } from './PlayPage'

vi.mock('../api/playHub', () => import('../test/playHubMock'))

const codeLock = 'Lås: Hvad giver regnestykket?'
const codeLockOpen = 'Åben lås: Hvad giver regnestykket?'
const textLock = 'Lås: Hvad er 10 / 3?'

function bodyOf(init: RequestInit | undefined): unknown {
  return JSON.parse(init?.body as string)
}

/** An answer as the server returns it; a correct one solves a one-step lock. */
function result(outcome: AnswerResult['outcome'], changes: Partial<AnswerResult> = {}): AnswerResult {
  const correct = outcome === 'Correct'
  return {
    outcome,
    solvedSteps: correct ? 1 : 0,
    lockSolved: correct,
    hint: null,
    cooldownSeconds: 0,
    openedDoorIds: [],
    ...changes,
  }
}

/** The state as the server sends it: the team's waits are in seconds from now. */
function stateJson(state: PlayState) {
  return {
    ...state,
    team: {
      ...state.team,
      locks: state.team.locks.map(({ cooldownUntil, ...lock }) => ({
        ...lock,
        cooldownSeconds: cooldownUntil === null ? 0 : Math.ceil((cooldownUntil - Date.now()) / 1000),
      })),
    },
  }
}

/** Serves `state` on reconnect and the given answers in order; records every call. */
function serve(state: () => PlayState, answers: AnswerResult[] = [], extra: Parameters<typeof mockFetch>[0] = {}) {
  return mockFetch({
    '/api/play/reconnect': () => json(stateJson(state())),
    '/api/play/answer': () => json(answers.shift() ?? result('Wrong')),
    ...extra,
  })
}

/** The demo room with the code lock replaced, e.g. by one with several steps. */
function roomWithCodeLock(changes: Partial<PlayLock>): PlayRoom {
  return {
    ...playRoom,
    scenes: playRoom.scenes.map((scene) => ({
      ...scene,
      objects: scene.objects.map((object) =>
        object.id === 'code-lock' && object.type === 'Lock' ? { ...object, ...changes } : object,
      ),
    })),
  }
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
    // Doors are always there; this one is still shut.
    expect(within(scene).getByRole('button', { name: 'Dør til Biblioteket' })).toBeInTheDocument()
    expect(latestHub().subscribedTokens).toEqual(['token-1'])
  })

  it('starts where the student was before a reload, or where a preview starts', async () => {
    serve(() => playState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] }, { currentSceneId: 'library' }))
    await renderPlay()

    expect(screen.getByRole('region', { name: 'Biblioteket' })).toBeInTheDocument()
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
    const fetchMock = serve(() => playState(), [result('Wrong'), result('Correct')])
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
      { deviceToken: 'token-1', lockId: 'code-lock', stepIndex: 0, answer: '18', order: null },
      { deviceToken: 'token-1', lockId: 'code-lock', stepIndex: 0, answer: ' 17 ', order: null },
    ])
  })

  it('shows the next step in the same pop-up, and opens the lock only after the last one', async () => {
    const steps = [step('Hvad er 3 · 4?', 'Code'), step('Hvad er 12 + 5?', 'Code')]
    const room = roomWithCodeLock({ steps })
    const fetchMock = serve(() => playState({}, { room }), [
      result('Correct', { solvedSteps: 1, lockSolved: false }),
      result('Correct', { solvedSteps: 2, lockSolved: true }),
    ])
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: 'Lås: Hvad er 3 · 4?' }))
    const dialog = screen.getByRole('dialog', { name: 'Lås' })
    expect(dialog).toHaveTextContent('Trin 1 af 2')
    expect(dialog).not.toHaveTextContent('Hvad er 12 + 5?')
    answer('12')

    expect(await within(dialog).findByText('Hvad er 12 + 5?')).toBeInTheDocument()
    expect(dialog).toHaveTextContent('Trin 2 af 2')
    expect(dialog).toHaveTextContent('Trinnet er løst. Her er det næste.')
    expect(screen.getByRole('button', { name: 'Lås: Hvad er 12 + 5?' })).toBeInTheDocument()
    answer('17')

    expect(await screen.findByRole('button', { name: 'Åben lås: Hvad er 12 + 5?' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(callsTo(fetchMock, '/api/play/answer').map(([, init]) => (bodyOf(init) as { stepIndex: number }).stepIndex)).toEqual([
      0, 1,
    ])
  })

  it("moves on to the next step when a teammate answers it, and keeps the team's step after a reload", async () => {
    const steps = [step('Hvad er 3 · 4?', 'Code'), step('Hvad er 12 + 5?', 'Code')]
    const room = roomWithCodeLock({ steps })
    let state = playState({}, { room })
    serve(() => state)
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Lås: Hvad er 3 · 4?' }))

    act(() => {
      latestHub().handlers.stepSolved('code-lock', 1)
    })
    expect(screen.getByRole('dialog')).toHaveTextContent('Hvad er 12 + 5?')

    // After a lost connection the server's state has the team on step 2.
    state = playState({ locks: [{ lockId: 'code-lock', solvedSteps: 1, hint: null, cooldownUntil: null }] }, { room })
    act(() => {
      latestHub().drop()
      latestHub().restore()
    })
    await screen.findByRole('dialog')
    expect(screen.getByRole('dialog')).toHaveTextContent('Trin 2 af 2')
  })

  it('locks the lock for the whole team after a wrong answer, with a countdown', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
    try {
      serve(() => playState(), [result('Wrong', { cooldownSeconds: 15 })])
      await renderPlay()
      fireEvent.click(screen.getByRole('button', { name: codeLock }))
      answer('18')

      expect(await screen.findByRole('timer')).toHaveTextContent('Låsen er låst i 15 sekunder efter det forkerte svar.')
      expect(screen.getByRole('button', { name: 'Prøv' })).toBeDisabled()

      act(() => {
        vi.advanceTimersByTime(14_000)
      })
      expect(screen.getByRole('timer')).toHaveTextContent('Låsen er låst i 1 sekund efter det forkerte svar.')

      act(() => {
        vi.advanceTimersByTime(1_000)
      })
      expect(screen.queryByRole('timer')).not.toBeInTheDocument()
      fireEvent.change(screen.getByLabelText('Dit svar'), { target: { value: '17' } })
      expect(screen.getByRole('button', { name: 'Prøv' })).toBeEnabled()
    } finally {
      vi.useRealTimers()
    }
  })

  it("shows a teammate's wait too", async () => {
    serve(() => playState())
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: codeLock }))

    act(() => {
      latestHub().handlers.lockCooldown('code-lock', 10)
    })

    // The countdown's clock catches up at once.
    await waitFor(() => {
      expect(screen.getByRole('timer')).toHaveTextContent('10 sekunder')
    })
    expect(screen.getByRole('button', { name: 'Prøv' })).toBeDisabled()
  })

  it('lets the student drag the items into order and sends only their ids', async () => {
    const orderItems = [
      { id: 'c', text: 'Produkt dannes', imageUrl: null },
      { id: 'a', text: 'Stofferne blandes', imageUrl: 'https://images.example/a.webp' },
      { id: 'b', text: 'Opvarmning', imageUrl: null },
    ]
    const room = roomWithCodeLock({ steps: [{ ...step('Sæt trinnene i rækkefølge', 'Order'), orderItems }] })
    const fetchMock = serve(() => playState({}, { room }), [result('Correct')])
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: 'Lås: Sæt trinnene i rækkefølge' }))
    const list = screen.getByRole('list', { name: 'Elementerne i jeres rækkefølge' })
    // The arrow keys do what dragging does: "Stofferne blandes" first, then "Opvarmning" second.
    fireEvent.keyDown(within(list).getByLabelText('2. Stofferne blandes'), { key: 'ArrowUp' })
    fireEvent.keyDown(within(list).getByLabelText('3. Opvarmning'), { key: 'ArrowUp' })
    expect(within(list).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      '1Stofferne blandes',
      '2Opvarmning',
      '3Produkt dannes',
    ])
    fireEvent.click(screen.getByRole('button', { name: 'Prøv' }))

    expect(await screen.findByRole('button', { name: 'Åben lås: Sæt trinnene i rækkefølge' })).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, '/api/play/answer')[0][1])).toEqual({
      deviceToken: 'token-1',
      lockId: 'code-lock',
      stepIndex: 0,
      answer: null,
      order: ['a', 'b', 'c'],
    })
  })

  it("keeps the student's order while the lock waits after a wrong answer on the second step", async () => {
    const orderItems = [
      { id: 'c', text: 'Produkt dannes', imageUrl: null },
      { id: 'a', text: 'Stofferne blandes', imageUrl: null },
    ]
    const steps = [step('Hvad er 3 · 4?', 'Code'), { ...step('Sæt i rækkefølge', 'Order'), orderItems }]
    const room = roomWithCodeLock({ steps })
    const team = { locks: [{ lockId: 'code-lock', solvedSteps: 1, hint: null, cooldownUntil: null }] }
    serve(() => playState(team, { room }), [result('Wrong', { cooldownSeconds: 2 })])
    const consoleError = vi.spyOn(console, 'error')
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Lås: Sæt i rækkefølge' }))
    fireEvent.click(screen.getByRole('button', { name: 'Prøv' }))
    await screen.findByRole('timer')

    const list = screen.getByRole('list', { name: 'Elementerne i jeres rækkefølge' })
    fireEvent.keyDown(within(list).getByLabelText('2. Stofferne blandes'), { key: 'ArrowUp' })
    // The countdown renders the pop-up again and again; the moved card must stay where the student put it.
    await waitFor(
      () => {
        expect(screen.queryByRole('timer')).not.toBeInTheDocument()
      },
      { timeout: 3000 },
    )
    expect(within(list).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      '1Stofferne blandes',
      '2Produkt dannes',
    ])
    // The wrong-answer message and the second step's answer are siblings; they must never share a key.
    expect(consoleError.mock.calls.flat().join(' ')).not.toMatch(/same key/)
    consoleError.mockRestore()
  })

  it('shows the hint once the team has earned it', async () => {
    serve(() => playState(), [result('Wrong', { hint: 'Gang først.' })])
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: codeLock }))
    answer('1')

    expect(await screen.findByText('Gang først.')).toBeInTheDocument()
    expect(screen.getByText('Hint:')).toBeInTheDocument()
  })

  it('offers the options of a multiple-choice lock as buttons', async () => {
    const fetchMock = serve(
      () => playState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] }),
      [result('Correct')],
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
      stepIndex: 0,
      answer: '17',
      order: null,
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

  it('says a shut door needs the puzzles solved, and never moves the student by itself', async () => {
    serve(() => playState({ solvedLockIds: ['code-lock'] }))
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: 'Dør til Biblioteket' }))

    expect(screen.getByRole('status')).toHaveTextContent('Døren er lukket. Løs gåderne for at åbne den.')
    expect(screen.getByRole('region', { name: 'Klasselokalet' })).toBeInTheDocument()
  })

  it('opens a door for every device on the team, and lets the student walk through and back', async () => {
    serve(() => playState({ solvedLockIds: ['code-lock', 'text-lock'] }))
    await renderPlay()
    const door = screen.getByRole('button', { name: 'Dør til Biblioteket' })

    act(() => {
      latestHub().handlers.doorOpened('door')
    })

    // It opens with a short animation where the student is; the scene stays until the student walks through.
    expect(screen.getByRole('button', { name: 'Åben dør til Biblioteket' })).toBe(door)
    expect(door.querySelector('[data-opening]')).not.toBeNull()
    expect(screen.getByRole('region', { name: 'Klasselokalet' })).toBeInTheDocument()

    fireEvent.click(door)
    expect(screen.getByRole('region', { name: 'Biblioteket' })).toBeInTheDocument()
    const scenes = screen.getByRole('navigation', { name: 'Scener' })
    fireEvent.click(within(scenes).getByRole('button', { name: 'Klasselokalet' }))
    expect(screen.getByRole('button', { name: 'Åben dør til Biblioteket' })).toBeInTheDocument()
  })

  it('opens the doors an answer opened at once, without waiting for the push', async () => {
    serve(() => playState({ solvedLockIds: ['code-lock'] }), [result('Correct', { openedDoorIds: ['door'] })])
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: textLock }))
    answer('3,33')

    expect(await screen.findByRole('button', { name: 'Åben dør til Biblioteket' })).toBeInTheDocument()
  })

  it('keeps the exit door shut until it opens', async () => {
    const fetchMock = serve(() => playState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] }))
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))

    fireEvent.click(screen.getByRole('button', { name: 'Slutdør' }))

    expect(screen.getByRole('status')).toHaveTextContent('Døren er lukket. Løs gåderne for at åbne den.')
    expect(callsTo(fetchMock, '/api/play/exit')).toHaveLength(0)
  })

  it('lets the team break out through the open exit door', async () => {
    serve(
      () => playState({ solvedLockIds: ['code-lock', 'text-lock', 'choice-lock'], openedDoorIds: ['door', 'exit'] }),
      [],
      { '/api/play/exit': () => new Response(null, { status: 204 }) },
    )
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))

    fireEvent.click(screen.getByRole('button', { name: 'Åben slutdør' }))

    expect(await screen.findByRole('heading', { name: 'I slap ud!' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Biblioteket' })).not.toBeInTheDocument()
  })

  it('shows the escape to teammates as well', async () => {
    serve(() => playState())
    await renderPlay()

    act(() => {
      latestHub().handlers.teamEscaped()
    })

    expect(screen.getByRole('heading', { name: 'I slap ud!' })).toBeInTheDocument()
  })
})

describe("telling the teacher's dashboard where the student is", () => {
  beforeEach(() => {
    saveDeviceToken('token-1')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    saveDeviceToken(null)
  })

  const reportScene = () => new Response(null, { status: 204 })

  it('reports the start scene and every scene the student moves to', async () => {
    const fetchMock = serve(() => playState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] }), [], {
      '/api/play/scene': reportScene,
    })
    await renderPlay()

    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))
    await screen.findByRole('region', { name: 'Biblioteket' })

    const reported = () => callsTo(fetchMock, '/api/play/scene').map(([, init]) => bodyOf(init))
    await vi.waitFor(() => {
      expect(reported()).toEqual([
        { deviceToken: 'token-1', sceneId: 'classroom' },
        { deviceToken: 'token-1', sceneId: 'library' },
      ])
    })
  })

  it('switches to the new team when the teacher moves the student', async () => {
    let state = playState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] })
    const fetchMock = serve(() => state, [], { '/api/play/scene': reportScene })
    await renderPlay()
    fireEvent.click(screen.getByRole('button', { name: 'Biblioteket' }))
    await screen.findByRole('region', { name: 'Biblioteket' })
    const firstHub = latestHub()

    // The new team has not opened the door to the library yet.
    state = playState({ id: 'blue', color: 'Blue' })
    act(() => {
      firstHub.handlers.teamChanged()
    })

    expect(await screen.findByRole('heading', { name: 'Du er på Hold Blå' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Klasselokalet' })).toBeInTheDocument()
    // A fresh connection, so the old team's events no longer arrive.
    expect(firstHub.subscribedTokens).toEqual(['token-1', 'token-1'])
    expect(callsTo(fetchMock, '/api/play/reconnect')).toHaveLength(2)
    await vi.waitFor(() => {
      expect(bodyOf(callsTo(fetchMock, '/api/play/scene').at(-1)?.[1])).toEqual({
        deviceToken: 'token-1',
        sceneId: 'classroom',
      })
    })
  })
})
