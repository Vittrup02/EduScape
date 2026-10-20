import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { accessToken } from './auth'
import { apiBaseUrl } from './client'
import { reconnectDelayMs } from './playHub'

export interface TeacherHubHandlers {
  /** Something in the watched session changed; the dashboard fetches its whole state again. */
  dashboardChanged(): void
  /** The connection was lost; it is retried automatically. */
  reconnecting(): void
  /** Back online on a new connection, which must watch the session again and fetch its state. */
  reconnected(): void
  /** The connection stopped for good and has to be started again. */
  closed(): void
}

export interface TeacherHub {
  start(): Promise<void>
  stop(): Promise<void>
  isConnected(): boolean
  /** Joins the session's dashboard group. Resolves to null, or an error code such as NOT_FOUND. */
  watch(sessionId: string): Promise<string | null>
}

export const teacherHubPath = '/hubs/teacher'

/** The SignalR connection the teacher's live dashboard listens on. */
export function createTeacherHub(handlers: TeacherHubHandlers): TeacherHub {
  let tokenRequests = 0
  const connection = new HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}${teacherHubPath}`, {
      withCredentials: false,
      // The first connection uses the stored token. Any later request for a token is a reconnect or a retry after a
      // 401, when the 30-minute access token may have expired, so the tokens are refreshed first.
      accessTokenFactory: async () => (await accessToken(tokenRequests++ > 0)) ?? '',
    })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => reconnectDelayMs(context.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('DashboardChanged', () => {
    handlers.dashboardChanged()
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
    watch: (sessionId) => connection.invoke<string | null>('Watch', sessionId),
  }
}
