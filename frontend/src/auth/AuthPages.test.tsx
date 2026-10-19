import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveAuth } from '../api/tokenStorage'
import { AppRoutes } from '../App'
import { json, mockFetch, tokens } from '../test/fetchMock'

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
    </MemoryRouter>,
  )
}

function fillIn(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } })
}

describe('teacher login and protected routes', () => {
  beforeEach(() => {
    mockFetch({ '/health': () => new Response(null, { status: 200 }) })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('sends a signed-out visitor from a teacher page to the login page', () => {
    renderAt('/teacher')

    expect(screen.getByRole('heading', { name: 'Log ind' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Mine rum' })).not.toBeInTheDocument()
  })

  it('shows a translated error for wrong credentials', async () => {
    mockFetch({
      '/health': () => new Response(null, { status: 200 }),
      '/api/auth/login': () => json({ code: 'INVALID_CREDENTIALS' }, 401),
    })
    renderAt('/login')

    fillIn('E-mail', 'ida@skole.dk')
    fillIn('Adgangskode', 'forkert kode')
    fireEvent.click(screen.getByRole('button', { name: 'Log ind' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Forkert e-mail eller adgangskode.')
  })

  it('returns to the teacher page after logging in', async () => {
    mockFetch({
      '/health': () => new Response(null, { status: 200 }),
      '/api/auth/login': () => tokens('access-1'),
      '/api/teacher/rooms': () => json([]),
    })
    renderAt('/teacher')

    fillIn('E-mail', 'ida@skole.dk')
    fillIn('Adgangskode', 'en lang adgangskode')
    fireEvent.click(screen.getByRole('button', { name: 'Log ind' }))

    expect(await screen.findByRole('heading', { name: 'Mine rum' })).toBeInTheDocument()
    expect(screen.getByText('Logget ind som ida@skole.dk')).toBeInTheDocument()
  })

  it('shows the server password rule when registering', async () => {
    mockFetch({
      '/health': () => new Response(null, { status: 200 }),
      '/api/auth/register': () => json({ code: 'PASSWORD_TOO_SHORT' }, 400),
    })
    renderAt('/register')

    fillIn('E-mail', 'ny@skole.dk')
    fillIn('Adgangskode', 'kort')
    fireEvent.click(screen.getByRole('button', { name: 'Opret konto' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Adgangskoden skal være på mindst 10 tegn.')
  })

  it('logs out from the header and leaves the teacher page', async () => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
    mockFetch({
      '/api/teacher/rooms': () => json([]),
      '/api/auth/logout': () => new Response(null, { status: 204 }),
      '/health': () => new Response(null, { status: 200 }),
    })
    renderAt('/teacher')
    expect(await screen.findByText('Du har ingen rum endnu.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Log ud' }))

    expect(await screen.findByRole('heading', { name: 'Log ind' })).toBeInTheDocument()
  })
})
