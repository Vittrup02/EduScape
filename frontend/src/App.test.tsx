import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { backendTiming } from './api/client'
import App from './App'

describe('App', () => {
  beforeEach(() => {
    window.history.pushState({}, '', '/')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows "getting ready" while the sleeping backend wakes up, and hides it when it answers', async () => {
    Object.assign(backendTiming, { slowAfterMs: 10, idleAfterMs: -1 })
    let answer: (response: Response) => void = () => undefined
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((resolve) => (answer = resolve))))

    render(<App />)

    expect(await screen.findByRole('status')).toHaveTextContent('Gør klar … det tager op til et minut første gang.')
    // The page itself works meanwhile; nothing is shown as an error.
    expect(screen.getByRole('link', { name: 'Deltag med PIN-kode' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()

    act(() => {
      answer(new Response(null, { status: 200 }))
    })
    await vi.waitFor(() => {
      expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })
  })

  it('shows "getting ready", not an error, while the backend cannot be reached', async () => {
    Object.assign(backendTiming, { retryDelayMs: 1000, giveUpAfterMs: 60_000 })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    render(<App />)

    expect(await screen.findByRole('status')).toHaveTextContent('Gør klar')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows nothing about the backend when it answers right away', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    // The wake-up call goes out as soon as the app opens.
    await vi.waitFor(() => {
      expect(fetchMock).toHaveBeenCalled()
    })
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('offers new teachers an account and links to "About data" from every page', () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 200 })))
    render(<App />)

    expect(screen.getByRole('link', { name: 'Opret en lærerkonto' })).toHaveAttribute('href', '/register')

    fireEvent.click(screen.getByRole('link', { name: 'Om data' }))

    expect(screen.getByRole('heading', { level: 1, name: 'Om data' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Hvornår slettes elevernes data?' })).toBeInTheDocument()
    expect(screen.getByText(/Det, eleverne skriver som svar, gemmes ikke/)).toBeInTheDocument()
  })

  it('switches to English with the language switcher', () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 200 })))
    render(<App />)
    expect(screen.getByRole('button', { name: 'DA' })).toHaveAttribute('aria-pressed', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'EN' }))

    expect(screen.getByRole('link', { name: 'Join with a PIN' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'About data' })).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Language' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'EN' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'DA' })).toHaveAttribute('aria-pressed', 'false')
  })
})
