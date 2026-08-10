/** Delays `run` until `waitMs` has passed with no further `call()`; `cancel()` drops any pending call. */
export function debounce(run: () => void, waitMs: number): { call: () => void; cancel: () => void } {
  let timeoutId: ReturnType<typeof setTimeout> | null = null

  return {
    call() {
      if (timeoutId !== null) {
        clearTimeout(timeoutId)
      }

      timeoutId = setTimeout(run, waitMs)
    },
    cancel() {
      if (timeoutId !== null) {
        clearTimeout(timeoutId)
        timeoutId = null
      }
    },
  }
}
