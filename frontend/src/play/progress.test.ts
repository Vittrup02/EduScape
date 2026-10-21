import { describe, expect, it } from 'vitest'
import type { PlayExitDoor, PlayLock, PlayObject } from '../api/play'
import { playRoom, step, teamState } from '../test/playRoom'
import {
  applyTeamEvent,
  canOpenExitDoor,
  cooldownRemaining,
  currentStep,
  isAvailable,
  isCompleted,
  isSceneUnlocked,
  revealedHint,
  unlockedScenes,
} from './progress'

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
  })

  it('always shows doors, shut until the server says they are open', () => {
    const shut = teamState({ solvedLockIds: ['code-lock'] })
    const open = teamState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] })

    expect(isAvailable(playRoom, shut, object('door'))).toBe(true)
    expect(isCompleted(object('door'), shut)).toBe(false)
    expect(isCompleted(object('door'), open)).toBe(true)
  })

  it('unlocks the scene behind an open door', () => {
    const before = teamState({ solvedLockIds: ['code-lock', 'text-lock'] })
    const after = teamState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] })

    expect(isSceneUnlocked(playRoom, before, 'library')).toBe(false)
    expect(isAvailable(playRoom, before, object('choice-lock'))).toBe(false)
    expect(isSceneUnlocked(playRoom, after, 'library')).toBe(true)
    expect(isAvailable(playRoom, after, object('choice-lock'))).toBe(true)
    expect(unlockedScenes(playRoom, after).map((s) => s.id)).toEqual(['classroom', 'library'])
  })

  it('lets the team out once the exit door is open', () => {
    const shut = teamState({ solvedLockIds: ['code-lock', 'text-lock'], openedDoorIds: ['door'] })
    const open = teamState({ solvedLockIds: ['code-lock', 'text-lock', 'choice-lock'], openedDoorIds: ['door', 'exit'] })

    expect(isAvailable(playRoom, shut, exit)).toBe(true)
    expect(canOpenExitDoor(playRoom, shut, exit)).toBe(false)
    expect(canOpenExitDoor(playRoom, open, exit)).toBe(true)
  })

  it('knows the step the team is on, and how long a lock still waits', () => {
    const lock = { ...(object('code-lock') as PlayLock), steps: [step('Trin 1', 'Code'), step('Trin 2', 'Text')] }
    const team = teamState({ locks: [{ lockId: 'code-lock', solvedSteps: 1, hint: null, cooldownUntil: 10_000 }] })

    expect(currentStep(teamState(), lock)).toBe(0)
    expect(currentStep(team, lock)).toBe(1)
    expect(currentStep(teamState({ solvedLockIds: ['code-lock'] }), lock)).toBe(2)
    expect(cooldownRemaining(team, 'code-lock', 1_000)).toBe(9)
    expect(cooldownRemaining(team, 'code-lock', 9_500)).toBe(1)
    expect(cooldownRemaining(team, 'code-lock', 10_000)).toBe(0)
    expect(cooldownRemaining(team, 'text-lock', 0)).toBe(0)
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

  it('move the team on to the next step, which starts without a hint or a wait', () => {
    let team = teamState()
    team = applyTeamEvent(team, { type: 'hintRevealed', lockId: 'code-lock', hint: 'Gang først' })
    team = applyTeamEvent(team, { type: 'lockCooldown', lockId: 'code-lock', until: 5_000 })
    team = applyTeamEvent(team, { type: 'stepSolved', lockId: 'code-lock', solvedSteps: 1 })

    expect(team.locks).toEqual([{ lockId: 'code-lock', solvedSteps: 1, hint: null, cooldownUntil: null }])
    // A late or repeated push never moves the team back.
    expect(applyTeamEvent(team, { type: 'stepSolved', lockId: 'code-lock', solvedSteps: 1 })).toBe(team)
    expect(applyTeamEvent(team, { type: 'stepSolved', lockId: 'code-lock', solvedSteps: 0 })).toBe(team)

    const solved = applyTeamEvent(team, { type: 'lockSolved', lockId: 'code-lock' })
    expect(solved.solvedLockIds).toEqual(['code-lock'])
    expect(solved.locks).toEqual([])
  })

  it('keep the first escape', () => {
    const escaped = applyTeamEvent(teamState(), { type: 'teamEscaped' })

    expect(escaped.escapedAt).not.toBeNull()
    expect(applyTeamEvent(escaped, { type: 'teamEscaped' })).toBe(escaped)
  })
})
