import { AppstoreOutlined, AuditOutlined, BankOutlined, BarChartOutlined, BellOutlined, CalendarOutlined, ClockCircleOutlined, ControlOutlined, CreditCardOutlined, InboxOutlined, LockOutlined, LogoutOutlined, MenuFoldOutlined, MenuUnfoldOutlined, ProfileOutlined, QuestionCircleOutlined, SafetyCertificateOutlined, SettingOutlined, ShoppingOutlined, TagsOutlined, TeamOutlined, TruckOutlined } from '@ant-design/icons'
import { Avatar, Badge, Button, Dropdown, Empty, Layout, Menu, Popover, Select, Tag, Tooltip, Typography, type MenuProps } from 'antd'
import { useState, type ReactNode } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../auth/useAuth'
import { apiRequest } from '../api/client'
import type { NotificationInbox } from '../api/types'
import { Permission, type PermissionCode } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'
import { BrandLogo } from '../components/BrandLogo'
import { defaultNavigationLabels, resolveNavigationLabel } from '../navigationLabels'
import type { NavigationLabels } from '../api/types'

const { Header, Sider, Content } = Layout
const appVersion = import.meta.env.VITE_APP_VERSION?.trim() || '0.0.0-local'
const deploymentEnvironment = import.meta.env.VITE_APP_ENVIRONMENT?.trim() || 'Local'
const environmentLabel = deploymentEnvironment.toLowerCase() === 'production' ? '生产' : deploymentEnvironment.toLowerCase() === 'staging' ? '预发布' : '本地开发'
interface AuthorizedMenuItem { key: string; icon: ReactNode; label: string; permission: PermissionCode }
const operationMenuItems: AuthorizedMenuItem[] = [
  { key: '/', icon: <AppstoreOutlined />, label: defaultNavigationLabels['/'], permission: Permission.DashboardRead },
  { key: '/facilities', icon: <ClockCircleOutlined />, label: defaultNavigationLabels['/facilities'], permission: Permission.FacilityOperate },
  { key: '/scheduling', icon: <CalendarOutlined />, label: defaultNavigationLabels['/scheduling'], permission: Permission.SchedulingOperate },
  { key: '/customers', icon: <TeamOutlined />, label: defaultNavigationLabels['/customers'], permission: Permission.CustomerRead },
  { key: '/inventory', icon: <InboxOutlined />, label: defaultNavigationLabels['/inventory'], permission: Permission.InventoryRead },
  { key: '/supply-chain', icon: <TruckOutlined />, label: defaultNavigationLabels['/supply-chain'], permission: Permission.SupplyChainRead },
]
const managementMenuItems: AuthorizedMenuItem[] = [
  { key: '/catalog/items', icon: <ProfileOutlined />, label: defaultNavigationLabels['/catalog/items'], permission: Permission.CatalogRead },
  { key: '/catalog/products', icon: <ShoppingOutlined />, label: defaultNavigationLabels['/catalog/products'], permission: Permission.CatalogRead },
  { key: '/catalog/prices', icon: <TagsOutlined />, label: defaultNavigationLabels['/catalog/prices'], permission: Permission.CatalogRead },
  { key: '/reports', icon: <BarChartOutlined />, label: defaultNavigationLabels['/reports'], permission: Permission.ReportRead },
  { key: '/audit', icon: <AuditOutlined />, label: defaultNavigationLabels['/audit'], permission: Permission.AuditRead },
]

export function AppLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const auth = useAuth(); const navigate = useNavigate(); const location = useLocation()
  const { can } = useAuthorization()
  const navigationLabels = useQuery({ queryKey: ['navigation-labels'], queryFn: () => apiRequest<NavigationLabels>('/api/v1/navigation/labels'), staleTime: 60_000 })
  const named = (item: Omit<AuthorizedMenuItem, 'permission'>) => ({ ...item, label: resolveNavigationLabel(item.key, navigationLabels.data?.labels) })
  const visibleOperationItems = operationMenuItems.filter((item) => can(item.permission))
  const visibleManagementItems = managementMenuItems.filter((item) => can(item.permission))
  const visibleBaseItems: NonNullable<MenuProps['items']> = [
    ...visibleOperationItems.map(({ permission: _, ...item }) => named(item)),
    ...(visibleOperationItems.length && visibleManagementItems.length ? [{ type: 'divider' as const }] : []),
    ...visibleManagementItems.map(({ permission: _, ...item }) => named(item)),
  ]
  const notifications = useQuery({ queryKey: ['notifications', auth.store?.id], enabled: Boolean(auth.store?.id), queryFn: () => apiRequest<NotificationInbox>(`/api/v1/notifications?storeId=${auth.store?.id}`), refetchInterval: 30_000 })
  const menuItems: MenuProps['items'] = [
    ...visibleBaseItems,
    ...(can(Permission.FacilityConfigure) ? [named({ key: '/settings/facilities', icon: <ControlOutlined />, label: defaultNavigationLabels['/settings/facilities'] })] : []),
    ...(can(Permission.OrganizationManage) ? [named({ key: '/settings/organization', icon: <BankOutlined />, label: defaultNavigationLabels['/settings/organization'] })] : []),
    ...(can(Permission.EmployeeManage) ? [named({ key: '/settings/employees', icon: <SafetyCertificateOutlined />, label: defaultNavigationLabels['/settings/employees'] })] : []),
    ...(can(Permission.PaymentChannelManage) ? [named({ key: '/settings/payment-channels', icon: <CreditCardOutlined />, label: defaultNavigationLabels['/settings/payment-channels'] })] : []),
  ]
  const settingsItems: MenuProps['items'] = [
    ...(can(Permission.FacilityConfigure) ? [{ key: '/settings/facilities', icon: <ControlOutlined />, label: '门店设施配置' }] : []),
    ...(can(Permission.OrganizationManage) ? [{ key: '/settings/organization', icon: <BankOutlined />, label: '品牌与门店' }] : []),
    ...(can(Permission.EmployeeManage) ? [{ key: '/settings/employees', icon: <SafetyCertificateOutlined />, label: '员工与权限' }] : []),
    ...(can(Permission.PaymentChannelManage) ? [{ key: '/settings/payment-channels', icon: <CreditCardOutlined />, label: '支付渠道配置' }] : []),
  ]
  const accountItems: MenuProps['items'] = [
    { key: 'account', disabled: true, label: <div className="account-menu-summary"><strong>{auth.user?.displayName}</strong><span>{auth.user?.roles.join(' / ')}</span></div> },
    { type: 'divider' },
    { key: 'password', icon: <LockOutlined />, label: '修改密码' },
    { key: 'logout', icon: <LogoutOutlined />, label: '退出登录', danger: true },
  ]
  const logout = async () => {
    await auth.logout()
    navigate('/login')
  }
  return <Layout className="app-shell">
    <Sider width={232} collapsedWidth={76} breakpoint="lg" collapsed={collapsed} onBreakpoint={setCollapsed} className="app-sider">
      <div className="app-logo"><span className="app-logo-mark"><BrandLogo /></span>{!collapsed && <strong>门店 ERP</strong>}</div>
      <nav className="sider-menu-scroll" aria-label="主导航">
        <Menu theme="light" mode="inline" items={menuItems} selectedKeys={[location.pathname.startsWith('/facilities') ? '/facilities' : location.pathname]} onClick={({ key }) => navigate(key)} />
      </nav>
      <div className="sider-version" title={`版本 ${appVersion} · ${environmentLabel}`}>{collapsed ? `v${appVersion.split('.')[0]}` : `v${appVersion} · ${environmentLabel}`}</div>
    </Sider>
    <Layout>
      <Header className="app-header">
        <Button type="text" className="collapse-button" icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />} onClick={() => setCollapsed((value) => !value)} />
        <div className="header-context"><Typography.Text type="secondary">当前门店</Typography.Text><Select value={auth.store?.id} options={auth.user?.stores.map((store) => ({ value: store.id, label: `${store.name} · ${store.code}` }))} onChange={(id) => { const store = auth.user?.stores.find((item) => item.id === id); if (store) auth.setStore(store) }} variant="borderless" className="store-select" /></div>
        <div className="header-actions">
          <Popover trigger="click" placement="bottomRight" onOpenChange={(open) => { if (open) void notifications.refetch() }} content={<div className="header-popover notification-popover"><div className="header-popover-title"><strong>待办通知</strong><Typography.Text type="secondary">{notifications.data?.pendingCount ?? 0} 条待处理</Typography.Text></div>{notifications.data?.items.length ? <div className="notification-list">{notifications.data.items.map((item) => <button type="button" key={item.id} className="notification-item" onClick={() => navigate(item.targetUrl)}><span><strong>{item.title}</strong><Tag color={item.severity === 'error' ? 'red' : item.severity === 'warning' ? 'gold' : 'blue'}>{item.type === 'PriceOverrideApproval' ? '改价' : item.type === 'RefundApproval' ? '退款' : '交班'}</Tag></span><small>{item.description}</small><time>{new Date(item.occurredAtUtc).toLocaleString('zh-CN', { hour12: false })}</time></button>)}</div> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={notifications.isLoading ? '正在加载' : '暂无待办'} />}</div>}>
            <Tooltip title="待办通知"><Badge count={notifications.data?.pendingCount ?? 0} overflowCount={99} size="small"><Button type="text" className="header-icon-button" icon={<BellOutlined />} aria-label="待办通知" /></Badge></Tooltip>
          </Popover>
          <Popover trigger="click" placement="bottomRight" content={<div className="header-popover"><div className="header-popover-title"><strong>使用帮助</strong></div><div className="header-help-list"><span>① 先选择当前门店</span><span>② 从左侧进入对应业务</span><span>③ 涉及金额的操作会保留审计记录</span></div></div>}>
            <Tooltip title="使用帮助"><Button type="text" className="header-icon-button" icon={<QuestionCircleOutlined />} aria-label="使用帮助" /></Tooltip>
          </Popover>
          {settingsItems.length > 0 && <Dropdown menu={{ items: settingsItems, onClick: ({ key }) => navigate(key) }} placement="bottomRight" trigger={['click']}>
            <Tooltip title="系统设置"><Button type="text" className="header-icon-button" icon={<SettingOutlined />} aria-label="系统设置" /></Tooltip>
          </Dropdown>}
          <Dropdown menu={{ items: accountItems, onClick: ({ key }) => { if (key === 'password') navigate('/change-password'); if (key === 'logout') void logout() } }} placement="bottomRight" trigger={['click']}>
            <Button type="text" className="account-trigger" aria-label="个人账号菜单"><span className="header-user-copy"><strong>{auth.user?.displayName}</strong><small>{auth.user?.roles.join(' / ')}</small></span><Avatar className="user-avatar">{auth.user?.displayName.slice(0, 1)}</Avatar></Button>
          </Dropdown>
        </div>
      </Header>
      <Content className="app-content"><Outlet /></Content>
    </Layout>
  </Layout>
}
