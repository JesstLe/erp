import { describe, expect, it } from 'vitest'
import { isValidPeriod, toLocalDateTimeValue, toUtcIso } from './schedulingRules'

describe('scheduling time rules', () => {
  it('converts local form values to UTC without changing the instant', () => {
    const local = toLocalDateTimeValue('2026-08-19T02:30:00.000Z')
    expect(new Date(toUtcIso(local)).toISOString()).toBe('2026-08-19T02:30:00.000Z')
  })

  it('rejects reversed and undersized periods', () => {
    expect(isValidPeriod('2026-08-19T10:00', '2026-08-19T09:00', 5)).toBe(false)
    expect(isValidPeriod('2026-08-19T10:00', '2026-08-19T10:04', 5)).toBe(false)
    expect(isValidPeriod('2026-08-19T10:00', '2026-08-19T10:05', 5)).toBe(true)
  })
})
