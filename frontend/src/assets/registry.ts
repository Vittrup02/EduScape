import type { Translation } from '../i18n/languages'
import basement from './library/backgrounds/basement.svg'
import classroom from './library/backgrounds/classroom.svg'
import laboratory from './library/backgrounds/laboratory.svg'
import library from './library/backgrounds/library.svg'
import codeLockClosed from './library/objects/code-lock-closed.svg'
import codeLockOpen from './library/objects/code-lock-open.svg'
import doorClosed from './library/objects/door-closed.svg'
import doorOpen from './library/objects/door-open.svg'
import exitDoorClosed from './library/objects/exit-door-closed.svg'
import exitDoorOpen from './library/objects/exit-door-open.svg'
import letterClosed from './library/objects/letter-closed.svg'
import letterOpen from './library/objects/letter-open.svg'
import padlockClosed from './library/objects/padlock-closed.svg'
import padlockOpen from './library/objects/padlock-open.svg'

/** The same object types as the backend's RoomObjectType. */
export type ObjectAssetType = 'Lock' | 'Letter' | 'Door' | 'ExitDoor'
export type AssetType = ObjectAssetType | 'Background'
export type ObjectState = 'closed' | 'open'

/** i18n key of an asset's display name, e.g. "assets.padlock". Type-checked against da.json. */
export type AssetNameKey = `assets.${keyof Translation['assets']}`

interface AssetBase {
  /** What the database stores (RoomObject.GraphicKey / Scene.BackgroundImageKey). Never change an existing key. */
  key: string
  nameKey: AssetNameKey
}

export interface ObjectAsset extends AssetBase {
  type: ObjectAssetType
  /** One image per state. Both share a viewBox, so swapping them doesn't move the object. */
  files: Record<ObjectState, string>
  /** Width / height, so the editor and play view can size the object before the image has loaded. */
  aspectRatio: number
}

export interface BackgroundAsset extends AssetBase {
  type: 'Background'
  files: { image: string }
}

export type Asset = ObjectAsset | BackgroundAsset

/**
 * Prefix of built-in asset keys. Uploaded images use generated R2 object keys in the same columns,
 * so the prefix keeps the two apart.
 */
export const builtInPrefix = 'builtin:'

const square = 1
const doorShaped = 160 / 240

/**
 * The built-in asset library. Every file is bundled into the frontend build; nothing is loaded from a CDN.
 * To add an asset, see ASSETS.md in this folder.
 */
export const assets: readonly Asset[] = [
  {
    key: 'builtin:padlock',
    type: 'Lock',
    nameKey: 'assets.padlock',
    files: { closed: padlockClosed, open: padlockOpen },
    aspectRatio: square,
  },
  {
    key: 'builtin:code-lock',
    type: 'Lock',
    nameKey: 'assets.codeLock',
    files: { closed: codeLockClosed, open: codeLockOpen },
    aspectRatio: square,
  },
  {
    key: 'builtin:letter',
    type: 'Letter',
    nameKey: 'assets.letter',
    files: { closed: letterClosed, open: letterOpen },
    aspectRatio: square,
  },
  {
    key: 'builtin:door',
    type: 'Door',
    nameKey: 'assets.door',
    files: { closed: doorClosed, open: doorOpen },
    aspectRatio: doorShaped,
  },
  {
    key: 'builtin:exit-door',
    type: 'ExitDoor',
    nameKey: 'assets.exitDoor',
    files: { closed: exitDoorClosed, open: exitDoorOpen },
    aspectRatio: doorShaped,
  },
  { key: 'builtin:classroom', type: 'Background', nameKey: 'assets.classroom', files: { image: classroom } },
  { key: 'builtin:laboratory', type: 'Background', nameKey: 'assets.laboratory', files: { image: laboratory } },
  { key: 'builtin:library', type: 'Background', nameKey: 'assets.library', files: { image: library } },
  { key: 'builtin:basement', type: 'Background', nameKey: 'assets.basement', files: { image: basement } },
]

export function isBuiltInAssetKey(key: string): boolean {
  return key.startsWith(builtInPrefix)
}

/** The built-in asset with this key, or undefined for an unknown key or an uploaded image's R2 key. */
export function findAsset(key: string): Asset | undefined {
  return assets.find((asset) => asset.key === key)
}

export type AssetOfType<T extends AssetType> = T extends 'Background' ? BackgroundAsset : ObjectAsset

/** The choices the editor offers for an object or background of this type. */
export function assetsOfType<T extends AssetType>(type: T): AssetOfType<T>[] {
  return assets.filter((asset): asset is AssetOfType<T> => asset.type === type)
}
