import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config.ts'

export default mergeConfig(viteConfig, defineConfig({
  test: {
    // Concurrent Ant Design/jsdom suites compete for CPU on the Linux runner.
    // Serialize files and retain bounded deadlines without removing assertions.
    fileParallelism: false,
    testTimeout: 15_000,
  },
}))
