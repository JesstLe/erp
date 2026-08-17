import { createContext, useContext } from 'react'
import type { AuthorizedStore, CurrentUser } from '../api/types'

export interface AuthState {
  user?: CurrentUser
  store?: AuthorizedStore
  loading: boolean
  setStore: (store: AuthorizedStore) => void
  refresh: () => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthState | undefined>(undefined)

export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}

