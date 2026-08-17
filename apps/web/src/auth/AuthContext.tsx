import { useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRequest, resetCsrfToken } from '../api/client'
import type { AuthorizedStore, CurrentUser } from '../api/types'
import { AuthContext, type AuthState } from './useAuth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [selectedStore, setSelectedStore] = useState<AuthorizedStore>()
  const [signedOut, setSignedOut] = useState(false)
  const query = useQuery({ queryKey: ['current-user'], queryFn: () => apiRequest<CurrentUser>('/api/v1/auth/me'), retry: false, staleTime: 60_000 })
  const user = signedOut ? undefined : query.data
  const store = selectedStore ?? user?.stores.find((item) => item.isDefault) ?? user?.stores[0]
  const value: AuthState = {
    user,
    store,
    loading: query.isLoading,
    setStore: setSelectedStore,
    refresh: async () => { setSignedOut(false); await query.refetch() },
    logout: async () => {
      await apiRequest<void>('/api/v1/auth/logout', { method: 'POST' })
      resetCsrfToken()
      setSelectedStore(undefined)
      setSignedOut(true)
      await queryClient.cancelQueries({ queryKey: ['current-user'], exact: true })
      queryClient.removeQueries({ queryKey: ['current-user'], exact: true })
    },
  }
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
