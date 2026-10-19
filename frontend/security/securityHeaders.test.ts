import { describe, expect, it } from 'vitest'
import { contentSecurityPolicy, headersFile } from './securityHeaders.ts'

function directive(policy: string, name: string): string[] {
  const found = policy.split('; ').find((d) => d.startsWith(`${name} `))
  return found?.split(' ').slice(1) ?? []
}

describe('security headers', () => {
  it('lets the app reach the API over https and wss for SignalR', () => {
    const policy = contentSecurityPolicy({ apiUrl: 'https://eduscape-api.onrender.com/' })

    expect(directive(policy, 'connect-src')).toEqual([
      "'self'",
      'https://eduscape-api.onrender.com',
      'wss://eduscape-api.onrender.com',
    ])
  })

  it('uses http and ws for the local backend', () => {
    const policy = contentSecurityPolicy({ apiUrl: 'http://localhost:5115' })

    expect(directive(policy, 'connect-src')).toEqual(["'self'", 'http://localhost:5115', 'ws://localhost:5115'])
  })

  it('only allows our own scripts, styles and fonts, never inline', () => {
    const policy = contentSecurityPolicy({ apiUrl: 'https://api.example' })

    expect(directive(policy, 'default-src')).toEqual(["'self'"])
    expect(directive(policy, 'script-src')).toEqual(["'self'"])
    expect(directive(policy, 'style-src')).toEqual(["'self'"])
    expect(directive(policy, 'font-src')).toEqual(["'self'"])
    expect(policy).not.toContain('unsafe-')
  })

  it('adds extra image sources to img-src', () => {
    const policy = contentSecurityPolicy({ apiUrl: 'https://api.example', imageSources: ['https://images.example'] })

    expect(directive(policy, 'img-src')).toEqual(["'self'", 'https://images.example'])
  })

  it('lets the browser upload to and show images from the image storage, and nothing more of it', () => {
    const policy = contentSecurityPolicy({
      apiUrl: 'https://api.example',
      imageStorageUrl: 'https://abc123.eu.r2.cloudflarestorage.com/some/path',
    })

    expect(directive(policy, 'img-src')).toEqual(["'self'", 'https://abc123.eu.r2.cloudflarestorage.com'])
    expect(directive(policy, 'connect-src')).toContain('https://abc123.eu.r2.cloudflarestorage.com')
    expect(directive(policy, 'script-src')).toEqual(["'self'"])
  })

  it('leaves the image storage out when none is configured, and rejects plain http', () => {
    expect(directive(contentSecurityPolicy({ apiUrl: 'https://api.example', imageStorageUrl: '' }), 'img-src')).toEqual([
      "'self'",
    ])
    expect(() => contentSecurityPolicy({ apiUrl: 'https://api.example', imageStorageUrl: 'http://bucket.example' })).toThrow(
      'VITE_IMAGE_STORAGE_URL',
    )
  })

  it('forbids framing the app', () => {
    const policy = contentSecurityPolicy({ apiUrl: 'https://api.example' })

    expect(directive(policy, 'frame-ancestors')).toEqual(["'none'"])
  })

  it('rejects an API URL that is not http(s)', () => {
    expect(() => contentSecurityPolicy({ apiUrl: 'ftp://api.example' })).toThrow('VITE_API_URL')
    expect(() => contentSecurityPolicy({ apiUrl: 'not a url' })).toThrow()
  })

  it('applies every header to all paths', () => {
    const lines = headersFile({ apiUrl: 'https://api.example' }).split('\n')

    expect(lines).toContain('/*')
    expect(lines).toContain('  X-Content-Type-Options: nosniff')
    expect(lines).toContain('  Referrer-Policy: no-referrer')
    expect(lines.some((line) => line.startsWith('  Content-Security-Policy: default-src '))).toBe(true)
  })
})
