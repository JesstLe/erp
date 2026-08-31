import { EditOutlined, MenuOutlined, PlusOutlined, ShopOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Descriptions, Form, Input, Modal, Space, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, apiRequest } from '../api/client'
import type { BrandProfile, NavigationLabels, OrganizationSettings, StoreProfile } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { configurableNavigationItems, defaultNavigationLabels } from '../navigationLabels'

interface BrandValues { code: string; name: string }
interface StoreValues { code?: string; name: string; timeZoneId: string; address?: string }
interface StatusValues { reason: string }
interface NavigationValues { labels: Record<string, string> }

export function OrganizationSettingsPage() {
  const auth = useAuth(); const queryClient = useQueryClient()
  const [brandForm] = Form.useForm<BrandValues>(); const [storeForm] = Form.useForm<StoreValues>()
  const [statusForm] = Form.useForm<StatusValues>(); const [brandOpen, setBrandOpen] = useState(false)
  const [navigationForm] = Form.useForm<NavigationValues>(); const [navigationOpen, setNavigationOpen] = useState(false)
  const [editingStore, setEditingStore] = useState<StoreProfile | 'new'>(); const [statusStore, setStatusStore] = useState<StoreProfile>()
  const settings = useQuery({ queryKey: ['organization-settings'], queryFn: () => apiRequest<OrganizationSettings>('/api/v1/organization/settings') })
  const navigationLabels = useQuery({ queryKey: ['navigation-labels'], queryFn: () => apiRequest<NavigationLabels>('/api/v1/navigation/labels') })
  const onError = (error: unknown) => message.error(error instanceof ApiError ? error.message : '操作失败')
  const refresh = async (success: string, refreshAuth = false) => {
    message.success(success)
    await queryClient.invalidateQueries({ queryKey: ['organization-settings'] })
    await queryClient.invalidateQueries({ queryKey: ['facility-configuration-stores'] })
    if (refreshAuth) await auth.refresh()
  }
  const updateBrand = useMutation({ mutationFn: (values: BrandValues) => apiRequest<BrandProfile>('/api/v1/organization/brand', { method: 'PUT', body: JSON.stringify({ ...values, expectedVersion: settings.data!.brand.version }) }), onSuccess: async () => { setBrandOpen(false); await refresh('品牌资料已更新') }, onError })
  const saveStore = useMutation({ mutationFn: (values: StoreValues) => editingStore === 'new' ? apiRequest<StoreProfile>('/api/v1/organization/stores', { method: 'POST', body: JSON.stringify({ name: values.name, timeZoneId: values.timeZoneId }) }) : apiRequest<StoreProfile>(`/api/v1/organization/stores/${editingStore!.id}`, { method: 'PUT', body: JSON.stringify({ ...values, expectedVersion: editingStore!.version }) }), onSuccess: async () => { const created = editingStore === 'new'; setEditingStore(undefined); storeForm.resetFields(); await refresh(created ? '门店已创建，编码已由系统自动分配' : '门店资料已更新', true) }, onError })
  const changeStatus = useMutation({ mutationFn: (values: StatusValues) => apiRequest<StoreProfile>(`/api/v1/organization/stores/${statusStore!.id}/status`, { method: 'POST', body: JSON.stringify({ enable: statusStore!.status !== 'Enabled', reason: values.reason, expectedVersion: statusStore!.version }) }), onSuccess: async (store) => { setStatusStore(undefined); statusForm.resetFields(); await refresh(store.status === 'Enabled' ? '门店已恢复启用' : '门店已停用，历史数据仍保留', true) }, onError })
  const updateNavigationLabels = useMutation({ mutationFn: (values: NavigationValues) => apiRequest<NavigationLabels>('/api/v1/navigation/labels', { method: 'PUT', body: JSON.stringify({ labels: values.labels, expectedVersion: navigationLabels.data!.version }) }), onSuccess: async () => { setNavigationOpen(false); message.success('左侧导航名称已更新'); await Promise.all([queryClient.invalidateQueries({ queryKey: ['navigation-labels'] }), queryClient.invalidateQueries({ queryKey: ['organization-settings'] })]) }, onError })
  const openBrand = () => { if (!settings.data) return; brandForm.setFieldsValue({ code: settings.data.brand.code, name: settings.data.brand.name }); setBrandOpen(true) }
  const openStore = (store: StoreProfile | 'new') => { storeForm.setFieldsValue(store === 'new' ? { name: '', timeZoneId: 'Asia/Shanghai', address: '' } : { code: store.code, name: store.name, timeZoneId: store.timeZoneId, address: store.address }); setEditingStore(store) }
  const openNavigationLabels = () => { navigationForm.setFieldsValue({ labels: { ...defaultNavigationLabels, ...navigationLabels.data?.labels } }); setNavigationOpen(true) }
  const enabledCount = settings.data?.stores.filter((store) => store.status === 'Enabled').length ?? 0

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>品牌与门店</Typography.Title><Typography.Paragraph>由最高权限账号维护品牌和全部门店主数据；停用代替删除，历史业务永久保留。</Typography.Paragraph></div><Button type="primary" icon={<PlusOutlined />} onClick={() => openStore('new')}>新增门店</Button></div>
    <Alert type="info" showIcon title="新门店创建后，先在“员工与权限”分配店长和员工，再到“门店设施配置”建立服务区与服务位。至少保留一家有效门店。" />
    <Card loading={settings.isLoading} title="品牌资料" extra={<Button icon={<EditOutlined />} onClick={openBrand}>编辑品牌</Button>}>
      {settings.error && <Alert type="error" showIcon title={settings.error instanceof Error ? settings.error.message : '品牌与门店加载失败'} />}
      {settings.data && <Descriptions column={3} items={[{ key: 'name', label: '品牌名称', children: settings.data.brand.name }, { key: 'code', label: '品牌编码', children: settings.data.brand.code }, { key: 'status', label: '状态', children: <Tag color="green">有效</Tag> }]} />}
    </Card>
    <Card loading={navigationLabels.isLoading} title={<Space><MenuOutlined />左侧导航名称</Space>} extra={<Button icon={<EditOutlined />} onClick={openNavigationLabels}>自定义名称</Button>}>
      <Typography.Paragraph type="secondary">名称按品牌统一生效，只改变左侧显示文字；页面地址、权限、数据和业务逻辑均保持不变。</Typography.Paragraph>
      <Space wrap>{configurableNavigationItems.map((item) => <Tag key={item.key}>{navigationLabels.data?.labels[item.key] || item.label}</Tag>)}</Space>
    </Card>
    <Card title={<Space><ShopOutlined />门店列表</Space>} extra={<Typography.Text type="secondary">{enabledCount} 家有效 / {settings.data?.stores.length ?? 0} 家全部</Typography.Text>}>
      <Table<StoreProfile> rowKey="id" loading={settings.isLoading} dataSource={settings.data?.stores} pagination={false} columns={[
        { title: '门店', key: 'store', render: (_: unknown, store) => <Space orientation="vertical" size={0}><Typography.Text strong>{store.name}</Typography.Text><Typography.Text type="secondary">{store.code} · {store.timeZoneId}</Typography.Text>{store.address && <Typography.Text type="secondary">{store.address}</Typography.Text>}</Space> },
        { title: '当前店长', dataIndex: 'managerNames', render: (names: string[]) => names.length ? names.map((name) => <Tag color="blue" key={name}>{name}</Tag>) : <Tag>尚未设置</Tag> },
        { title: '员工', dataIndex: 'employeeCount', render: (count: number) => `${count} 人` },
        { title: '服务区/服务位', key: 'facilities', render: (_: unknown, store) => `${store.facilityGroupCount} 区 · ${store.enabledFacilityCount}/${store.facilityCount} 位启用` },
        { title: '状态', dataIndex: 'status', render: (status: string) => <Tag color={status === 'Enabled' ? 'green' : 'default'}>{status === 'Enabled' ? '有效' : '已停用'}</Tag> },
        { title: '操作', key: 'actions', render: (_: unknown, store) => <Space><Button type="link" icon={<EditOutlined />} onClick={() => openStore(store)}>编辑</Button><Button type="link" danger={store.status === 'Enabled'} onClick={() => { statusForm.resetFields(); setStatusStore(store) }}>{store.status === 'Enabled' ? '停用' : '恢复'}</Button></Space> },
      ]} />
    </Card>

    <Modal title="编辑品牌资料" open={brandOpen} onCancel={() => setBrandOpen(false)} onOk={() => brandForm.submit()} okText="保存" confirmLoading={updateBrand.isPending} destroyOnHidden><Form<BrandValues> form={brandForm} layout="vertical" onFinish={(values) => updateBrand.mutate(values)}><Form.Item name="code" label="品牌编码" extra="品牌的永久唯一标识，创建后不可修改"><Input disabled /></Form.Item><Form.Item name="name" label="品牌名称" rules={[{ required: true }, { max: 100 }]}><Input maxLength={100} /></Form.Item></Form></Modal>
    <Modal title={editingStore === 'new' ? '新增门店' : '编辑门店'} open={Boolean(editingStore)} onCancel={() => setEditingStore(undefined)} onOk={() => storeForm.submit()} okText="保存" confirmLoading={saveStore.isPending} destroyOnHidden><Form<StoreValues> form={storeForm} layout="vertical" onFinish={(values) => saveStore.mutate(values)}>{editingStore === 'new' ? <Typography.Paragraph type="secondary">门店编码将由系统自动分配为下一可用的三位序号。</Typography.Paragraph> : <Form.Item name="code" label="门店编码" extra="永久唯一标识，创建后不可修改"><Input disabled /></Form.Item>}<Form.Item name="name" label="门店名称" rules={[{ required: true }, { max: 100 }]}><Input maxLength={100} /></Form.Item><Form.Item name="address" label="门店地址（小票展示）" rules={[{ max: 300 }]}><Input.TextArea rows={2} maxLength={300} showCount placeholder="可选；填写后用于结算小票和门店资料" /></Form.Item><Form.Item name="timeZoneId" label="业务时区" rules={[{ required: true }, { max: 64 }]}><Input maxLength={64} placeholder="Asia/Shanghai" /></Form.Item></Form></Modal>
    <Modal title={statusStore?.status === 'Enabled' ? '停用门店' : '恢复门店'} open={Boolean(statusStore)} onCancel={() => setStatusStore(undefined)} onOk={() => statusForm.submit()} okText="确认" okButtonProps={{ danger: statusStore?.status === 'Enabled' }} confirmLoading={changeStatus.isPending} destroyOnHidden><Alert type={statusStore?.status === 'Enabled' ? 'warning' : 'info'} showIcon title={statusStore?.status === 'Enabled' ? '存在使用中设施、未完成接待/消费单或未关闭班次时不能停用。历史数据不会删除。' : '恢复后门店重新出现在有效门店选择器中。'} className="modal-alert" /><Form<StatusValues> form={statusForm} layout="vertical" onFinish={(values) => changeStatus.mutate(values)}><Form.Item name="reason" label="原因" rules={[{ required: true }, { min: 2 }, { max: 200 }]}><Input.TextArea rows={4} showCount maxLength={200} /></Form.Item></Form></Modal>
    <Modal title="自定义左侧导航名称" open={navigationOpen} onCancel={() => setNavigationOpen(false)} onOk={() => navigationForm.submit()} okText="保存并生效" confirmLoading={updateNavigationLabels.isPending} width={720} destroyOnHidden><Alert type="info" showIcon title="这里只修改显示名称，不会修改菜单顺序、页面路由、角色权限或任何业务功能。" className="modal-alert" /><Form<NavigationValues> form={navigationForm} layout="vertical" onFinish={(values) => updateNavigationLabels.mutate(values)}><div className="navigation-label-form">{configurableNavigationItems.map((item) => <Form.Item key={item.key} name={['labels', item.key]} label={`${item.label}（${item.key}）`} rules={[{ required: true, whitespace: true }, { max: 24 }]}><Input maxLength={24} /></Form.Item>)}</div><Button type="link" onClick={() => navigationForm.setFieldsValue({ labels: { ...defaultNavigationLabels } })}>恢复系统默认名称</Button></Form></Modal>
  </div>
}
