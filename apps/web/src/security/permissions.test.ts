import { describe, expect, it } from 'vitest'
import { hasPermission, isLocalAuthorizationBypassEnabled, Permission } from './permissions'

describe('hasPermission', () => {
  it('fails closed when the server grants no permissions', () => {
    expect(hasPermission(undefined, Permission.DashboardRead)).toBe(false)
    expect(hasPermission([], Permission.DashboardRead)).toBe(false)
  })

  it('only accepts an explicitly granted permission', () => {
    expect(hasPermission([Permission.CatalogRead], Permission.CatalogRead)).toBe(true)
    expect(hasPermission([Permission.CatalogRead], Permission.CatalogWrite)).toBe(false)
  })

  it('allows the explicit local development bypass', () => {
    expect(hasPermission([], Permission.DashboardRead, true)).toBe(true)
    expect(isLocalAuthorizationBypassEnabled({
      DEV: true,
      VITE_LOCAL_AUTHORIZATION_BYPASS: 'true',
    })).toBe(true)
  })

  it('never enables the local bypass in a production build', () => {
    expect(isLocalAuthorizationBypassEnabled({
      DEV: false,
      VITE_LOCAL_AUTHORIZATION_BYPASS: 'true',
    })).toBe(false)
  })
})
