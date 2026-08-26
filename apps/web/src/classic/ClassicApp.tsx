import {
  AppstoreOutlined,
  AuditOutlined,
  BarChartOutlined,
  CalendarOutlined,
  ClockCircleOutlined,
  CreditCardOutlined,
  DatabaseOutlined,
  DollarOutlined,
  FileSearchOutlined,
  InboxOutlined,
  LineChartOutlined,
  LockOutlined,
  LogoutOutlined,
  MenuOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  MessageOutlined,
  PayCircleOutlined,
  ProfileOutlined,
  SearchOutlined,
  SettingOutlined,
  ShopOutlined,
  ShoppingCartOutlined,
  ShoppingOutlined,
  SolutionOutlined,
  TeamOutlined,
  TruckOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, ConfigProvider, Form, Input, Select, Spin } from 'antd'
import { lazy, Suspense, useMemo, useState, type ComponentType, type ReactNode } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Navigate, Outlet, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom'
import { apiRequest, ApiError, resetCsrfToken } from '../api/client'
import type { CurrentUser, CustomerSummary, OperationsReport, PageResult } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { BrandLogo } from '../components/BrandLogo'
import { Permission, type PermissionCode } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'
import { ClassicLegacyPage } from './ClassicLegacyPage'
import { classicDashboardReference, type ClassicDashboardReference } from './classicDashboardReference'
import { getClassicManifestModule, getClassicManifestPage, type ClassicManifestPage } from './classicManifest'
import './classic.css'

const ClassicCashierFacilitiesPage = lazy(() => import('./ClassicCashierFacilitiesPage').then((module) => ({ default: module.ClassicCashierFacilitiesPage })))
const ClassicCustomerDashboard = lazy(() => import('./ClassicCustomerPage').then((module) => ({ default: module.ClassicCustomerDashboard })))
const ClassicCustomerListPage = lazy(() => import('./ClassicCustomerPage').then((module) => ({ default: module.ClassicCustomerListPage })))
const ClassicCustomerCarePage = lazy(() => import('./ClassicCustomerCarePage').then((module) => ({ default: module.ClassicCustomerCarePage })))
const ClassicEmployeePage = lazy(() => import('./ClassicEmployeePage').then((module) => ({ default: module.ClassicEmployeePage })))
const SchedulingPage = lazy(() => import('../pages/SchedulingPage').then((module) => ({ default: module.SchedulingPage })))
const CashierPage = lazy(() => import('../pages/CashierPage').then((module) => ({ default: module.CashierPage })))
const InventoryPage = lazy(() => import('../pages/InventoryPage').then((module) => ({ default: module.InventoryPage })))
const SupplyChainPage = lazy(() => import('../pages/SupplyChainPage').then((module) => ({ default: module.SupplyChainPage })))
const ReportsPage = lazy(() => import('../pages/ReportsPage').then((module) => ({ default: module.ReportsPage })))
const AuditPage = lazy(() => import('../pages/AuditPage').then((module) => ({ default: module.AuditPage })))
const ServiceItemsPage = lazy(() => import('../pages/ServiceItemsPage').then((module) => ({ default: module.ServiceItemsPage })))
const ProductsPage = lazy(() => import('../pages/ProductsPage').then((module) => ({ default: module.ProductsPage })))
const PriceBooksPage = lazy(() => import('../pages/PriceBooksPage').then((module) => ({ default: module.PriceBooksPage })))
const FacilityConfigurationPage = lazy(() => import('../pages/FacilityConfigurationPage').then((module) => ({ default: module.FacilityConfigurationPage })))
const OrganizationSettingsPage = lazy(() => import('../pages/OrganizationSettingsPage').then((module) => ({ default: module.OrganizationSettingsPage })))
const PaymentChannelsPage = lazy(() => import('../pages/PaymentChannelsPage').then((module) => ({ default: module.PaymentChannelsPage })))
const ChangePasswordPage = lazy(() => import('../pages/ChangePasswordPage').then((module) => ({ default: module.ChangePasswordPage })))

type ClassicModuleKey = 'cashier' | 'customer' | 'promotion' | 'purchase' | 'sales' | 'inventory' | 'distribution' | 'employee' | 'finance' | 'reports' | 'decision' | 'sms'

interface ClassicAction {
  label: string
  path: string
  permission: PermissionCode
  icon: ReactNode
}

interface ClassicModuleDefinition {
  key: ClassicModuleKey
  label: string
  icon: ReactNode
  permission: PermissionCode
  managementTitle: string
  queryTitle: string
  chartTitle: string
  listTitle: string
  actions: ClassicAction[]
  queries: ClassicAction[]
}

const classicModules: ClassicModuleDefinition[] = [
  { key: 'cashier', label: '收银', icon: <PayCircleOutlined />, permission: Permission.CashierCheckout, managementTitle: '收银管理', queryTitle: '业务查询', chartTitle: '本月收银金额图表', listTitle: '最新服务单列表', actions: [
    { label: '设施接待', path: '/ui/new/cashier/facilities', permission: Permission.FacilityOperate, icon: <ClockCircleOutlined /> },
    { label: '服务录单与收银', path: '/ui/new/cashier/checkout', permission: Permission.CashierCheckout, icon: <PayCircleOutlined /> },
    { label: '预约与排班', path: '/ui/new/cashier/scheduling', permission: Permission.SchedulingOperate, icon: <CalendarOutlined /> },
  ], queries: [
    { label: '服务单查询', path: '/ui/new/cashier/checkout', permission: Permission.CashierCheckout, icon: <SearchOutlined /> },
    { label: '设施记录查询', path: '/ui/new/cashier/facilities', permission: Permission.FacilityOperate, icon: <SearchOutlined /> },
  ] },
  { key: 'customer', label: '顾客', icon: <TeamOutlined />, permission: Permission.CustomerRead, managementTitle: '顾客管理', queryTitle: '会员查询', chartTitle: '本月会员消费图表', listTitle: '最新顾客列表', actions: [
    { label: '顾客档案', path: '/ui/new/customer/list', permission: Permission.CustomerRead, icon: <TeamOutlined /> },
    { label: '会员与储值卡', path: '/ui/new/customer/list', permission: Permission.CustomerRead, icon: <CreditCardOutlined /> },
    { label: '服务记录', path: '/ui/new/customer/list', permission: Permission.CustomerRead, icon: <ProfileOutlined /> },
  ], queries: [
    { label: '顾客查询', path: '/ui/new/customer/list', permission: Permission.CustomerRead, icon: <SearchOutlined /> },
    { label: '会员余额查询', path: '/ui/new/customer/list', permission: Permission.CustomerRead, icon: <SearchOutlined /> },
  ] },
  { key: 'promotion', label: '促销', icon: <SolutionOutlined />, permission: Permission.CatalogRead, managementTitle: '营销与价格', queryTitle: '价格查询', chartTitle: '服务项目销售占比', listTitle: '当前价格方案', actions: [
    { label: '价格版本', path: '/ui/new/promotion/prices', permission: Permission.CatalogRead, icon: <DollarOutlined /> },
    { label: '服务项目', path: '/ui/new/promotion/services', permission: Permission.CatalogRead, icon: <ProfileOutlined /> },
    { label: '产品目录', path: '/ui/new/promotion/products', permission: Permission.CatalogRead, icon: <ShoppingOutlined /> },
  ], queries: [
    { label: '价格版本查询', path: '/ui/new/promotion/prices', permission: Permission.CatalogRead, icon: <SearchOutlined /> },
    { label: '服务项目查询', path: '/ui/new/promotion/services', permission: Permission.CatalogRead, icon: <SearchOutlined /> },
  ] },
  { key: 'purchase', label: '进货', icon: <ShoppingCartOutlined />, permission: Permission.SupplyChainRead, managementTitle: '进货管理', queryTitle: '报表查询', chartTitle: '本年进货金额图表', listTitle: '最新进货入库单列表', actions: [
    { label: '进货入库单', path: '/ui/new/purchase/manage', permission: Permission.SupplyChainRead, icon: <InboxOutlined /> },
    { label: '供应商管理', path: '/ui/new/purchase/manage', permission: Permission.SupplyChainRead, icon: <TeamOutlined /> },
    { label: '采购批次', path: '/ui/new/purchase/manage', permission: Permission.SupplyChainRead, icon: <DatabaseOutlined /> },
  ], queries: [
    { label: '进货单查询', path: '/ui/new/purchase/manage', permission: Permission.SupplyChainRead, icon: <SearchOutlined /> },
    { label: '商品订货统计', path: '/ui/new/reports/operations', permission: Permission.ReportRead, icon: <BarChartOutlined /> },
  ] },
  { key: 'sales', label: '销售', icon: <ShopOutlined />, permission: Permission.CashierCheckout, managementTitle: '销售管理', queryTitle: '销售查询', chartTitle: '本年销售金额图表', listTitle: '最新销售单列表', actions: [
    { label: '服务销售单', path: '/ui/new/sales/orders', permission: Permission.CashierCheckout, icon: <ProfileOutlined /> },
    { label: '商品销售单', path: '/ui/new/sales/orders', permission: Permission.CashierCheckout, icon: <ShoppingOutlined /> },
  ], queries: [
    { label: '销售单查询', path: '/ui/new/sales/orders', permission: Permission.CashierCheckout, icon: <SearchOutlined /> },
    { label: '退款记录查询', path: '/ui/new/sales/orders', permission: Permission.CashierCheckout, icon: <SearchOutlined /> },
  ] },
  { key: 'inventory', label: '库存', icon: <DatabaseOutlined />, permission: Permission.InventoryRead, managementTitle: '库存管理', queryTitle: '库存查询', chartTitle: '商品库存分类占比', listTitle: '最新库存变动列表', actions: [
    { label: '库存总览', path: '/ui/new/inventory/manage', permission: Permission.InventoryRead, icon: <DatabaseOutlined /> },
    { label: '盘点与调整', path: '/ui/new/inventory/manage', permission: Permission.InventoryRead, icon: <AuditOutlined /> },
  ], queries: [
    { label: '库存余额查询', path: '/ui/new/inventory/manage', permission: Permission.InventoryRead, icon: <SearchOutlined /> },
    { label: '库存流水查询', path: '/ui/new/inventory/manage', permission: Permission.InventoryRead, icon: <SearchOutlined /> },
  ] },
  { key: 'distribution', label: '配货', icon: <TruckOutlined />, permission: Permission.SupplyChainRead, managementTitle: '配货管理', queryTitle: '调拨查询', chartTitle: '本年门店调拨图表', listTitle: '最新调拨单列表', actions: [
    { label: '门店调拨', path: '/ui/new/distribution/manage', permission: Permission.SupplyChainRead, icon: <TruckOutlined /> },
    { label: '收货确认', path: '/ui/new/distribution/manage', permission: Permission.SupplyChainRead, icon: <InboxOutlined /> },
  ], queries: [
    { label: '调拨单查询', path: '/ui/new/distribution/manage', permission: Permission.SupplyChainRead, icon: <SearchOutlined /> },
  ] },
  { key: 'employee', label: '员工', icon: <UserOutlined />, permission: Permission.EmployeeManage, managementTitle: '员工管理', queryTitle: '员工查询', chartTitle: '本月员工业绩图表', listTitle: '员工与班次列表', actions: [
    { label: '员工与权限', path: '/ui/new/employee/manage', permission: Permission.EmployeeManage, icon: <UserOutlined /> },
    { label: '预约与排班', path: '/ui/new/employee/scheduling', permission: Permission.SchedulingOperate, icon: <CalendarOutlined /> },
  ], queries: [
    { label: '员工查询', path: '/ui/new/employee/manage', permission: Permission.EmployeeManage, icon: <SearchOutlined /> },
    { label: '排班查询', path: '/ui/new/employee/scheduling', permission: Permission.SchedulingOperate, icon: <SearchOutlined /> },
  ] },
  { key: 'finance', label: '财务', icon: <DollarOutlined />, permission: Permission.ReportRead, managementTitle: '财务管理', queryTitle: '财务查询', chartTitle: '本月资金构成图表', listTitle: '最新交班与对账列表', actions: [
    { label: '交班与复核', path: '/ui/new/finance/checkout', permission: Permission.CashierCheckout, icon: <AuditOutlined /> },
    { label: '经营对账', path: '/ui/new/finance/reports', permission: Permission.ReportRead, icon: <BarChartOutlined /> },
    { label: '支付渠道', path: '/ui/new/finance/channels', permission: Permission.PaymentChannelRead, icon: <CreditCardOutlined /> },
  ], queries: [
    { label: '收入明细查询', path: '/ui/new/finance/reports', permission: Permission.ReportRead, icon: <SearchOutlined /> },
    { label: '审计记录查询', path: '/ui/new/finance/audit', permission: Permission.AuditRead, icon: <SearchOutlined /> },
  ] },
  { key: 'reports', label: '报表', icon: <BarChartOutlined />, permission: Permission.ReportRead, managementTitle: '经营报表', queryTitle: '报表查询', chartTitle: '本月经营趋势图表', listTitle: '门店经营汇总', actions: [
    { label: '经营总览', path: '/ui/new/reports/operations', permission: Permission.ReportRead, icon: <BarChartOutlined /> },
    { label: '门店对账', path: '/ui/new/reports/operations', permission: Permission.ReportRead, icon: <AuditOutlined /> },
  ], queries: [
    { label: '收入报表', path: '/ui/new/reports/operations', permission: Permission.ReportRead, icon: <SearchOutlined /> },
    { label: '员工业绩报表', path: '/ui/new/reports/operations', permission: Permission.ReportRead, icon: <SearchOutlined /> },
  ] },
  { key: 'decision', label: '决策', icon: <LineChartOutlined />, permission: Permission.ReportRead, managementTitle: '经营决策', queryTitle: '分析入口', chartTitle: '经营趋势分析', listTitle: '关键经营指标', actions: [
    { label: '经营趋势', path: '/ui/new/decision/analysis', permission: Permission.ReportRead, icon: <LineChartOutlined /> },
    { label: '门店对比', path: '/ui/new/decision/analysis', permission: Permission.ReportRead, icon: <BarChartOutlined /> },
  ], queries: [
    { label: '服务项目分析', path: '/ui/new/decision/analysis', permission: Permission.ReportRead, icon: <SearchOutlined /> },
    { label: '员工提成分析', path: '/ui/new/decision/analysis', permission: Permission.ReportRead, icon: <SearchOutlined /> },
  ] },
  { key: 'sms', label: '短信', icon: <MessageOutlined />, permission: Permission.CustomerRead, managementTitle: '短信服务', queryTitle: '发送记录', chartTitle: '本月短信统计', listTitle: '短信通知记录', actions: [
    { label: '顾客通知对象', path: '/ui/new/customer/list', permission: Permission.CustomerRead, icon: <TeamOutlined /> },
  ], queries: [
    { label: '顾客联系方式查询', path: '/ui/new/customer/list', permission: Permission.CustomerRead, icon: <SearchOutlined /> },
  ] },
]

const featureTitles: Record<string, string> = {
  '/ui/new/cashier/facilities': '设施接待', '/ui/new/cashier/checkout': '服务录单与收银', '/ui/new/cashier/scheduling': '预约与排班',
  '/ui/new/customer/list': '顾客、会员与服务记录', '/ui/new/promotion/prices': '价格版本', '/ui/new/promotion/services': '服务项目', '/ui/new/promotion/products': '产品目录',
  '/ui/new/purchase/manage': '采购与入库', '/ui/new/sales/orders': '销售单与收银', '/ui/new/inventory/manage': '库存管理', '/ui/new/distribution/manage': '门店调拨',
  '/ui/new/employee/manage': '员工与权限', '/ui/new/employee/scheduling': '预约与排班', '/ui/new/finance/checkout': '交班与复核', '/ui/new/finance/reports': '财务报表',
  '/ui/new/finance/channels': '支付渠道', '/ui/new/finance/audit': '审计记录', '/ui/new/reports/operations': '经营报表', '/ui/new/decision/analysis': '经营决策',
  '/ui/new/settings/organization': '品牌与门店', '/ui/new/settings/facilities': '门店设施配置', '/ui/new/change-password': '修改密码',
}

function ClassicGuard() {
  const auth = useAuth()
  const location = useLocation()
  if (auth.loading) return <div className="classic-loader"><Spin size="large" /></div>
  if (!auth.user) return <Navigate to="/ui/new/login" replace state={{ from: location.pathname }} />
  if (auth.user.mustChangePassword && location.pathname !== '/ui/new/change-password') return <Navigate to="/ui/new/change-password" replace />
  return <Outlet />
}

function ClassicAuthorized({ permission, children }: { permission: PermissionCode; children: ReactNode }) {
  const { can } = useAuthorization()
  return can(permission) ? children : <Navigate to="/forbidden" replace />
}

function ClassicLogin() {
  const auth = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string>()
  if (auth.user) return <Navigate to="/ui/new/index" replace />
  const submit = async (values: { account: string; password: string }) => {
    setSubmitting(true); setError(undefined)
    try {
      await apiRequest<CurrentUser>('/api/v1/auth/login', { method: 'POST', body: JSON.stringify({ ...values, rememberMe: false }) })
      resetCsrfToken(); await auth.refresh()
      navigate((location.state as { from?: string } | undefined)?.from ?? '/ui/new/index', { replace: true })
    } catch (requestError) {
      setError(requestError instanceof ApiError ? requestError.message : '登录失败，请稍后重试')
    } finally { setSubmitting(false) }
  }
  return <main className="classic-login">
    <div className="classic-login-card">
      <div className="classic-login-brand"><BrandLogo /><div><strong>门店 ERP</strong><span>经典操作界面</span></div></div>
      <div className="classic-login-title">用户登录</div>
      {error && <Alert type="error" showIcon title={error} />}
      <Form layout="vertical" onFinish={submit} requiredMark={false}>
        <Form.Item name="account" label="登录账号" rules={[{ required: true, message: '请输入登录账号' }]}><Input prefix={<UserOutlined />} autoComplete="username" /></Form.Item>
        <Form.Item name="password" label="登录密码" rules={[{ required: true, message: '请输入登录密码' }]}><Input.Password prefix={<LockOutlined />} autoComplete="current-password" /></Form.Item>
        <Button type="primary" htmlType="submit" block loading={submitting}>登 录</Button>
      </Form>
      <p>与新版界面使用同一账号、权限和经营数据</p>
    </div>
  </main>
}

function ClassicLayout() {
  const auth = useAuth()
  const { can } = useAuthorization()
  const navigate = useNavigate()
  const location = useLocation()
  const [collapsed, setCollapsed] = useState(false)
  const legacyModuleKey = location.pathname.startsWith('/ui/new/legacy/') ? location.pathname.split('/')[4] : undefined
  const activeModule = classicModules.find((item) => legacyModuleKey ? item.key === legacyModuleKey : location.pathname === '/ui/new/index' ? item.key === 'cashier' : location.pathname.startsWith(`/ui/new/${item.key}`))?.key ?? 'cashier'
  const logout = async () => { await auth.logout(); navigate('/ui/new/login') }
  return <div className={`classic-shell ${collapsed ? 'is-collapsed' : ''}`}>
    <aside className="classic-sidebar">
      <button className="classic-brand" type="button" onClick={() => navigate('/ui/new/index')} aria-label="经典版首页"><BrandLogo />{!collapsed && <span>门店 ERP</span>}</button>
      <nav aria-label="经典版主导航">
        {classicModules.filter((item) => can(item.permission)).map((item) => <button type="button" key={item.key} className={activeModule === item.key ? 'active' : ''} onClick={() => navigate(`/ui/new/${item.key}`)} title={item.label}><span className="classic-nav-icon">{item.icon}</span>{!collapsed && <span>{item.label}</span>}</button>)}
      </nav>
    </aside>
    <section className="classic-main">
      <header className="classic-topbar">
        <button type="button" className="classic-collapse" onClick={() => setCollapsed((value) => !value)} aria-label={collapsed ? '展开菜单' : '收起菜单'}>{collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}</button>
        <div className="classic-store-context"><span>当前门店：</span><Select value={auth.store?.id} options={auth.user?.stores.map((store) => ({ value: store.id, label: `${store.name}（${store.code}）` }))} onChange={(id) => { const store = auth.user?.stores.find((item) => item.id === id); if (store) auth.setStore(store) }} /></div>
        <div className="classic-account-tools">
          <button type="button" onClick={() => navigate('/ui/new/index')}><AppstoreOutlined /> 工作台</button>
          {can(Permission.OrganizationManage) && <button type="button" onClick={() => navigate('/ui/new/settings/organization')}><SettingOutlined /> 设置</button>}
          <span>{auth.user?.displayName}</span>
          <button type="button" onClick={() => navigate('/ui/new/change-password')}><LockOutlined /> 修改密码</button>
          <button type="button" onClick={() => void logout()}><LogoutOutlined /> 退出</button>
        </div>
      </header>
      <main className="classic-content"><Outlet /></main>
    </section>
  </div>
}

function useClassicReport() {
  const auth = useAuth()
  const { can } = useAuthorization()
  const now = new Date()
  const toDate = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
  const fromDate = `${now.getFullYear()}-01-01`
  const report = useQuery({ queryKey: ['classic-operations-report', auth.store?.id, fromDate, toDate], enabled: Boolean(auth.store?.id && can(Permission.ReportRead)), queryFn: () => apiRequest<OperationsReport>(`/api/v1/reports/operations?storeId=${auth.store?.id}&fromDate=${fromDate}&toDate=${toDate}`) })
  const customers = useQuery({ queryKey: ['classic-customer-count', auth.store?.id], enabled: Boolean(auth.store?.id && can(Permission.CustomerRead)), queryFn: () => apiRequest<PageResult<CustomerSummary>>('/api/v1/customers/search', { method: 'POST', body: JSON.stringify({ storeId: auth.store?.id, query: '', page: 1, pageSize: 1 }) }) })
  return { report, customers }
}

const money = (minor = 0) => (minor / 100).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

function ClassicHome() {
  const { report, customers } = useClassicReport()
  const data = report.data
  const paymentTotal = data?.paymentMix.reduce((sum, item) => sum + Math.max(0, item.netAmountMinor), 0) ?? 0
  const paymentMix = (data?.paymentMix.length ? data.paymentMix : [{ methodName: '暂无数据', netAmountMinor: 1 }]).map((item) => ({ name: item.methodName, value: Math.max(1, item.netAmountMinor) }))
  const monthDays = new Date(new Date().getFullYear(), new Date().getMonth() + 1, 0).getDate()
  const dailyByDay = new Map((data?.daily ?? []).map((item) => [Number(item.date.slice(-2)), item.netRevenueMinor / 100]))
  const daily = Array.from({ length: monthDays }, (_, index) => ({ day: String(index + 1).padStart(2, '0'), amount: dailyByDay.get(index + 1) ?? 0 }))
  const cards = [
    { value: money(data?.summary.netRevenueMinor), label: '收入总额（单位：元）', tone: '#5d89cf', details: [`¥${money(data?.paymentMix.find((item) => item.methodCode === 'CASH')?.netAmountMinor)} 现金`, `¥${money(paymentTotal - (data?.paymentMix.find((item) => item.methodCode === 'CASH')?.netAmountMinor ?? 0))} 其他支付`] },
    { value: data?.summary.visitCount ?? 0, label: '消费人数（单位：人）', tone: '#7fc7aa', details: [`${data?.summary.settledOrderCount ?? 0} 已结单`, `${data?.summary.pendingReconciliationMinor ? 1 : 0} 待核对`] },
    { value: customers.data?.total ?? 0, label: '会员总数（单位：人）', tone: '#e4b24e', details: [`${customers.data?.total ?? 0} 顾客档案`, `${data?.summary.visitCount ?? 0} 本期到店`] },
    { value: money(data?.summary.storedValueNetMinor), label: '储值余额（单位：元）', tone: '#8f5eb5', details: [`¥${money(data?.summary.storedValuePrincipalMinor)} 本金`, `¥${money(data?.summary.storedValueBonusMinor)} 赠送`] },
  ]
  return <div className="classic-home">
    <section className="classic-metrics">{cards.map((card) => <article key={card.label}><strong>{card.value}</strong><span>{card.label}</span><i style={{ '--metric-tone': card.tone } as React.CSSProperties} /><div>{card.details.map((detail) => <small key={detail}>{detail}</small>)}</div></article>)}</section>
    <section className="classic-home-grid">
      <div className="classic-panel"><header><strong>消费类型占比</strong><Select size="small" value="本期" options={[{ value: '本期', label: '本期' }]} /></header><div className="classic-chart-wrap"><ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={paymentMix} dataKey="value" nameKey="name" innerRadius={56} outerRadius={84} paddingAngle={2} isAnimationActive={false}>{paymentMix.map((entry, index) => <Cell key={entry.name} fill={['#79aee7', '#8fce80', '#efb15c', '#9b73c9'][index % 4]} />)}</Pie><Tooltip formatter={(value) => `¥${money(Number(value))}`} /></PieChart></ResponsiveContainer></div><div className="classic-legend">{paymentMix.map((item, index) => <span key={item.name}><i style={{ backgroundColor: ['#79aee7', '#8fce80', '#efb15c', '#9b73c9'][index % 4] }} />{item.name}</span>)}</div></div>
      <div className="classic-panel"><header><strong>营业走势图表</strong><Select size="small" value="本月" options={[{ value: '本月', label: '本月' }]} /></header><div className="classic-chart-wrap"><ResponsiveContainer width="100%" height="100%"><LineChart data={daily}><CartesianGrid stroke="#edf0f2" vertical={false} /><XAxis dataKey="day" tick={{ fontSize: 10 }} /><YAxis tick={{ fontSize: 10 }} width={48} /><Tooltip formatter={(value) => `¥${money(Number(value) * 100)}`} /><Line type="monotone" dataKey="amount" stroke="#71a9df" strokeWidth={2} dot={{ r: 3, fill: '#71a9df' }} isAnimationActive={false} /></LineChart></ResponsiveContainer></div></div>
    </section>
  </div>
}

function ClassicModuleDashboard({ module }: { module: ClassicModuleDefinition }) {
  const navigate = useNavigate()
  const { can } = useAuthorization()
  const { report } = useClassicReport()
  const inventoryModule = getClassicManifestModule(module.key)
  const monthData = useMemo(() => Array.from({ length: 12 }, (_, index) => {
    const month = String(index + 1).padStart(2, '0')
    const amount = report.data?.daily.filter((item) => item.date.slice(5, 7) === month).reduce((sum, item) => sum + item.netRevenueMinor, 0) ?? 0
    return { month: `${index + 1}月`, amount: amount / 100 }
  }), [report.data])
  const reference: ClassicDashboardReference = classicDashboardReference[module.key]
  const categoryData = reference.chartDataMode === 'operations' && report.data?.services.length
    ? report.data.services.slice(0, 5).map((item) => ({ name: item.itemName, value: Math.max(1, item.revenueMinor), actual: item.revenueMinor }))
    : [{ name: '全部分类', value: 1, actual: 0 }]
  const pageAction = (page: ClassicManifestPage): ClassicAction => ({
    label: page.label,
    path: `/ui/new/legacy/${module.key}/${page.id}`,
    permission: module.permission,
    icon: page.kind === 'query' ? <SearchOutlined /> : <FileSearchOutlined />,
  })
  const inventoryManagement = inventoryModule?.pages.slice(0, reference.managementActionCount).map(pageAction) ?? []
  const inventoryQueries = inventoryModule?.pages.slice(reference.managementActionCount).map(pageAction) ?? []
  const managementActions = inventoryManagement.length ? inventoryManagement : module.actions.filter((item) => can(item.permission))
  const queryActions = inventoryQueries.length ? inventoryQueries : module.queries.filter((item) => can(item.permission))
  const dashboardHeaders = reference.latestHeaders ?? []
  const chartMonthData = reference.chartDataMode === 'operations' ? monthData : monthData.map((item) => ({ ...item, amount: 0 }))
  if (reference.layout === 'cashier') {
    return <ClassicCashierDashboard actions={managementActions} onNavigate={navigate} />
  }
  if (module.key === 'customer') {
    return <ClassicCustomerDashboard />
  }
  const charts = reference.layout === 'analysis'
    ? <ClassicAnalysisCharts titles={reference.chartTitles} categoryData={categoryData} report={report.data} />
    : reference.layout === 'single-chart'
      ? <ClassicSingleChart title={reference.chartTitles[0]} monthData={chartMonthData} />
      : <ClassicStandardCharts titles={reference.chartTitles} categoryData={categoryData} monthData={chartMonthData} />
  return <div className="classic-module-dashboard">
    <div className={`classic-dashboard-left classic-layout-${reference.layout}`}>
      {charts}
      {reference.layout !== 'analysis' && <ClassicLatestTable title={reference.latestTitle ?? module.listTitle} headers={dashboardHeaders} onQuery={() => queryActions[0] && navigate(queryActions[0].path)} />}
    </div>
    <aside className="classic-quick-column">
      <ClassicQuickGroup title={reference.managementTitle} actions={managementActions} onNavigate={navigate} more={managementActions.length > 5} moreLabel={reference.layout === 'analysis' ? '查看更多报表' : '查看更多功能'} />
      {reference.queryTitle && <ClassicQuickGroup title={reference.queryTitle} actions={queryActions} onNavigate={navigate} more={queryActions.length > 5} moreLabel="查看更多报表" />}
    </aside>
  </div>
}

function ClassicChartPanel({ title, children, className = '', action }: { title: string; children: ReactNode; className?: string; action?: ReactNode }) {
  return <section className={`classic-panel classic-chart-panel ${className}`}><header><strong>{title}</strong>{action ?? <MenuOutlined />}</header><div className="classic-chart-wrap">{children}</div></section>
}

function ClassicStandardCharts({ titles, categoryData, monthData }: { titles: readonly string[]; categoryData: { name: string; value: number; actual: number }[]; monthData: { month: string; amount: number }[] }) {
  const hasMonthData = monthData.some((item) => item.amount > 0)
  return <section className="classic-module-charts">
    <ClassicChartPanel title={titles[0]}><div className="classic-donut-layout"><ResponsiveContainer width="72%" height="100%"><PieChart><Pie data={categoryData} dataKey="value" nameKey="name" innerRadius={56} outerRadius={84} startAngle={90} endAngle={-180} isAnimationActive={false}>{categoryData.map((entry, index) => <Cell key={entry.name} fill={['#78ace2', '#83c988', '#efb25a', '#9c74cb', '#74c5c8'][index % 5]} />)}</Pie><Tooltip formatter={(_, _name, item) => Number(item.payload.actual ?? 0).toLocaleString('zh-CN')} /></PieChart></ResponsiveContainer><div className="classic-donut-legend"><i />全部分类</div></div></ClassicChartPanel>
    <ClassicChartPanel title={titles[1]}><ResponsiveContainer width="100%" height="100%"><BarChart data={monthData}><CartesianGrid stroke="#e6e8eb" vertical={false} /><XAxis dataKey="month" tick={{ fontSize: 10 }} /><YAxis tick={{ fontSize: 10 }} width={48} domain={hasMonthData ? ['auto', 'auto'] : [0, 1]} ticks={hasMonthData ? undefined : [0]} /><Tooltip formatter={(value) => `¥${money(Number(value) * 100)}`} /><Bar dataKey="amount" fill="#75a9df" maxBarSize={24} isAnimationActive={false} /></BarChart></ResponsiveContainer></ClassicChartPanel>
  </section>
}

function ClassicSingleChart({ title, monthData }: { title: string; monthData: { month: string; amount: number }[] }) {
  return <section className="classic-module-single-chart"><ClassicChartPanel title={title}><ResponsiveContainer width="100%" height="100%"><BarChart data={monthData}><CartesianGrid stroke="#e6e8eb" vertical={false} /><XAxis dataKey="month" tick={{ fontSize: 10 }} /><YAxis tick={{ fontSize: 10 }} width={48} /><Tooltip formatter={(value) => `¥${money(Number(value) * 100)}`} /><Bar dataKey="amount" fill="#75a9df" maxBarSize={30} isAnimationActive={false} /></BarChart></ResponsiveContainer></ClassicChartPanel></section>
}

function ClassicAnalysisCharts({ titles, categoryData, report }: { titles: readonly string[]; categoryData: { name: string; value: number; actual: number }[]; report?: OperationsReport }) {
  const daily = report?.daily.map((item) => ({ day: item.date.slice(-2), amount: item.netRevenueMinor / 100 })) ?? []
  const serviceData = report?.services.slice(0, 12).map((item) => ({ name: item.itemName, amount: item.revenueMinor / 100 })) ?? []
  const trendData = daily.length ? daily.map((item) => ({ label: item.day, amount: item.amount })) : Array.from({ length: 31 }, (_, index) => ({ label: String(index + 1).padStart(2, '0'), amount: 0 }))
  const hasCategoryData = categoryData.some((item) => item.actual > 0)
  const periodAction = <Select size="small" value="本日" options={[{ value: '本日', label: '本日' }]} />
  return <section className="classic-analysis-grid">
    <ClassicChartPanel title={titles[0]} action={periodAction}><div className="classic-analysis-chart-shell"><MenuOutlined className="classic-plot-menu" />{hasCategoryData && <ResponsiveContainer width="100%" height="72%"><PieChart><Pie data={categoryData} dataKey="value" nameKey="name" innerRadius={50} outerRadius={76} isAnimationActive={false}>{categoryData.map((entry, index) => <Cell key={entry.name} fill={['#78ace2', '#83c988', '#efb25a', '#9c74cb'][index % 4]} />)}</Pie><Tooltip formatter={(_, _name, item) => `¥${money(Number(item.payload.actual ?? 0))}`} /></PieChart></ResponsiveContainer>}<div className="classic-analysis-legend">{['顾客储值', '次卡销售', '项目消费', '产品销售'].map((label, index) => <span key={label}><i style={{ background: ['#78ace2', '#83c988', '#efb25a', '#7a77d2'][index] }} />{label}</span>)}</div></div></ClassicChartPanel>
    <ClassicChartPanel title={titles[1]} action={periodAction}><div className="classic-analysis-chart-shell"><MenuOutlined className="classic-plot-menu" /><ResponsiveContainer width="100%" height="100%"><BarChart data={serviceData}><CartesianGrid stroke="#e6e8eb" vertical={false} /><XAxis dataKey="name" tick={{ fontSize: 9 }} interval={0} angle={-38} textAnchor="end" height={72} /><YAxis tick={{ fontSize: 10 }} width={48} /><Tooltip formatter={(value) => `¥${Number(value).toLocaleString('zh-CN')}`} /><Bar dataKey="amount" fill="#75a9df" maxBarSize={24} isAnimationActive={false} /></BarChart></ResponsiveContainer></div></ClassicChartPanel>
    <ClassicChartPanel title={titles[2]} className="classic-analysis-trend" action={<Select size="small" value="本月" options={[{ value: '本月', label: '本月' }]} />}><div className="classic-analysis-chart-shell"><MenuOutlined className="classic-plot-menu" /><ResponsiveContainer width="100%" height="100%"><LineChart data={trendData}><CartesianGrid stroke="#e6e8eb" vertical={false} /><XAxis dataKey="label" tick={{ fontSize: 10 }} /><YAxis tick={{ fontSize: 10 }} width={48} /><Tooltip formatter={(value) => `¥${Number(value).toLocaleString('zh-CN')}`} /><Line type="monotone" dataKey="amount" stroke="#75a9df" dot={{ r: 3 }} strokeWidth={2} isAnimationActive={false} /></LineChart></ResponsiveContainer></div></ClassicChartPanel>
  </section>
}

function ClassicLatestTable({ title, headers, onQuery }: { title: string; headers: readonly string[]; onQuery: () => void }) {
  return <section className="classic-panel classic-latest"><header><strong>{title}</strong><button type="button" onClick={onQuery}><SearchOutlined /> 查询</button></header><div className="classic-table-scroll"><table><thead><tr>{headers.map((header) => <th key={header}>{header}</th>)}</tr></thead><tbody>{Array.from({ length: 6 }, (_, index) => <tr key={index}>{headers.map((header) => <td key={header}>&nbsp;</td>)}</tr>)}</tbody></table></div></section>
}

function ClassicCashierDashboard({ actions, onNavigate }: { actions: ClassicAction[]; onNavigate: (path: string) => void }) {
  const visibleActions = actions.slice(0, 10)
  return <section className="classic-cashier-dashboard">
    <header><strong>前台收银</strong><span>当前门店设施与接待状态</span></header>
    <div className="classic-cashier-stage"><Button type="primary" icon={<ClockCircleOutlined />} onClick={() => onNavigate('/ui/new/cashier/facilities')}>进入设施接待操作台</Button></div>
    <footer>{visibleActions.map((action) => <button type="button" key={`${action.path}-${action.label}`} onClick={() => onNavigate(action.path)}><span>{action.icon}</span><b>{action.label.slice(0, 2)}</b><small>{action.label.slice(2) || '业务'}</small></button>)}</footer>
  </section>
}

function ClassicQuickGroup({ title, actions, onNavigate, more, moreLabel = '查看更多' }: { title: string; actions: ClassicAction[]; onNavigate: (path: string) => void; more?: boolean; moreLabel?: string }) {
  const [expanded, setExpanded] = useState(false)
  const visibleActions = more && !expanded ? actions.slice(0, 5) : actions
  return <section className="classic-quick-group"><h2>{title}</h2>{visibleActions.map((action) => <button type="button" key={`${action.path}-${action.label}`} onClick={() => onNavigate(action.path)}><span>{action.icon}</span>{action.label}</button>)}{more && <button type="button" className="classic-more" onClick={() => setExpanded((value) => !value)}>{expanded ? '收起' : moreLabel} <span>{expanded ? '«' : '»'}</span></button>}</section>
}

function ClassicFeatureFrame({ children, title }: { children: ReactNode; title?: string }) {
  const navigate = useNavigate()
  const location = useLocation()
  const legacyModuleKey = location.pathname.startsWith('/ui/new/legacy/') ? location.pathname.split('/')[4] : undefined
  const activeModule = classicModules.find((item) => legacyModuleKey ? item.key === legacyModuleKey : location.pathname.startsWith(`/ui/new/${item.key}`))
  return <section className="classic-feature">
    <header className="classic-feature-header"><div><button type="button" onClick={() => navigate(activeModule ? `/ui/new/${activeModule.key}` : '/ui/new/index')}>返回{activeModule?.label ?? '首页'}</button><span>/</span><strong>{title ?? featureTitles[location.pathname] ?? activeModule?.label ?? '业务管理'}</strong></div><small>经典操作界面 · 与新版数据实时同步</small></header>
    <div className="classic-feature-body">{children}</div>
  </section>
}

function ClassicPageRoute({ permission, component: Component }: { permission: PermissionCode; component: ComponentType }) {
  return <ClassicAuthorized permission={permission}><ClassicFeatureFrame><Component /></ClassicFeatureFrame></ClassicAuthorized>
}

function ClassicModuleRoute({ moduleKey }: { moduleKey: ClassicModuleKey }) {
  const module = classicModules.find((item) => item.key === moduleKey)!
  return <ClassicAuthorized permission={module.permission}><ClassicModuleDashboard module={module} /></ClassicAuthorized>
}

function ClassicLegacyRoute() {
  const { moduleKey = '', pageId = '' } = useParams()
  const moduleDefinition = classicModules.find((item) => item.key === moduleKey)
  const manifestModule = getClassicManifestModule(moduleKey)
  const page = getClassicManifestPage(moduleKey, pageId)
  if (!moduleDefinition || !manifestModule || !page) return <Navigate to="/ui/new/index" replace />
  if (moduleKey === 'employee' && pageId === 'employee-001') {
    return <ClassicAuthorized permission={Permission.EmployeeManage}><ClassicEmployeePage /></ClassicAuthorized>
  }
  if (moduleKey === 'customer' && pageId === 'customer-005') {
    return <ClassicAuthorized permission={Permission.ServiceRecordManage}><ClassicCustomerCarePage /></ClassicAuthorized>
  }
  return <ClassicAuthorized permission={moduleDefinition.permission}><ClassicLegacyPage module={manifestModule} page={page} /></ClassicAuthorized>
}

export function ClassicApp() {
  return <ConfigProvider theme={{ token: { colorPrimary: '#449be6', borderRadius: 2, colorText: '#333333', fontFamily: 'Arial, "Microsoft YaHei", sans-serif' }, components: { Button: { controlHeight: 30 }, Input: { controlHeight: 30 }, Select: { controlHeight: 30 }, Card: { borderRadiusLG: 2 } } }}>
    <Suspense fallback={<div className="classic-loader"><Spin size="large" /></div>}>
      <Routes>
        <Route path="login" element={<ClassicLogin />} />
        <Route element={<ClassicGuard />}>
          <Route element={<ClassicLayout />}>
            <Route path="index" element={<ClassicAuthorized permission={Permission.DashboardRead}><ClassicHome /></ClassicAuthorized>} />
            {classicModules.map((item) => <Route key={item.key} path={item.key} element={<ClassicModuleRoute moduleKey={item.key} />} />)}
            <Route path="cashier/facilities" element={<ClassicPageRoute permission={Permission.FacilityOperate} component={ClassicCashierFacilitiesPage} />} />
            <Route path="cashier/checkout" element={<ClassicPageRoute permission={Permission.CashierCheckout} component={CashierPage} />} />
            <Route path="cashier/scheduling" element={<ClassicPageRoute permission={Permission.SchedulingOperate} component={SchedulingPage} />} />
            <Route path="customer/list" element={<ClassicAuthorized permission={Permission.CustomerRead}><ClassicCustomerListPage /></ClassicAuthorized>} />
            <Route path="promotion/prices" element={<ClassicPageRoute permission={Permission.CatalogRead} component={PriceBooksPage} />} />
            <Route path="promotion/services" element={<ClassicPageRoute permission={Permission.CatalogRead} component={ServiceItemsPage} />} />
            <Route path="promotion/products" element={<ClassicPageRoute permission={Permission.CatalogRead} component={ProductsPage} />} />
            <Route path="purchase/manage" element={<ClassicPageRoute permission={Permission.SupplyChainRead} component={SupplyChainPage} />} />
            <Route path="sales/orders" element={<ClassicPageRoute permission={Permission.CashierCheckout} component={CashierPage} />} />
            <Route path="inventory/manage" element={<ClassicPageRoute permission={Permission.InventoryRead} component={InventoryPage} />} />
            <Route path="distribution/manage" element={<ClassicPageRoute permission={Permission.SupplyChainRead} component={SupplyChainPage} />} />
            <Route path="employee/manage" element={<ClassicAuthorized permission={Permission.EmployeeManage}><ClassicEmployeePage /></ClassicAuthorized>} />
            <Route path="employee/scheduling" element={<ClassicPageRoute permission={Permission.SchedulingOperate} component={SchedulingPage} />} />
            <Route path="finance/checkout" element={<ClassicPageRoute permission={Permission.CashierCheckout} component={CashierPage} />} />
            <Route path="finance/reports" element={<ClassicPageRoute permission={Permission.ReportRead} component={ReportsPage} />} />
            <Route path="finance/channels" element={<ClassicPageRoute permission={Permission.PaymentChannelRead} component={PaymentChannelsPage} />} />
            <Route path="finance/audit" element={<ClassicPageRoute permission={Permission.AuditRead} component={AuditPage} />} />
            <Route path="reports/operations" element={<ClassicPageRoute permission={Permission.ReportRead} component={ReportsPage} />} />
            <Route path="decision/analysis" element={<ClassicPageRoute permission={Permission.ReportRead} component={ReportsPage} />} />
            <Route path="legacy/:moduleKey/:pageId" element={<ClassicLegacyRoute />} />
            <Route path="settings/organization" element={<ClassicPageRoute permission={Permission.OrganizationManage} component={OrganizationSettingsPage} />} />
            <Route path="settings/facilities" element={<ClassicPageRoute permission={Permission.FacilityConfigure} component={FacilityConfigurationPage} />} />
            <Route path="change-password" element={<ClassicFeatureFrame><ChangePasswordPage /></ClassicFeatureFrame>} />
          </Route>
        </Route>
        <Route index element={<Navigate to="index" replace />} />
        <Route path="*" element={<Navigate to="/ui/new/index" replace />} />
      </Routes>
    </Suspense>
  </ConfigProvider>
}
