import { describe, expect, it } from 'vitest'
import type { PlayExitDoor, PlayObject } from '../api/play'
import { playRoom, teamState } from '../test/playRoom'
import { applyTeamEvent, canOpenExitDoor, isAvailable, isSceneUnlocked, revealedHint, unlockedScenes } from './progress'

function object(id: string): PlayObject {
  const found = playRoom.scenes.flatMap((s) => s.objects).find((o) => o.id === id)
  if (found === undefined) {
    throw new Error(`No object ${id}`)
  }
  return found
}

const exit = object('exit') as PlayExitDoor

describe('room progress', () => {
  it('shows objects without unlockedBy from the start', () => {
    const team = teamState()

    expect(isAvailable(playRoom, team, object('letter'))).toBe(true)
    expect(isAvailable(playRoom, team, object('code-lock'))).toBe(true)
    expect(isAvailable(playRoom, team, object('text-lock'))).toBe(false)
  })

  it('reveals an object once the object it waits for is completed', () => {
    const team = teamState({ solvedLockIds: ['code-lock'] })

    expect(isAvailable(playRoom, team, object('text-lock'))).toBe(true)
    expect(isAvailable(playRoom, team, object('door'))).toBe(false)
  })

  it('unlocks the scene behind an opened door', () => {
    const before = teamState({ solvedLockIds: ['code-lock', 'text-lock'] })
    const after = teamState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] })

    expect(isSceneUnlocked(playRoom, before, 'library')).toBe(false)
    expect(isAvailable(playRoom, before, object('choice-lock'))).toBe(false)
    expect(isSceneUnlocked(playRoom, after, 'library')).toBe(true)
    expect(isAvailable(playRoom, after, object('choice-lock'))).toBe(true)
    expect(unlockedScenes(playRoom, after).map((s) => s.id)).toEqual(['classroom', 'library'])
  })

  it('opens the exit door only when every lock is solved', () => {
    const almost = teamState({ solvedLockIds: ['code-lock', 'choice-lock'], openedDoorIds: ['door'] })
    const done = teamState({ solvedLockIds: ['code-lock', 'text-lock', 'choice-lock'], openedDoorIds: ['door'] })

    expect(isAvailable(playRoom, almost, exit)).toBe(true)
    expect(canOpenExitDoor(playRoom, almost, exit)).toBe(false)
    expect(canOpenExitDoor(playRoom, done, exit)).toBe(true)
  })
})

describe('team events', () => {
  it('apply once, however often they arrive', () => {
    let team = teamState()
    for (let i = 0; i < 2; i++) {
      team = applyTeamEvent(team, { type: 'lockSolved', lockId: 'code-lock' })
      team = applyTeamEvent(team, { type: 'hintRevealed', lockId: 'text-lock', hint: 'Del med 3' })
      team = applyTeamEvent(team, { type: 'doorOpened', doorId: 'door' })
    }

    expect(team.solvedLockIds).toEqual(['code-lock'])
    expect(team.openedDoorIds).toEqual(['door'])
    expect(revealedHint(team, 'text-lock')).toBe('Del med 3')
    expect(revealedHint(team, 'code-lock')).toBeNull()
  })

  it('keep the first escape', () => {
    const escaped = applyTeamEvent(teamState(), { type: 'teamEscaped' })

    expect(escaped.escapedAt).not.toBeNull()
    expect(applyTeamEvent(escaped, { type: 'teamEscaped' })).toBe(escaped)
  })
})
