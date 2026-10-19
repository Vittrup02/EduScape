import { describe, expect, it } from 'vitest'
import i18n from '../i18n/i18n'
import { languageCodes } from '../i18n/languages'
import assetsDoc from './ASSETS.md?raw'
import { assets, assetsOfType, builtInPrefix, findAsset, isBuiltInAssetKey } from './registry'

// Every SVG in the library, as the same URLs the registry's imports resolve to.
const svgUrls = import.meta.glob<string>('./library/**/*.svg', { eager: true, import: 'default' })
// The raw markup, to check what the files contain.
const svgSources = import.meta.glob<string>('./library/**/*.svg', { eager: true, query: '?raw', import: 'default' })

const filesOf = (asset: (typeof assets)[number]): string[] => Object.values(asset.files)

describe('asset registry', () => {
  it('has unique, built-in keys', () => {
    const keys = assets.map((asset) => asset.key)

    expect(new Set(keys).size).toBe(keys.length)
    expect(keys.every((key) => key.startsWith(builtInPrefix))).toBe(true)
  })

  it('gives every object a different file for its closed and open state', () => {
    for (const asset of assets) {
      if (asset.type !== 'Background') {
        expect(asset.files.open, asset.key).not.toBe(asset.files.closed)
      }
    }
  })

  it('lists the choices for each type', () => {
    expect(assetsOfType('Lock').map((asset) => asset.key)).toEqual(['builtin:padlock', 'builtin:code-lock'])
    expect(assetsOfType('Letter')).toHaveLength(1)
    expect(assetsOfType('Door')).toHaveLength(1)
    expect(assetsOfType('ExitDoor')).toHaveLength(1)
    expect(assetsOfType('Background').map((asset) => asset.key)).toEqual([
      'builtin:classroom',
      'builtin:laboratory',
      'builtin:library',
      'builtin:basement',
    ])
  })

  it('finds assets by key and ignores uploaded images', () => {
    expect(findAsset('builtin:door')?.type).toBe('Door')
    expect(findAsset('builtin:unknown')).toBeUndefined()
    expect(findAsset('uploads/3f2a9c.png')).toBeUndefined()
    expect(isBuiltInAssetKey('builtin:door')).toBe(true)
    expect(isBuiltInAssetKey('uploads/3f2a9c.png')).toBe(false)
  })

  it.each(languageCodes)('names every asset in %s', (language) => {
    const t = i18n.getFixedT(language)
    for (const asset of assets) {
      expect(t(asset.nameKey), asset.key).not.toBe(asset.nameKey)
    }
  })

  it('uses every SVG in the library, and only files that exist', () => {
    const used = assets.flatMap(filesOf).sort()

    expect(used).toEqual(Object.values(svgUrls).sort())
  })

  it('documents every asset in ASSETS.md', () => {
    for (const asset of assets) {
      expect(assetsDoc, asset.key).toContain(`\`${asset.key}\``)
    }
  })
})

describe('asset SVG files', () => {
  const files = Object.entries(svgSources)

  it.each(files)('%s is a self-contained, static SVG', (_path, source) => {
    expect(source).toMatch(/<svg[^>]*\sviewBox="0 0 \d+ \d+"/)
    // No scripts or event handlers, no <style> (blocked by our CSP), no text (it couldn't be translated)
    // and nothing loaded from elsewhere: only internal #references.
    expect(source).not.toMatch(/<script|<style|<text|<image|<foreignObject/i)
    expect(source).not.toMatch(/\son[a-z]+\s*=/i)
    expect(source).not.toMatch(/data:/)
    expect(source.match(/https?:\/\/[^"]+/g)).toEqual(['http://www.w3.org/2000/svg'])
    for (const [, target] of source.matchAll(/href="([^"]*)"/g)) {
      expect(target).toMatch(/^#/)
    }
  })

  it.each(files)('%s stays small', (_path, source) => {
    expect(source.length).toBeLessThan(30_000)
  })
})
