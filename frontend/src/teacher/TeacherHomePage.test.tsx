import { render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveAuth } from '../api/tokenStorage'
import i18n from '../i18n/i18n'
import { json, mockFetch } from '../test/fetchMock'
import { TeacherHomePage } from './TeacherHomePage'

const rooms = [
  { id: '1', title: 'Brøkernes hemmelighed', subject: 'Mathematics', gradeLevel: 'Grade7' },
  { id: '2', title: 'Flugten fra Christiansborg', subject: 'SocialStudies', gradeLevel: 'Stx2' },
]

describe('TeacherHomePage', () => {
  beforeEach(() => {
    saveAuth({ email: 'ida@skole.dk', accessToken: 'access-1', refreshToken: 'refresh-1' })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it("lists the teacher's rooms with translated subject and grade level", async () => {
    mockFetch({ '/api/teacher/rooms': () => json(rooms) })

    render(<TeacherHomePage />)

    const items = await screen.findAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(within(items[0]).getByText('Brøkernes hemmelighed')).toBeInTheDocument()
    expect(within(items[0]).getByText('Matematik · 7. klasse')).toBeInTheDocument()
    expect(within(items[1]).getByText('Samfundsfag · 2.g')).toBeInTheDocument()
  })

  it('translates subject and grade level into the chosen language', async () => {
    await i18n.changeLanguage('en')
    mockFetch({ '/api/teacher/rooms': () => json(rooms) })

    render(<TeacherHomePage />)

    expect(await screen.findByText('Mathematics · Grade 7')).toBeInTheDocument()
  })

  it('shows a friendly loading message while the server wakes up', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => undefined)))

    render(<TeacherHomePage />)

    expect(screen.getByRole('status')).toHaveTextContent('Henter dine rum...')
  })

  it('says so when the teacher has no rooms yet', async () => {
    mockFetch({ '/api/teacher/rooms': () => json([]) })

    render(<TeacherHomePage />)

    expect(await screen.findByText('Du har ingen rum endnu.')).toBeInTheDocument()
  })

  it('shows a translated error when the rooms cannot be loaded', async () => {
    mockFetch({ '/api/teacher/rooms': () => json({ code: 'INTERNAL_ERROR' }, 500) })

    render(<TeacherHomePage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Der skete en fejl på serveren.')
  })
})
