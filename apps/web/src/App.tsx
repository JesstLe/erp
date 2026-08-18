import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { Spin } from 'antd'
import { lazy, Suspense } from 'react'
import { useAuth } from './auth/useAuth'
import { AppLayout } from './layout/AppLayout'
import './styles.css'

const LoginPage = lazy(() => import('./pages/LoginPage').then((module) => ({ default: module.LoginPage })))
const DashboardPage = lazy(() => import('./pages/DashboardPage').then((module) => ({ default: module.DashboardPage })))
const ServiceItemsPage = lazy(() => import('./pages/ServiceItemsPage').then((module) => ({ default: module.ServiceItemsPage })))
const PriceBooksPage = lazy(() => import('./pages/PriceBooksPage').then((module) => ({ default: module.PriceBooksPage })))
const FacilitiesPage = lazy(() => import('./pages/FacilitiesPage').then((module) => ({ default: module.FacilitiesPage })))
const CustomersPage = lazy(() => import('./pages/CustomersPage').then((module) => ({ default: module.CustomersPage })))
const CashierPage = lazy(() => import('./pages/CashierPage').then((module) => ({ default: module.CashierPage })))
const AuditPage = lazy(() => import('./pages/AuditPage').then((module) => ({ default: module.AuditPage })))
const ReportsPage = lazy(() => import('./pages/ReportsPage').then((module) => ({ default: module.ReportsPage })))
const EmployeesPage = lazy(() => import('./pages/EmployeesPage').then((module) => ({ default: module.EmployeesPage })))
const ChangePasswordPage = lazy(() => import('./pages/ChangePasswordPage').then((module) => ({ default: module.ChangePasswordPage })))
const ProductsPage = lazy(() => import('./pages/ProductsPage').then((module) => ({ default: module.ProductsPage })))
const PaymentChannelsPage = lazy(() => import('./pages/PaymentChannelsPage').then((module) => ({ default: module.PaymentChannelsPage })))
const InventoryPage = lazy(() => import('./pages/InventoryPage').then((module) => ({ default: module.InventoryPage })))
const SupplyChainPage = lazy(() => import('./pages/SupplyChainPage').then((module) => ({ default: module.SupplyChainPage })))
const FacilityConfigurationPage = lazy(() => import('./pages/FacilityConfigurationPage').then((module) => ({ default: module.FacilityConfigurationPage })))
const OrganizationSettingsPage = lazy(() => import('./pages/OrganizationSettingsPage').then((module) => ({ default: module.OrganizationSettingsPage })))
const SchedulingPage = lazy(() => import('./pages/SchedulingPage').then((module) => ({ default: module.SchedulingPage })))

function ProtectedRoute() {
  const auth = useAuth(); const location = useLocation()
  if (auth.loading) return <div className="screen-loader"><Spin size="large" /></div>
  if (!auth.user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if (auth.user.mustChangePassword && location.pathname !== '/change-password') return <Navigate to="/change-password" replace />
  return <Outlet />
}

export default function App() {
  return <Suspense fallback={<div className="screen-loader"><Spin size="large" /></div>}><Routes><Route path="/login" element={<LoginPage />} /><Route element={<ProtectedRoute />}><Route path="change-password" element={<ChangePasswordPage />} /><Route element={<AppLayout />}><Route index element={<DashboardPage />} /><Route path="catalog/items" element={<ServiceItemsPage />} /><Route path="catalog/products" element={<ProductsPage />} /><Route path="catalog/prices" element={<PriceBooksPage />} /><Route path="facilities" element={<FacilitiesPage />} /><Route path="scheduling" element={<SchedulingPage />} /><Route path="customers" element={<CustomersPage />} /><Route path="cashier" element={<CashierPage />} /><Route path="inventory" element={<InventoryPage />} /><Route path="supply-chain" element={<SupplyChainPage />} /><Route path="reports" element={<ReportsPage />} /><Route path="audit" element={<AuditPage />} /><Route path="settings/organization" element={<OrganizationSettingsPage />} /><Route path="settings/facilities" element={<FacilityConfigurationPage />} /><Route path="settings/employees" element={<EmployeesPage />} /><Route path="settings/payment-channels" element={<PaymentChannelsPage />} /></Route></Route><Route path="*" element={<Navigate to="/" replace />} /></Routes></Suspense>
}
