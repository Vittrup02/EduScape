import { useCallback, useReducer } from 'react'

/** How many steps back the teacher can undo. */
export const historyLimit = 100

export interface History<T> {
  past: T[]
  present: T
  future: T[]
  /** The coalesce key of the last change; a following change with the same key joins its undo step. */
  lastKey: string | null
}

export type HistoryAction<T> =
  | { type: 'apply'; update: (present: T) => T; coalesceKey?: string }
  | { type: 'undo' }
  | { type: 'redo' }
  /** Ends the current undo step, e.g. when a text field loses focus or a drag ends. */
  | { type: 'seal' }

export function initialHistory<T>(present: T): History<T> {
  return { past: [], present, future: [], lastKey: null }
}

/**
 * Undo and redo over whole versions of the document. Changes that belong together (typing in one field, one drag)
 * share a coalesce key and become one undo step.
 */
export function historyReducer<T>(history: History<T>, action: HistoryAction<T>): History<T> {
  switch (action.type) {
    case 'apply': {
      const next = action.update(history.present)
      if (next === history.present) {
        return history
      }
      const key = action.coalesceKey ?? null
      if (key !== null && key === history.lastKey) {
        return { ...history, present: next, future: [] }
      }
      return {
        past: [...history.past, history.present].slice(-historyLimit),
        present: next,
        future: [],
        lastKey: key,
      }
    }
    case 'undo': {
      const previous = history.past.at(-1)
      if (previous === undefined) {
        return history
      }
      return { past: history.past.slice(0, -1), present: previous, future: [history.present, ...history.future], lastKey: null }
    }
    case 'redo': {
      const [next, ...rest] = history.future
      if (next === undefined) {
        return history
      }
      return { past: [...history.past, history.present], present: next, future: rest, lastKey: null }
    }
    case 'seal':
      return history.lastKey === null ? history : { ...history, lastKey: null }
  }
}

export function useHistory<T>(initial: T) {
  const [history, dispatch] = useReducer(historyReducer<T>, initial, initialHistory)

  const apply = useCallback((update: (present: T) => T, coalesceKey?: string) => {
    dispatch({ type: 'apply', update, coalesceKey })
  }, [])
  const undo = useCallback(() => {
    dispatch({ type: 'undo' })
  }, [])
  const redo = useCallback(() => {
    dispatch({ type: 'redo' })
  }, [])
  const seal = useCallback(() => {
    dispatch({ type: 'seal' })
  }, [])

  return {
    present: history.present,
    canUndo: history.past.length > 0,
    canRedo: history.future.length > 0,
    apply,
    undo,
    redo,
    seal,
  }
}
