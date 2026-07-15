import type { NarutoCodeApi } from './shared/contracts'

declare global {
  interface Window {
    narutoCode: NarutoCodeApi
  }
}

export {}
