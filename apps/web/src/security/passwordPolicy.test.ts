import { describe, expect, it } from 'vitest'
import { isPasswordPolicyCompliant } from './passwordPolicy'

describe('password policy', () => {
  it.each([
    'abcd1234',
    'ABCD1234',
    'Mix1234!',
  ])('accepts an 8+ character letter-and-number password: %s', (password) => {
    expect(isPasswordPolicyCompliant(password)).toBe(true)
  })

  it.each([
    'abcdefgh',
    '12345678',
    'abc1234',
  ])('rejects a password missing a required component: %s', (password) => {
    expect(isPasswordPolicyCompliant(password)).toBe(false)
  })
})
