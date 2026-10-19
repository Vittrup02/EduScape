import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveAuth } from '../api/tokenStorage'
import i18n from '../i18n/i18n'
import { AppRoutes } from '../App'
import { json, mockFetch, urlOf } from '../test/fetchMock'
import { TeacherHomePage } from './TeacherHomePage'

const rooms = [
  { id: '1', title: 'Brøkernes hemmelighed', subject: 'Mathematics', gradeLevel: 'Grade7' },
  { id: '2', title: 'Flugten fra Christiansborg', subject: 'SocialStudies', gradeLevel: 'Stx2' },
]
const templates = [{ id: 't1', title: 'Demo: Flugten fra matematiklokalet', subject: 'Mathematics', gradeLevel: 'Grade7' }]
const sessions = [
  {
    id: 's1',
    pin: '042042',
    mode: 'Team',
    status: 'Open',
    roomTitle: 'Brøkernes hemmelighed',
    className: '9.A',
    startedAt: '2026-10-19T08:00:00Z',
    expiresAt: '2026-10-19T20:00:00Z',
  },
]

function mockHome(overrides: Record<string, () => Response> = {}) {
  return mockFetch({
    '/api/teacher/rooms': () => json(rooms),
    '/api/teacher/templates': () => json(templates),
    '/api/teacher/sessions': () => json([]),
    ...overrides,
  })
}

function renderPage() {
  render(
    <MemoryRouter>
      <TeacherHomePage />
    </MemoryRouter>,
  )
}

describe('TeacherHomePage', () => {
  beforeEach(() => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it("lists the teacher's rooms with translated subject and grade level", async () => {
    mockHome()

    renderPage()

    const list = await screen.findByRole('list', { name: 'Mine rum' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(within(items[0]).getByText('Brøkernes hemmelighed')).toBeInTheDocument()
    expect(within(items[0]).getByText('Matematik · 7. klasse')).toBeInTheDocument()
    expect(within(items[1]).getByText('Samfundsfag · 2.g')).toBeInTheDocument()
  })

  it('translates subject and grade level into the chosen language', async () => {
    await i18n.changeLanguage('en')
    mockHome()

    renderPage()

    expect(await screen.findAllByText('Mathematics · Grade 7')).toHaveLength(2)
  })

  it('links every room and template to its start page', async () => {
    mockHome()

    renderPage()

    const room = await screen.findByRole('link', { name: 'Start session: Brøkernes hemmelighed' })
    expect(room).toHaveAttribute('href', '/teacher/rooms/1/start')
    const templateList = screen.getByRole('list', { name: 'Skabeloner' })
    expect(within(templateList).getByRole('link', { name: /Start session/ })).toHaveAttribute(
      'href',
      '/teacher/templates/t1/start',
    )
  })

  it('offers a new room and editing the own rooms', async () => {
    mockHome()

    renderPage()

    expect(await screen.findByRole('link', { name: 'Redigér: Brøkernes hemmelighed' })).toHaveAttribute(
      'href',
      '/teacher/rooms/1/edit',
    )
    expect(screen.getByRole('link', { name: 'Nyt rum' })).toHaveAttribute('href', '/teacher/rooms/new')
    // Templates are not edited themselves; they are copied.
    const templateList = screen.getByRole('list', { name: 'Skabeloner' })
    expect(within(templateList).queryByRole('link', { name: /Redigér/ })).not.toBeInTheDocument()
  })

  it('copies a template and opens the copy in the editor', async () => {
    const fetchMock = mockHome({
      '/api/teacher/templates/t1/copy': () => json({ ...templates[0], id: 'copy-1' }, 201),
      '/api/teacher/rooms/copy-1/editor': () => json({ code: 'NOT_FOUND' }, 404),
    })
    render(
      <MemoryRouter initialEntries={['/teacher']}>
        <AppRoutes />
      </MemoryRouter>,
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Brug skabelonen: Demo: Flugten fra matematiklokalet' }))

    // The editor page asks for the copy, not the template.
    await waitFor(() => {
      expect(fetchMock.mock.calls.map(([input]) => new URL(urlOf(input)).pathname)).toContain(
        '/api/teacher/rooms/copy-1/editor',
      )
    })
    const copyCall = fetchMock.mock.calls.find(([input]) => urlOf(input).endsWith('/copy'))
    expect(copyCall?.[1]?.method).toBe('POST')
  })

  it('lists running sessions with class and PIN', async () => {
    mockHome({ '/api/teacher/sessions': () => json(sessions) })

    renderPage()

    const section = await screen.findByRole('region', { name: 'Igangværende sessioner' })
    expect(within(section).getByRole('link', { name: 'Brøkernes hemmelighed' })).toHaveAttribute(
      'href',
      '/teacher/sessions/s1',
    )
    expect(within(section).getByText('9.A · PIN 042042')).toBeInTheDocument()
  })

  it('leaves out the sessions section when nothing is running', async () => {
    mockHome()

    renderPage()

    await screen.findByRole('list', { name: 'Mine rum' })
    expect(screen.queryByRole('region', { name: 'Igangværende sessioner' })).not.toBeInTheDocument()
  })

  it('shows a friendly loading message while the server wakes up', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => undefined)))

    renderPage()

    expect(screen.getByRole('status')).toHaveTextContent('Henter dine rum...')
  })

  it('says so when the teacher has no rooms yet', async () => {
    mockHome({ '/api/teacher/rooms': () => json([]) })

    renderPage()

    expect(await screen.findByText('Du har ingen rum endnu.')).toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Skabeloner' })).toBeInTheDocument()
  })

  it('shows a translated error when the rooms cannot be loaded', async () => {
    mockHome({ '/api/teacher/rooms': () => json({ code: 'INTERNAL_ERROR' }, 500) })

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent('Der skete en fejl på serveren.')
  })
})
