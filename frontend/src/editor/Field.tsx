import { useId, type ReactNode } from 'react'
import type { EditorObject } from '../api/editor'

/** A change to the selected object; `field` groups typing in one field into one undo step. */
export type ObjectChange = <T extends EditorObject>(update: (object: T) => T, field?: string) => void

/** A labelled field with an optional hint under it. */
export function Field({ label, hint, children }: { label: string; hint?: string; children: (id: string) => ReactNode }) {
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children(id)}
      {hint !== undefined && <p className="field-hint">{hint}</p>}
    </div>
  )
}
