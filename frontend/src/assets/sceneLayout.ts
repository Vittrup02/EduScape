import type { CSSProperties } from 'react'
import { assetsOfType, findAsset, type ObjectAsset, type ObjectAssetType } from './registry'

/**
 * How objects sit on a scene, shared by the play view and the editor so an object lands exactly where the teacher
 * put it. Positions are the object's centre in percent of the scene's width and height.
 */

/** Object height in percent of the scene's height; doors stand taller than locks and letters. */
export const objectHeight: Record<ObjectAssetType, number> = { Lock: 16, Letter: 14, Door: 34, ExitDoor: 34 }

/** Scenes are 16:9, like the built-in backgrounds. */
export const sceneAspectRatio = 16 / 9

export function percent(value: number): string {
  return `${String(value)}%`
}

/** The object's built-in graphic. Unknown keys (e.g. uploads, which objects can't use yet) get the type's default. */
export function objectAsset(type: ObjectAssetType, graphicKey: string): ObjectAsset {
  const asset = findAsset(graphicKey)
  return asset !== undefined && asset.type === type ? asset : assetsOfType(type)[0]
}

/** Left, top and width of an object; the CSS (.room-object) centres it on that point. */
export function objectStyle(x: number, y: number, asset: ObjectAsset): CSSProperties {
  return {
    left: percent(x),
    top: percent(y),
    width: percent((objectHeight[asset.type] * asset.aspectRatio) / sceneAspectRatio),
  }
}

/** A built-in background's file, or the short-lived link the server sent for an uploaded one. */
export function backgroundImage(key: string, url: string | null = null): string | undefined {
  const asset = findAsset(key)
  return asset?.type === 'Background' ? asset.files.image : (url ?? undefined)
}
