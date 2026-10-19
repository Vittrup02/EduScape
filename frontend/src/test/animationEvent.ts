// jsdom has no AnimationEvent. React checks for it when react-dom loads and otherwise listens for a vendor-prefixed
// animationend that tests can't fire, so onAnimationEnd would never run. Must be imported before react-dom.
if (!('AnimationEvent' in window)) {
  class AnimationEvent extends Event {
    readonly animationName: string
    readonly elapsedTime: number
    readonly pseudoElement: string

    constructor(type: string, init: AnimationEventInit = {}) {
      super(type, init)
      this.animationName = init.animationName ?? ''
      this.elapsedTime = init.elapsedTime ?? 0
      this.pseudoElement = init.pseudoElement ?? ''
    }
  }
  Object.defineProperty(window, 'AnimationEvent', { value: AnimationEvent, configurable: true })
}

export {}
