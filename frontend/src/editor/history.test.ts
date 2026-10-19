import { describe, expect, it } from 'vitest'
import { historyLimit, historyReducer, initialHistory, type History } from './history'

function apply(history: History<string>, value: string, coalesceKey?: string) {
  return historyReducer(history, { type: 'apply', update: () => value, coalesceKey })
}

describe('history', () => {
  it('undoes and redoes whole versions', () => {
    let history = apply(apply(initialHistory('a'), 'b'), 'c')

    history = historyReducer(history, { type: 'undo' })
    expect(history.present).toBe('b')
    history = historyReducer(history, { type: 'undo' })
    expect(history.present).toBe('a')
    history = historyReducer(history, { type: 'undo' })
    expect(history.present).toBe('a')

    history = historyReducer(history, { type: 'redo' })
    expect(history.present).toBe('b')
  })

  it('a new change after undo drops what could be redone', () => {
    let history = historyReducer(apply(apply(initialHistory('a'), 'b'), 'c'), { type: 'undo' })

    history = apply(history, 'x')

    expect(history.future).toEqual([])
    expect(history.past).toEqual(['a', 'b'])
  })

  it('joins changes with the same key into one step until it is sealed', () => {
    let history = apply(initialHistory(''), 'H', 'title')
    history = apply(history, 'Ha', 'title')
    history = apply(history, 'Hal', 'title')
    expect(history.past).toEqual([''])

    history = historyReducer(history, { type: 'seal' })
    history = apply(history, 'Hall', 'title')
    expect(history.past).toEqual(['', 'Hal'])

    expect(historyReducer(history, { type: 'undo' }).present).toBe('Hal')
  })

  it('ignores changes that change nothing', () => {
    const history = initialHistory('a')

    expect(historyReducer(history, { type: 'apply', update: (v) => v })).toBe(history)
  })

  it('keeps a limited number of steps', () => {
    let history = initialHistory('0')
    for (let i = 1; i <= historyLimit + 10; i++) {
      history = apply(history, String(i))
    }

    expect(history.past).toHaveLength(historyLimit)
    expect(history.past[0]).toBe('10')
  })
})
