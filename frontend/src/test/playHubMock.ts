import type { PlayHub, PlayHubHandlers } from '../api/playHub'

/**
 * Stand-in for the SignalR connection in tests. Use it with
 * `vi.mock('../api/playHub', () => import('../test/playHubMock'))`; `latestHub()` then plays the server's part.
 */
export class FakePlayHub implements PlayHub {
  connected = false
  /** How many calls to start() fail, as while the backend is asleep. */
  startFailures = 0
  /** What Subscribe answers: null, or an error code. */
  subscribeResult: string | null = null
  readonly subscribedTokens: string[] = []
  readonly handlers: PlayHubHandlers

  constructor(handlers: PlayHubHandlers) {
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

  subscribe(deviceToken: string): Promise<string | null> {
    this.subscribedTokens.push(deviceToken)
    return Promise.resolve(this.subscribeResult)
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

const hubs: FakePlayHub[] = []

/** Options for the next hub the app creates, e.g. `{ startFailures: 2 }`. */
let nextHubOptions: Partial<Pick<FakePlayHub, 'startFailures' | 'subscribeResult'>> = {}

export function configureNextHub(options: typeof nextHubOptions) {
  nextHubOptions = options
}

export function createPlayHub(handlers: PlayHubHandlers): PlayHub {
  const hub = Object.assign(new FakePlayHub(handlers), nextHubOptions)
  nextHubOptions = {}
  hubs.push(hub)
  return hub
}

export function latestHub(): FakePlayHub {
  const hub = hubs.at(-1)
  if (hub === undefined) {
    throw new Error('The app has not created a play hub.')
  }
  return hub
}
