import { AppstoreOutlined, AuditOutlined, BarChartOutlined, BellOutlined, ClockCircleOutlined, CloudServerOutlined, DatabaseOutlined, InboxOutlined, LockOutlined, LogoutOutlined, MenuFoldOutlined, MenuUnfoldOutlined, PayCircleOutlined, QuestionCircleOutlined, SafetyCertificateOutlined, SettingOutlined, ShopOutlined, TeamOutlined } from '@ant-design/icons'
import { Avatar, Badge, Button, Dropdown, Empty, Layout, Menu, Popover, Select, Tag, Tooltip, Typography, type MenuProps } from 'antd'
import { useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../auth/useAuth'
import { apiRequest } from '../api/client'
import type { NotificationInbox } from '../api/types'

const { Header, Sider, Content } = Layout
const baseMenuItems: NonNullable<MenuProps['items']> = [
  { key: '/', icon: <AppstoreOutlined />, label: '经营工作台' },
  { key: '/facilities', icon: <ClockCircleOutlined />, label: '设施接待' },
  { key: '/customers', icon: <TeamOutlined />, label: '顾客与会员' },
  { key: '/cashier', icon: <PayCircleOutlined />, label: '服务录单与收银' },
  { key: '/inventory', icon: <InboxOutlined />, label: '商品库存' },
  { type: 'divider' },
  { key: '/catalog/items', icon: <DatabaseOutlined />, label: '服务项目' },
  { key: '/catalog/products', icon: <DatabaseOutlined />, label: '产品目录' },
  { key: '/catalog/prices', icon: <DatabaseOutlined />, label: '价格管理' },
  { key: '/reports', icon: <BarChartOutlined />, label: '经营报表' },
  { key: '/audit', icon: <AuditOutlined />, label: '审计记录' },
]

export function AppLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const auth = useAuth(); const navigate = useNavigate(); const location = useLocation()
  const roles = auth.user?.roles ?? []
  const canConfigureFacilities = auth.user?.roles.some((role) => role === 'OWNER' || role === 'STORE_MANAGER')
  const isOwner = auth.user?.roles.includes('OWNER') ?? false
  const isManager = auth.user?.roles.includes('STORE_MANAGER') ?? false
  const visibleBaseKeys = isOwner || isManager ? undefined : new Set(roles.includes('FRONT_DESK')
    ? ['/', '/facilities', '/customers', '/cashier', '/catalog/items', '/catalog/products', '/catalog/prices']
    : roles.includes('CASHIER')
      ? ['/', '/customers', '/cashier', '/inventory', '/catalog/items', '/catalog/products', '/catalog/prices']
      : ['/', '/catalog/items', '/catalog/products', '/catalog/prices'])
  const visibleBaseItems = visibleBaseKeys
    ? baseMenuItems.filter((item) => item?.type === 'divider' || typeof item?.key === 'string' && visibleBaseKeys.has(item.key))
    : baseMenuItems
  const notifications = useQuery({ queryKey: ['notifications', auth.store?.id], enabled: Boolean(auth.store?.id), queryFn: () => apiRequest<NotificationInbox>(`/api/v1/notifications?storeId=${auth.store?.id}`), refetchInterval: 30_000 })
  const menuItems: MenuProps['items'] = [
    ...visibleBaseItems,
    ...(canConfigureFacilities ? [{ key: '/settings/facilities', icon: <SettingOutlined />, label: '门店设施配置' }] : []),
    ...(isOwner ? [{ key: '/settings/employees', icon: <SafetyCertificateOutlined />, label: '员工与权限' }, { key: '/settings/payment-channels', icon: <CloudServerOutlined />, label: '支付渠道配置' }] : []),
  ]
  const settingsItems: MenuProps['items'] = [
    ...(canConfigureFacilities ? [{ key: '/settings/facilities', icon: <ShopOutlined />, label: '门店设施配置' }] : []),
    ...(isOwner ? [{ key: '/settings/employees', icon: <SafetyCertificateOutlined />, label: '员工与权限' }, { key: '/settings/payment-channels', icon: <CloudServerOutlined />, label: '支付渠道配置' }] : []),
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
    <Sider width={232} collapsedWidth={76} collapsed={collapsed} className="app-sider">
      <div className="app-logo"><span><ShopOutlined /></span>{!collapsed && <strong>门店 ERP</strong>}</div>
      <Menu theme="dark" mode="inline" items={menuItems} selectedKeys={[location.pathname]} onClick={({ key }) => navigate(key)} />
      <div className="sider-version">{collapsed ? 'V2' : 'V2 · 开发中'}</div>
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
