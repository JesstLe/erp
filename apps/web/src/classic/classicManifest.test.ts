import { describe, expect, it } from 'vitest'
import { classicUiManifest } from './classicManifest'

describe('classic UI manifest', () => {
  it('keeps every inspected legacy page addressable exactly once', () => {
    const pages = classicUiManifest.modules.flatMap((module) => module.pages)
    expect(classicUiManifest.modules).toHaveLength(12)
    expect(pages).toHaveLength(199)
    expect(new Set(pages.map((page) => page.id)).size).toBe(199)
    expect(pages.every((page) => page.label.length > 0 && page.controls.length > 0)).toBe(true)
    expect(classicUiManifest.sourceSummary.excludedModules).toEqual(['股权', '微信', '云商'])
  })
})
