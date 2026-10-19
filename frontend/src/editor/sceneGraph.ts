import type { RoomDocument } from '../api/editor'

export interface SceneNode {
  id: string
  name: string
  /** Steps from the start scene through doors; unreachable scenes come after the reachable ones. */
  column: number
  row: number
  isStart: boolean
  /** False when no chain of doors from the start scene leads here, so students can never see it. */
  reachable: boolean
  hasExitDoor: boolean
}

export interface SceneEdge {
  doorId: string
  from: string
  to: string
}

export interface SceneGraph {
  nodes: SceneNode[]
  edges: SceneEdge[]
  columns: number
  rows: number
}

/**
 * The map of scenes and the doors between them: the start scene first, then each scene in the column of the
 * fewest doors needed to get there. Several doors between the same two scenes are drawn as one edge.
 */
export function sceneGraph(document: RoomDocument): SceneGraph {
  const sceneIds = new Set(document.scenes.map((s) => s.id))
  const edges: SceneEdge[] = []
  for (const scene of document.scenes) {
    for (const object of scene.objects) {
      if (
        object.type === 'Door' &&
        object.targetSceneId !== null &&
        sceneIds.has(object.targetSceneId) &&
        !edges.some((e) => e.from === scene.id && e.to === object.targetSceneId)
      ) {
        edges.push({ doorId: object.id, from: scene.id, to: object.targetSceneId })
      }
    }
  }

  const depth = new Map<string, number>([[document.startSceneId, 0]])
  const queue = [document.startSceneId]
  for (let current = queue.shift(); current !== undefined; current = queue.shift()) {
    const next = (depth.get(current) ?? 0) + 1
    for (const edge of edges) {
      if (edge.from === current && !depth.has(edge.to)) {
        depth.set(edge.to, next)
        queue.push(edge.to)
      }
    }
  }

  const reachableColumns = Math.max(0, ...depth.values()) + 1
  const rowsPerColumn = new Map<number, number>()
  const nodes = document.scenes.map((scene): SceneNode => {
    const reachable = depth.has(scene.id)
    const column = depth.get(scene.id) ?? reachableColumns
    const row = rowsPerColumn.get(column) ?? 0
    rowsPerColumn.set(column, row + 1)
    return {
      id: scene.id,
      name: scene.name,
      column,
      row,
      isStart: scene.id === document.startSceneId,
      reachable,
      hasExitDoor: scene.objects.some((o) => o.type === 'ExitDoor'),
    }
  })

  return {
    nodes,
    edges,
    columns: Math.max(...nodes.map((n) => n.column)) + 1,
    rows: Math.max(...rowsPerColumn.values()),
  }
}
