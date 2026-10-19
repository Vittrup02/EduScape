import { useEffect, useState } from 'react'
import { checkHealth } from './api/client'

type BackendStatus = 'checking' | 'online' | 'offline'

const statusText: Record<BackendStatus, string> = {
  checking: 'Forbinder til serveren...',
  online: 'Serveren svarer',
  offline: 'Serveren svarer ikke lige nu',
}

function App() {
  const [status, setStatus] = useState<BackendStatus>('checking')

  useEffect(() => {
    const controller = new AbortController()
    void checkHealth(controller.signal).then((ok) => {
      if (!controller.signal.aborted) {
        setStatus(ok ? 'online' : 'offline')
      }
    })
    return () => {
      controller.abort()
    }
  }, [])

  return (
    <main>
      <h1>eduscape</h1>
      <p className="status" data-state={status} role="status">
        {statusText[status]}
      </p>
    </main>
  )
}

export default App
