import { describe, expect, it } from 'vitest'
import { languages } from '../i18n/languages'

// Every JSON file in this folder, so a new language is checked as soon as the file exists.
const files = import.meta.glob<unknown>('./*.json', { eager: true, import: 'default' })
const fileNames = Object.keys(files).map((path) => path.replace('./', ''))

function flattenKeys(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) {
    return [prefix]
  }
  return Object.entries(value).flatMap(([key, child]) =>
    flattenKeys(child, prefix ? `${prefix}.${key}` : key),
  )
}

function keysOf(fileName: string): string[] {
  return flattenKeys(files[`./${fileName}`]).sort()
}

describe('locale files', () => {
  it('include Danish and English', () => {
    expect(fileNames).toEqual(expect.arrayContaining(['da.json', 'en.json']))
  })

  it.each(fileNames)('%s has exactly the same keys as da.json', (fileName) => {
    expect(keysOf(fileName)).toEqual(keysOf('da.json'))
  })

  it.each(fileNames)('%s has no empty texts', (fileName) => {
    const empty = keysOf(fileName).filter((key) => {
      const text = key.split('.').reduce<unknown>(
        (node, part) => (node as Record<string, unknown>)[part],
        files[`./${fileName}`],
      )
      return typeof text !== 'string' || text.trim() === ''
    })
    expect(empty).toEqual([])
  })

  it('are all registered in languages.ts', () => {
    expect(Object.keys(languages).map((code) => `${code}.json`).sort()).toEqual([...fileNames].sort())
  })
})
