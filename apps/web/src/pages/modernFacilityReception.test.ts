import { describe, expect, it } from 'vitest'
import { normalizeExpectedDurationMinutes } from './modernFacilityReception'

describe('facility reception timing', () => {
  it('omits a zero or invalid expected duration when starting facility timing', () => {
    expect(normalizeExpectedDurationMinutes(0)).toBeNull()
    expect(normalizeExpectedDurationMinutes(-1)).toBeNull()
    expect(normalizeExpectedDurationMinutes(1441)).toBeNull()
    expect(normalizeExpectedDurationMinutes(undefined)).toBeNull()
    expect(normalizeExpectedDurationMinutes(30)).toBe(30)
  })
})
