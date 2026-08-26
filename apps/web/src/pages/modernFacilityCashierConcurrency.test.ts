import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { createSerialTaskQueue, retryVersionConflictOnce } from './modernFacilityCashierConcurrency'

describe('modern facility cashier concurrency', () => {
  it('runs draft updates strictly in order and keeps the queue usable after a failure', async () => {
    const queue = createSerialTaskQueue()
    const events: string[] = []
    let releaseFirst!: () => void
    const firstGate = new Promise<void>((resolve) => { releaseFirst = resolve })

    const first = queue.run(async () => {
      events.push('first:start')
      await firstGate
      events.push('first:end')
      return 1
    })
    const failed = queue.run(async () => {
      events.push('failed:start')
      throw new Error('expected')
    })
    const third = queue.run(async () => {
      events.push('third:start')
      return 3
    })

    await Promise.resolve()
    expect(events).toEqual(['first:start'])
    releaseFirst()

    await expect(first).resolves.toBe(1)
    await expect(failed).rejects.toThrow('expected')
    await expect(third).resolves.toBe(3)
    await queue.idle()
    expect(events).toEqual(['first:start', 'first:end', 'failed:start', 'third:start'])
  })

  it('retries one version conflict but does not retry unrelated failures', async () => {
    const succeedsOnRetry = vi.fn()
      .mockRejectedValueOnce(new ApiError(409, { error: { code: 'VERSION_CONFLICT', message: 'conflict' } }))
      .mockResolvedValueOnce('ok')
    await expect(retryVersionConflictOnce(succeedsOnRetry, 0)).resolves.toBe('ok')
    expect(succeedsOnRetry).toHaveBeenCalledTimes(2)

    const validationFailure = vi.fn()
      .mockRejectedValue(new ApiError(422, { error: { code: 'VALIDATION_FAILED', message: 'invalid' } }))
    await expect(retryVersionConflictOnce(validationFailure, 0)).rejects.toMatchObject({ code: 'VALIDATION_FAILED' })
    expect(validationFailure).toHaveBeenCalledTimes(1)
  })
})
