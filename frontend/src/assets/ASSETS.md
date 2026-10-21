# Built-in assets

The graphics teachers can choose in the editor without uploading anything. They are registered in [registry.ts](registry.ts) and bundled into the frontend build. Nothing is loaded from a CDN. The database stores only the asset key (e.g. `builtin:padlock`) in `RoomObject.GraphicKey` or `Scene.BackgroundImageKey`. For a built-in object, `UnlockedGraphicKey` stays empty, because the registry supplies the open state.

## Source and licence

All assets below are **original artwork drawn for eduscape** as hand-written SVG (2026). No third-party graphics, fonts or icon sets are used.

**Licence:** © eduscape, all rights reserved. The same terms as the rest of the project.

## Objects

Viewbox 160 × 160 for locks and the letter, 160 × 240 for doors. Both states of an object share the same viewBox, so the object doesn't move when it opens.

| Key | Type | Closed | Open | Source | Licence |
|---|---|---|---|---|---|
| `builtin:padlock` | Lock | `objects/padlock-closed.svg` | `objects/padlock-open.svg` | Original, eduscape | © eduscape |
| `builtin:code-lock` | Lock | `objects/code-lock-closed.svg` | `objects/code-lock-open.svg` | Original, eduscape | © eduscape |
| `builtin:chest` | Lock | `objects/chest-closed.svg` | `objects/chest-open.svg` | Original, eduscape | © eduscape |
| `builtin:safe` | Lock | `objects/safe-closed.svg` | `objects/safe-open.svg` | Original, eduscape | © eduscape |
| `builtin:letter` | Letter | `objects/letter-closed.svg` | `objects/letter-open.svg` | Original, eduscape | © eduscape |
| `builtin:door` | Door | `objects/door-closed.svg` | `objects/door-open.svg` | Original, eduscape | © eduscape |
| `builtin:exit-door` | ExitDoor | `objects/exit-door-closed.svg` | `objects/exit-door-open.svg` | Original, eduscape | © eduscape |

## Backgrounds

Viewbox 1600 × 900 (16:9). Object positions are stored as a percentage of the image.

| Key | File | Source | Licence |
|---|---|---|---|
| `builtin:classroom` | `backgrounds/classroom.svg` | Original, eduscape | © eduscape |
| `builtin:laboratory` | `backgrounds/laboratory.svg` | Original, eduscape | © eduscape |
| `builtin:library` | `backgrounds/library.svg` | Original, eduscape | © eduscape |
| `builtin:basement` | `backgrounds/basement.svg` | Original, eduscape | © eduscape |

## Style

- **Objects are stickers:** an ink outline (`#1D2B3A`, stroke 7) over a white rim (stroke 23), flat colour and one highlight. The rim keeps them readable on light and dark backgrounds, on a projector as well as a tablet.
- **Every open state has the yellow glow** (`#FFE27A`): a sparkle, a lit display, light through a doorway. Students learn that yellow means open.
- **Backgrounds have no outlines.** They are muted, with one light source, and leave free wall and floor space for objects.
- **No text in any SVG.** Text could not be translated.

## Adding an asset

1. Draw the SVG(s) in `library/objects` or `library/backgrounds`. Use only presentation attributes: no `<style>` (our CSP blocks it), `<script>`, `<text>`, `<image>`, event handlers or external links. Give both states of an object the same viewBox.
2. Import the file(s) in `registry.ts` and add an entry with a new `builtin:` key. Never rename or remove a key that is already in use, because rooms in the database refer to it.
3. Add the name under `assets` in **every** file in `src/locales/`.
4. Add a row to the matching table above, with source and licence. If the asset comes from somewhere else, check that its licence allows use in eduscape before you add it.

`registry.test.ts` fails if a file is unused, a key is missing from this document, or an SVG breaks the rules in step 1.
