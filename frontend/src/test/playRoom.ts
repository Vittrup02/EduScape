import type { PlayRoom, PlayState, TeamState } from '../api/play'

/**
 * A room like the seeded demo. Classroom: letter, code lock, text lock (after the code lock), door to the library
 * (after the text lock). Library: multiple-choice lock and the exit door (after it).
 */
export const playRoom: PlayRoom = {
  id: 'room',
  title: 'Demo',
  startSceneId: 'classroom',
  scenes: [
    {
      id: 'classroom',
      name: 'Klasselokalet',
      backgroundImageKey: 'builtin:classroom',
      backgroundImageUrl: null,
      narration: null,
      objects: [
        {
          id: 'letter',
          type: 'Letter',
          x: 15,
          y: 70,
          graphicKey: 'builtin:letter',
          unlockedGraphicKey: null,
          unlockedById: null,
          text: 'Tallet står på tavlen: 3 · 4 + 5',
        },
        {
          id: 'code-lock',
          type: 'Lock',
          x: 40,
          y: 45,
          graphicKey: 'builtin:code-lock',
          unlockedGraphicKey: null,
          unlockedById: null,
          question: 'Hvad giver regnestykket?',
          answerType: 'Code',
          options: null,
        },
        {
          id: 'text-lock',
          type: 'Lock',
          x: 65,
          y: 50,
          graphicKey: 'builtin:padlock',
          unlockedGraphicKey: null,
          unlockedById: 'code-lock',
          question: 'Hvad er 10 / 3?',
          answerType: 'Text',
          options: null,
        },
        {
          id: 'door',
          type: 'Door',
          x: 88,
          y: 55,
          graphicKey: 'builtin:door',
          unlockedGraphicKey: null,
          unlockedById: 'text-lock',
          targetSceneId: 'library',
        },
      ],
    },
    {
      id: 'library',
      name: 'Biblioteket',
      backgroundImageKey: 'builtin:library',
      backgroundImageUrl: null,
      narration: null,
      objects: [
        {
          id: 'choice-lock',
          type: 'Lock',
          x: 35,
          y: 40,
          graphicKey: 'builtin:padlock',
          unlockedGraphicKey: null,
          unlockedById: null,
          question: 'Hvilket tal er et primtal?',
          answerType: 'MultipleChoice',
          options: ['9', '15', '17'],
        },
        {
          id: 'exit',
          type: 'ExitDoor',
          x: 85,
          y: 50,
          graphicKey: 'builtin:exit-door',
          unlockedGraphicKey: null,
          unlockedById: 'choice-lock',
        },
      ],
    },
  ],
}

export function teamState(overrides: Partial<TeamState> = {}): TeamState {
  return {
    id: 'red',
    color: 'Red',
    solvedLockIds: [],
    openedDoorIds: [],
    revealedHints: [],
    escapedAt: null,
    ...overrides,
  }
}

export function playState(team: Partial<TeamState> = {}, overrides: Partial<PlayState> = {}): PlayState {
  return {
    sessionId: 's1',
    mode: 'Team',
    status: 'Open',
    participantId: 'p1',
    nickname: 'Ida',
    team: teamState(team),
    room: playRoom,
    isPreview: false,
    ...overrides,
  }
}
