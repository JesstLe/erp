import { useCallback } from 'react'
import { useAuth } from '../auth/useAuth'
import { hasPermission, isLocalAuthorizationBypassEnabled, type PermissionCode } from './permissions'

const localAuthorizationBypass = isLocalAuthorizationBypassEnabled(import.meta.env)

export function useAuthorization() {
  const auth = useAuth()
  const permissions = auth.user?.permissions
  const can = useCallback(
    (permission: PermissionCode) => hasPermission(permissions, permission, localAuthorizationBypass),
    [permissions],
  )
  return { can, permissions: permissions ?? [] }
}
