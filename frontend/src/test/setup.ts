import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach } from 'vitest'
import i18n from '../i18n/i18n'

beforeEach(async () => {
  // jsdom reports en-US as the browser language; tests start from the Danish default.
  localStorage.clear()
  await i18n.changeLanguage('da')
})

afterEach(() => {
  cleanup()
})
