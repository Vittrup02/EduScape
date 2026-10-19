import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { apiBaseUrl } from './client'

/** Events the server pushes to every device on the team. Same names as IPlayClient in the backend. */
export interface PlayHubEvents {
  lockSolved(lockId: string): void
  hintRevealed(lockId: string, hint: string): void
  doorOpened(doorId: string): void
  teamEscaped(): void
}

export interface PlayHubHandlers extends PlayHubEvents {
  /** The connection was lost; it is retried automatically. */
  reconnecting(): void
  /** Back online on a new connection, which must subscribe again and fetch the team's state. */
  reconnected(): void
  /** The connection stopped for good (e.g. the server closed it) and has to be started again. */
  closed(): void
}

export interface PlayHub {
  start(): Promise<void>
  stop(): Promise<void>
  isConnected(): boolean
  /** Joins the team's group. Resolves to null, or an error code such as SESSION_CLOSED or DEVICE_NOT_RECOGNIZED. */
  subscribe(deviceToken: string): Promise<string | null>
}

export const playHubPath = '/hubs/play'

/** Waits 0, 1, 2, 4 and 8 seconds, then every 10 seconds, without ever giving up: the backend may be asleep. */
export function reconnectDelayMs(previousRetryCount: number): number {
  return previousRetryCount === 0 ? 0 : Math.min(1000 * 2 ** (previousRetryCount - 1), 10_000)
}

/** The SignalR connection a student's device listens on. Actions go through the play endpoints, not this hub. */
export function createPlayHub(handlers: PlayHubHandlers): PlayHub {
  const connection = new HubConnectionBuilder()
    // No cookies are involved, so the default CORS policy (without credentials) is enough.
    .withUrl(`${apiBaseUrl}${playHubPath}`, { withCredentials: false })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => reconnectDelayMs(context.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('LockSolved', (lockId: string) => {
    handlers.lockSolved(lockId)
  })
  connection.on('HintRevealed', (lockId: string, hint: string) => {
    handlers.hintRevealed(lockId, hint)
  })
  connection.on('DoorOpened', (doorId: string) => {
    handlers.doorOpened(doorId)
  })
  connection.on('TeamEscaped', () => {
    handlers.teamEscaped()
  })
  connection.onreconnecting(() => {
    handlers.reconnecting()
  })
  connection.onreconnected(() => {
    handlers.reconnected()
  })
  connection.onclose(() => {
    handlers.closed()
  })

  return {
    start: () => connection.start(),
    stop: () => connection.stop(),
    isConnected: () => connection.state === HubConnectionState.Connected,
    subscribe: (deviceToken) => connection.invoke<string | null>('Subscribe', deviceToken),
  }
}
