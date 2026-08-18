import { useCallback } from 'react'
import { useAuth } from '../auth/useAuth'
import type { PermissionCode } from './permissions'

export function useAuthorization() {
  const auth = useAuth()
  const permissions = auth.user?.permissions
  const can = useCallback((permission: PermissionCode) => permissions?.includes(permission) ?? false, [permissions])
  return { can, permissions: permissions ?? [] }
}
