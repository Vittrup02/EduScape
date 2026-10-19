import { describe, expect, it } from 'vitest'
import type { EditorDoor, EditorScene, RoomDocument } from '../api/editor'
import { sceneGraph } from './sceneGraph'

function door(id: string, targetSceneId: string | null): EditorDoor {
  return { id, type: 'Door', x: 50, y: 50, graphicKey: 'builtin:door', unlockedById: null, targetSceneId }
}

function scene(id: string, ...objects: EditorScene['objects']): EditorScene {
  return { id, name: id, backgroundImageKey: 'builtin:classroom', backgroundImageUrl: null, narration: '', objects }
}

function room(startSceneId: string, ...scenes: EditorScene[]): RoomDocument {
  return { title: 'Rum', subject: 'Mathematics', gradeLevel: 'Grade6', startSceneId, scenes }
}

describe('scene graph', () => {
  it('places each scene by the doors needed to get there from the start scene', () => {
    const graph = sceneGraph(
      room(
        'b',
        scene('a'),
        scene('b', door('b-a', 'a'), door('b-c', 'c')),
        scene('c', door('c-d', 'd'), door('c-b', 'b')),
        scene('d', { id: 'exit', type: 'ExitDoor', x: 1, y: 1, graphicKey: 'builtin:exit-door', unlockedById: null }),
      ),
    )

    const column = Object.fromEntries(graph.nodes.map((n) => [n.id, n.column]))
    expect(column).toEqual({ b: 0, a: 1, c: 1, d: 2 })
    expect(graph.nodes.find((n) => n.id === 'b')?.isStart).toBe(true)
    expect(graph.nodes.find((n) => n.id === 'd')?.hasExitDoor).toBe(true)
    expect(graph.edges).toHaveLength(4)
    expect(graph.columns).toBe(3)
    expect(graph.rows).toBe(2)
  })

  it('marks scenes no door leads to, and ignores doors without a target in the room', () => {
    const graph = sceneGraph(room('a', scene('a', door('nowhere', null), door('gone', 'deleted')), scene('island')))

    expect(graph.edges).toEqual([])
    expect(graph.nodes.find((n) => n.id === 'island')).toMatchObject({ reachable: false, column: 1 })
  })

  it('draws several doors between the same scenes as one arrow', () => {
    const graph = sceneGraph(room('a', scene('a', door('one', 'b'), door('two', 'b')), scene('b')))

    expect(graph.edges).toEqual([{ doorId: 'one', from: 'a', to: 'b' }])
  })
})
