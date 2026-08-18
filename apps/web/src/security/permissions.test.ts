import { describe, expect, it } from 'vitest'
import { hasPermission, Permission } from './permissions'

describe('hasPermission', () => {
  it('fails closed when the server grants no permissions', () => {
    expect(hasPermission(undefined, Permission.DashboardRead)).toBe(false)
    expect(hasPermission([], Permission.DashboardRead)).toBe(false)
  })

  it('only accepts an explicitly granted permission', () => {
    expect(hasPermission([Permission.CatalogRead], Permission.CatalogRead)).toBe(true)
    expect(hasPermission([Permission.CatalogRead], Permission.CatalogWrite)).toBe(false)
  })
})
