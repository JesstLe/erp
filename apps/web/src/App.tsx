import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { Spin } from 'antd'
import { useAuth } from './auth/useAuth'
import { AppLayout } from './layout/AppLayout'
import { LoginPage } from './pages/LoginPage'
import { DashboardPage } from './pages/DashboardPage'
import { ServiceItemsPage } from './pages/ServiceItemsPage'
import { PriceBooksPage } from './pages/PriceBooksPage'
import { ComingSoonPage } from './pages/ComingSoonPage'
import { FacilitiesPage } from './pages/FacilitiesPage'
import './styles.css'

function ProtectedRoute() {
  const auth = useAuth(); const location = useLocation()
  if (auth.loading) return <div className="screen-loader"><Spin size="large" /></div>
  if (!auth.user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet />
}

export default function App() {
  return <Routes><Route path="/login" element={<LoginPage />} /><Route element={<ProtectedRoute />}><Route element={<AppLayout />}><Route index element={<DashboardPage />} /><Route path="catalog/items" element={<ServiceItemsPage />} /><Route path="catalog/prices" element={<PriceBooksPage />} /><Route path="facilities" element={<FacilitiesPage />} /><Route path="customers" element={<ComingSoonPage title="顾客与会员" />} /><Route path="cashier" element={<ComingSoonPage title="服务录单与收银" />} /><Route path="reports" element={<ComingSoonPage title="经营报表" />} /><Route path="audit" element={<ComingSoonPage title="审计记录" />} /></Route></Route><Route path="*" element={<Navigate to="/" replace />} /></Routes>
}
