import type { TeacherHub, TeacherHubHandlers } from '../api/teacherHub'

/**
 * Stand-in for the teacher's SignalR connection in tests. Use it with
 * `vi.mock('../api/teacherHub', () => import('../test/teacherHubMock'))`; `latestTeacherHub()` then plays the
 * server's part.
 */
export class FakeTeacherHub implements TeacherHub {
  connected = false
  /** How many calls to start() fail, as while the backend is asleep. */
  startFailures = 0
  /** What Watch answers: null, or an error code. */
  watchResult: string | null = null
  readonly watchedSessions: string[] = []
  readonly handlers: TeacherHubHandlers

  constructor(handlers: TeacherHubHandlers) {
    this.handlers = handlers
  }

  start(): Promise<void> {
    if (this.startFailures > 0) {
      this.startFailures--
      return Promise.reject(new Error('Failed to start the connection'))
    }
    this.connected = true
    return Promise.resolve()
  }

  /** Like SignalR, stopping a started connection reports it closed. */
  stop(): Promise<void> {
    if (this.connected) {
      this.connected = false
      this.handlers.closed()
    }
    return Promise.resolve()
  }

  isConnected(): boolean {
    return this.connected
  }

  watch(sessionId: string): Promise<string | null> {
    this.watchedSessions.push(sessionId)
    return Promise.resolve(this.watchResult)
  }

  /** The connection drops; SignalR retries on its own. */
  drop() {
    this.connected = false
    this.handlers.reconnecting()
  }

  /** SignalR got a new connection. */
  restore() {
    this.connected = true
    this.handlers.reconnected()
  }
}

const hubs: FakeTeacherHub[] = []

let nextHubOptions: Partial<Pick<FakeTeacherHub, 'startFailures' | 'watchResult'>> = {}

/** Options for the next hub the app creates, e.g. `{ startFailures: 2 }`. */
export function configureNextTeacherHub(options: typeof nextHubOptions) {
  nextHubOptions = options
}

export function createTeacherHub(handlers: TeacherHubHandlers): TeacherHub {
  const hub = Object.assign(new FakeTeacherHub(handlers), nextHubOptions)
  nextHubOptions = {}
  hubs.push(hub)
  return hub
}

export function latestTeacherHub(): FakeTeacherHub {
  const hub = hubs.at(-1)
  if (hub === undefined) {
    throw new Error('The app has not created a teacher hub.')
  }
  return hub
}
