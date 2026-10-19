import { describe, expect, it } from 'vitest'
import { toApiError } from './client'

describe('toApiError', () => {
  it('reads the error code from the response body', async () => {
    const response = new Response(JSON.stringify({ code: 'SESSION_CLOSED' }), { status: 400 })

    const error = await toApiError(response)

    expect(error.code).toBe('SESSION_CLOSED')
    expect(error.status).toBe(400)
  })

  it.each([
    ['a non-JSON body', '<html>Bad gateway</html>'],
    ['JSON without a code', JSON.stringify({ title: 'Bad Request' })],
    ['an empty body', ''],
  ])('uses UNKNOWN for %s', async (_, body) => {
    const error = await toApiError(new Response(body, { status: 502 }))

    expect(error.code).toBe('UNKNOWN')
    expect(error.status).toBe(502)
  })
})
