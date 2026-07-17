import { describe, expect, it, vi } from 'vitest'
import { debounce } from '../state/debounce'

describe('debounce', () => {
  it('runs once after the wait, coalescing repeated calls', () => {
    vi.useFakeTimers()
    const run = vi.fn()
    const debounced = debounce(run, 100)

    debounced.call()
    vi.advanceTimersByTime(50)
    debounced.call()
    vi.advanceTimersByTime(50)
    expect(run).not.toHaveBeenCalled()
    vi.advanceTimersByTime(50)

    expect(run).toHaveBeenCalledTimes(1)
    vi.useRealTimers()
  })

  it('cancel drops a pending call', () => {
    vi.useFakeTimers()
    const run = vi.fn()
    const debounced = debounce(run, 100)

    debounced.call()
    debounced.cancel()
    vi.advanceTimersByTime(200)

    expect(run).not.toHaveBeenCalled()
    vi.useRealTimers()
  })
})
