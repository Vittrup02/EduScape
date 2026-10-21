import type { EditorLock, EditorOrderItem, EditorStep } from '../api/editor'
import { newId, randomId } from './ids'

/**
 * The kinds of puzzle a teacher picks for a lock, on four big cards. "Steps" is not an answer type of its own: it is a
 * puzzle of several steps, each of one of the other three kinds. "Typed" covers both free text (or a number with a
 * tolerance) and an exact code.
 */
export type PuzzleKind = 'MultipleChoice' | 'Typed' | 'Order' | 'Steps'
export type StepKind = Exclude<PuzzleKind, 'Steps'>

export const puzzleKinds: readonly PuzzleKind[] = ['MultipleChoice', 'Typed', 'Order', 'Steps']
export const stepKinds: readonly StepKind[] = ['MultipleChoice', 'Typed', 'Order']

/** A new lock starts with this many items to put in order, so the teacher only has to fill them in. */
const newOrderItems = 3

export function stepKind(step: EditorStep): StepKind {
  return step.answerType === 'MultipleChoice' || step.answerType === 'Order' ? step.answerType : 'Typed'
}

/** The kind of the lock's puzzle, or null while the teacher hasn't picked one. */
export function puzzleKind(lock: EditorLock): PuzzleKind | null {
  if (lock.steps.length === 0) {
    return null
  }
  return lock.steps.length > 1 ? 'Steps' : stepKind(lock.steps[0])
}

export function createOrderItem(): EditorOrderItem {
  return { id: randomId(), text: '', imageKey: null, imageUrl: null }
}

function emptyStep(): EditorStep {
  return {
    id: newId(),
    question: '',
    answerType: 'Text',
    correctAnswer: '',
    tolerance: null,
    hint: null,
    options: [],
    acceptedAnswers: [],
    acceptTypos: false,
    orderItems: [],
  }
}

/** A new, empty step of this kind, with the blanks the teacher fills in. */
export function createStep(kind: StepKind): EditorStep {
  return changeStepKind(emptyStep(), kind)
}

/** The steps of a new puzzle: one step, or two to begin with for a puzzle of several steps. */
export function createPuzzle(kind: PuzzleKind): EditorStep[] {
  return kind === 'Steps' ? [createStep('Typed'), createStep('Typed')] : [createStep(kind)]
}

/**
 * Switches a step to another kind. Nothing typed is lost: options, alternatives and items stay with the step, so
 * switching back restores them. A new multiple choice starts with the current answer as its first option.
 */
export function changeStepKind(step: EditorStep, kind: StepKind): EditorStep {
  if (stepKind(step) === kind) {
    return step
  }
  switch (kind) {
    case 'Typed':
      return { ...step, answerType: 'Text' }
    case 'MultipleChoice':
      return {
        ...step,
        answerType: 'MultipleChoice',
        options:
          step.options.length >= 2
            ? step.options
            : [
                { id: randomId(), text: step.correctAnswer },
                { id: randomId(), text: '' },
              ],
      }
    case 'Order':
      return {
        ...step,
        answerType: 'Order',
        orderItems: step.orderItems.length >= 2 ? step.orderItems : Array.from({ length: newOrderItems }, createOrderItem),
      }
  }
}

/** The list with the item at `from` moved to `to`: the order of the list is what counts. */
export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  const next = [...items]
  next.splice(to, 0, ...next.splice(from, 1))
  return next
}

/** "3,5" and "3.5" both mean 3.5, like the answers students type. Null when the text is not a number. */
export function parseNumber(text: string): number | null {
  const trimmed = text.trim().replace(',', '.')
  if (trimmed === '' || !/^[+-]?(\d+\.?\d*|\.\d+)$/.test(trimmed)) {
    return null
  }
  return Number(trimmed)
}

/** Spelling mistakes are never forgiven in shorter answers, so "Na" and "Ne" stay different. Same as the server. */
export const minLengthForTypos = 5
export const minLengthForTwoTypos = 10

/** How many spelling mistakes the server forgives in an answer whose correct form is this, when the teacher allows them. */
export function typosAllowed(answer: string): number {
  const length = answer.trim().length
  return length >= minLengthForTwoTypos ? 2 : length >= minLengthForTypos ? 1 : 0
}
