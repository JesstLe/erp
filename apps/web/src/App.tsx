import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { Spin } from 'antd'
import { useAuth } from './auth/useAuth'
import { AppLayout } from './layout/AppLayout'
import { LoginPage } from './pages/LoginPage'
import { DashboardPage } from './pages/DashboardPage'
import { ServiceItemsPage } from './pages/ServiceItemsPage'
import { PriceBooksPage } from './pages/PriceBooksPage'
import { FacilitiesPage } from './pages/FacilitiesPage'
import { CustomersPage } from './pages/CustomersPage'
import { CashierPage } from './pages/CashierPage'
import { AuditPage } from './pages/AuditPage'
import { ReportsPage } from './pages/ReportsPage'
import { EmployeesPage } from './pages/EmployeesPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { ProductsPage } from './pages/ProductsPage'
import { PaymentChannelsPage } from './pages/PaymentChannelsPage'
import { InventoryPage } from './pages/InventoryPage'
import { FacilityConfigurationPage } from './pages/FacilityConfigurationPage'
import './styles.css'

function ProtectedRoute() {
  const auth = useAuth(); const location = useLocation()
  if (auth.loading) return <div className="screen-loader"><Spin size="large" /></div>
  if (!auth.user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if (auth.user.mustChangePassword && location.pathname !== '/change-password') return <Navigate to="/change-password" replace />
  return <Outlet />
}

export default function App() {
  return <Routes><Route path="/login" element={<LoginPage />} /><Route element={<ProtectedRoute />}><Route path="change-password" element={<ChangePasswordPage />} /><Route element={<AppLayout />}><Route index element={<DashboardPage />} /><Route path="catalog/items" element={<ServiceItemsPage />} /><Route path="catalog/products" element={<ProductsPage />} /><Route path="catalog/prices" element={<PriceBooksPage />} /><Route path="facilities" element={<FacilitiesPage />} /><Route path="customers" element={<CustomersPage />} /><Route path="cashier" element={<CashierPage />} /><Route path="inventory" element={<InventoryPage />} /><Route path="reports" element={<ReportsPage />} /><Route path="audit" element={<AuditPage />} /><Route path="settings/facilities" element={<FacilityConfigurationPage />} /><Route path="settings/employees" element={<EmployeesPage />} /><Route path="settings/payment-channels" element={<PaymentChannelsPage />} /></Route></Route><Route path="*" element={<Navigate to="/" replace />} /></Routes>
}
