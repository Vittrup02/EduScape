import { useTranslation } from 'react-i18next'
import type { RoomDocument } from '../api/editor'
import { sceneGraph, type SceneNode } from './sceneGraph'

/** Map units per column and row; the SVG scales to the panel's width. */
const columnWidth = 120
const rowHeight = 56
const nodeWidth = 96
const nodeHeight = 36

function centre(node: SceneNode) {
  return { x: node.column * columnWidth + columnWidth / 2, y: node.row * rowHeight + rowHeight / 2 }
}

interface SceneMapProps {
  document: RoomDocument
  currentSceneId: string
  onOpen: (sceneId: string) => void
}

/**
 * A small map of the scenes and the doors between them, starting from the start scene. Scenes no door leads to are
 * drawn dashed, because students can never get there. Clicking a scene opens it.
 */
export function SceneMap({ document, currentSceneId, onOpen }: SceneMapProps) {
  const { t } = useTranslation()
  const graph = sceneGraph(document)
  const byId = new Map(graph.nodes.map((n) => [n.id, n]))
  const width = graph.columns * columnWidth
  const height = graph.rows * rowHeight

  return (
    <section className="scene-map" aria-labelledby="scene-map-title">
      <h2 id="scene-map-title" className="panel-heading">
        {t('editor.map.title')}
      </h2>
      <div className="scene-map-canvas" style={{ aspectRatio: `${String(width)} / ${String(height)}` }}>
        <svg viewBox={`0 0 ${String(width)} ${String(height)}`} aria-hidden="true">
          <defs>
            <marker id="map-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto">
              <path d="M0 0 L10 5 L0 10 z" className="map-arrowhead" />
            </marker>
          </defs>
          {graph.edges.map((edge) => {
            const from = byId.get(edge.from)
            const to = byId.get(edge.to)
            if (from === undefined || to === undefined) {
              return null
            }
            const a = centre(from)
            const b = centre(to)
            const back = to.column <= from.column
            // Forward edges go from the right side to the left side; edges back (or within a column) curve below.
            const start = { x: a.x + (back ? 0 : nodeWidth / 2), y: a.y + (back ? nodeHeight / 2 : 0) }
            const end = { x: b.x - (back ? 0 : nodeWidth / 2), y: b.y + (back ? nodeHeight / 2 : 0) }
            const bend = back ? `Q ${String((start.x + end.x) / 2)} ${String(Math.max(start.y, end.y) + rowHeight / 2)}` : 'L'
            return (
              <path
                key={edge.doorId}
                className="map-edge"
                d={`M ${String(start.x)} ${String(start.y)} ${bend} ${String(end.x)} ${String(end.y)}`}
                markerEnd="url(#map-arrow)"
              />
            )
          })}
        </svg>
        {graph.nodes.map((node) => {
          const c = centre(node)
          return (
            <button
              key={node.id}
              type="button"
              className="map-node"
              data-start={node.isStart || undefined}
              data-unreachable={!node.reachable || undefined}
              aria-current={node.id === currentSceneId || undefined}
              title={node.reachable ? undefined : t('editor.map.unreachable')}
              style={{
                left: `${String(((c.x - nodeWidth / 2) / width) * 100)}%`,
                top: `${String(((c.y - nodeHeight / 2) / height) * 100)}%`,
                width: `${String((nodeWidth / width) * 100)}%`,
                height: `${String((nodeHeight / height) * 100)}%`,
              }}
              onClick={() => {
                onOpen(node.id)
              }}
            >
              <span className="map-node-name">{node.name}</span>
              <span className="visually-hidden">
                {[
                  node.isStart ? t('editor.map.start') : null,
                  node.hasExitDoor ? t('editor.map.exit') : null,
                  node.reachable ? null : t('editor.map.unreachable'),
                ]
                  .filter((text) => text !== null)
                  .join(' ')}
              </span>
            </button>
          )
        })}
      </div>
      <ul className="map-connections">
        {graph.edges.map((edge) => (
          <li key={edge.doorId}>
            {t('editor.map.connection', { from: byId.get(edge.from)?.name, to: byId.get(edge.to)?.name })}
          </li>
        ))}
        {graph.edges.length === 0 && <li>{t('editor.map.noDoors')}</li>}
      </ul>
    </section>
  )
}
