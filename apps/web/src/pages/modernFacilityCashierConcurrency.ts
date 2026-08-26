import { ApiError } from '../api/client'

export interface SerialTaskQueue {
  run<T>(task: () => Promise<T>): Promise<T>
  idle(): Promise<void>
}

export function createSerialTaskQueue(): SerialTaskQueue {
  let tail: Promise<void> = Promise.resolve()

  return {
    run<T>(task: () => Promise<T>) {
      const result = tail.then(task, task)
      tail = result.then(() => undefined, () => undefined)
      return result
    },
    idle() {
      return tail
    },
  }
}

export async function retryVersionConflictOnce<T>(operation: () => Promise<T>, delayMs = 100): Promise<T> {
  try {
    return await operation()
  } catch (error) {
    if (!(error instanceof ApiError) || error.code !== 'VERSION_CONFLICT') throw error
    if (delayMs > 0) await new Promise((resolve) => window.setTimeout(resolve, delayMs))
    return operation()
  }
}
