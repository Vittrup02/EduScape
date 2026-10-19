export interface StagePoint {
  x: number
  y: number
}

/** Where a pointer is on the stage, in percent of its width and height, or null when it is outside. */
export function pointOnStage(stage: HTMLElement | null, clientX: number, clientY: number): StagePoint | null {
  if (stage === null) {
    return null
  }
  const rect = stage.getBoundingClientRect()
  if (rect.width === 0 || clientX < rect.left || clientX > rect.right || clientY < rect.top || clientY > rect.bottom) {
    return null
  }
  return { x: ((clientX - rect.left) / rect.width) * 100, y: ((clientY - rect.top) / rect.height) * 100 }
}
