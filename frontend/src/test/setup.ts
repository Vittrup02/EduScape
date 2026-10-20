import './animationEvent'
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach } from 'vitest'
import { backendTiming } from '../api/client'
import i18n from '../i18n/i18n'

const defaultBackendTiming = { ...backendTiming }

beforeEach(async () => {
  // A test that simulates an unreachable backend should not wait the two minutes a real one gets to wake up.
  Object.assign(backendTiming, defaultBackendTiming, { retryDelayMs: 0, giveUpAfterMs: 50 })
  // jsdom reports en-US as the browser language; tests start from the Danish default.
  localStorage.clear()
  await i18n.changeLanguage('da')
})

afterEach(() => {
  cleanup()
})
