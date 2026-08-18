import { AppstoreOutlined, AuditOutlined, BarChartOutlined, ClockCircleOutlined, CloudServerOutlined, DatabaseOutlined, InboxOutlined, LogoutOutlined, MenuFoldOutlined, MenuUnfoldOutlined, PayCircleOutlined, SafetyCertificateOutlined, ShopOutlined, TeamOutlined } from '@ant-design/icons'
import { Avatar, Button, Layout, Menu, Select, Space, Typography, type MenuProps } from 'antd'
import { useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'

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
  { key: '/catalog/prices', icon: <DatabaseOutlined />, label: '价格版本' },
  { key: '/reports', icon: <BarChartOutlined />, label: '经营报表' },
  { key: '/audit', icon: <AuditOutlined />, label: '审计记录' },
]

export function AppLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const auth = useAuth(); const navigate = useNavigate(); const location = useLocation()
  const menuItems: MenuProps['items'] = auth.user?.roles.includes('OWNER')
    ? [...baseMenuItems, { key: '/settings/employees', icon: <SafetyCertificateOutlined />, label: '员工与权限' }, { key: '/settings/payment-channels', icon: <CloudServerOutlined />, label: '支付渠道配置' }]
    : baseMenuItems
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
        <Space className="header-user">
          <div className="header-user-copy"><strong>{auth.user?.displayName}</strong><span>{auth.user?.roles.join(' / ')}</span></div>
          <Avatar className="user-avatar">{auth.user?.displayName.slice(0, 1)}</Avatar>
          <Button type="text" icon={<LogoutOutlined />} onClick={logout}>退出登录</Button>
        </Space>
      </Header>
      <Content className="app-content"><Outlet /></Content>
    </Layout>
  </Layout>
}
