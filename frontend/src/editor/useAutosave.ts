import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/client'
import { saveEditorRoom, type RoomDocument, type ValidationError } from '../api/editor'
import { missingRequiredText } from './editorDocument'

/**
 * - saved: everything is on the server.
 * - pending: a change waits for the short pause before saving.
 * - saving: a save is on its way.
 * - waiting: a title or scene name is empty, so the change is not saved until it is filled in.
 * - retrying: the last save failed (e.g. the server is waking up); it is tried again shortly.
 * - conflict: the room was saved elsewhere (another tab); only reloading helps.
 */
export type SaveStatus = 'saved' | 'pending' | 'saving' | 'waiting' | 'retrying' | 'conflict'

export interface Autosave {
  status: SaveStatus
  validationErrors: ValidationError[]
  /** Saves now if anything is unsaved. Resolves to true once the document on screen is on the server. */
  flush: () => Promise<boolean>
}

interface AutosaveOptions {
  delayMs?: number
  retryDelayMs?: number
}

/**
 * Saves the room a moment after each change, always the latest version and never two saves at once. Each save sends
 * the revision the previous one returned, so a stale tab gets a conflict instead of overwriting newer work.
 */
export function useAutosave(
  roomId: string,
  document: RoomDocument,
  initial: { revision: number; validationErrors: ValidationError[] },
  { delayMs = 800, retryDelayMs = 5000 }: AutosaveOptions = {},
): Autosave {
  const [savedDocument, setSavedDocument] = useState(document)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<'retrying' | 'conflict' | null>(null)
  const [validationErrors, setValidationErrors] = useState(initial.validationErrors)

  // The same facts for flush(), which runs between renders.
  const latest = useRef(document)
  const saved = useRef(document)
  const conflict = useRef(false)
  const revision = useRef(initial.revision)
  const inFlight = useRef<Promise<void> | null>(null)

  useEffect(() => {
    latest.current = document
  }, [document])

  const save = useCallback((): Promise<void> => {
    if (inFlight.current !== null) {
      return inFlight.current
    }
    const sending = latest.current
    if (conflict.current || sending === saved.current || missingRequiredText(sending)) {
      return Promise.resolve()
    }

    setSaving(true)
    inFlight.current = (async () => {
      try {
        const result = await saveEditorRoom(roomId, revision.current, sending)
        revision.current = result.revision
        saved.current = sending
        setSavedDocument(sending)
        setValidationErrors(result.validationErrors)
        setFailure(null)
      } catch (error) {
        const isConflict = error instanceof ApiError && error.code === 'ROOM_CHANGED'
        conflict.current = isConflict
        setFailure(isConflict ? 'conflict' : 'retrying')
      } finally {
        inFlight.current = null
        setSaving(false)
      }
    })()
    return inFlight.current
  }, [roomId])

  // After each change (and after each failed save) wait a moment, then save the latest version.
  const unsaved = document !== savedDocument
  const blocked = missingRequiredText(document)
  useEffect(() => {
    if (!unsaved || blocked || saving || failure === 'conflict') {
      return
    }
    const timer = setTimeout(() => void save(), failure === 'retrying' ? retryDelayMs : delayMs)
    return () => {
      clearTimeout(timer)
    }
  }, [document, unsaved, blocked, saving, failure, save, delayMs, retryDelayMs])

  // Leaving the page with unsaved changes asks first.
  useEffect(() => {
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (latest.current !== saved.current && !conflict.current) {
        event.preventDefault()
      }
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => {
      window.removeEventListener('beforeunload', onBeforeUnload)
    }
  }, [])

  const flush = useCallback(async (): Promise<boolean> => {
    for (;;) {
      if (inFlight.current !== null) {
        await inFlight.current
      }
      const wanted = latest.current
      if (wanted === saved.current) {
        return true
      }
      if (conflict.current || missingRequiredText(wanted)) {
        return false
      }
      await save()
      if (saved.current !== wanted) {
        // The save failed; it is tried again in the background.
        return false
      }
    }
  }, [save])

  const status: SaveStatus =
    failure === 'conflict'
      ? 'conflict'
      : saving
        ? 'saving'
        : !unsaved
          ? 'saved'
          : blocked
            ? 'waiting'
            : failure === 'retrying'
              ? 'retrying'
              : 'pending'

  return { status, validationErrors, flush }
}
