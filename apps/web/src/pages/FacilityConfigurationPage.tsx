import { EditOutlined, PlusOutlined, ShopOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Descriptions, Empty, Form, Input, InputNumber, Modal, Select, Space, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { ApiError, apiRequest } from '../api/client'
import type { FacilityConfiguration, FacilityConfigurationGroup, FacilityConfigurationItem, FacilityConfigurationStore, FacilityType } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

interface GroupValues { displayName: string; sortOrder: number }
interface FacilityValues {
  groupId: string
  facilityTypeId?: string
  code?: string
  displayName: string
  serviceName?: string
  equipmentName?: string
  referencePriceYuan?: number
  sortOrder: number
  defaultCleaningMinutes: number
  allowReservation: boolean
  lifecycleStatus: string
}

const lifecycleOptions = [
  { value: 'Enabled', label: '启用' },
  { value: 'Maintenance', label: '维护中' },
  { value: 'Disabled', label: '停用' },
]

function money(minor?: number | null) { return minor === undefined || minor === null ? '未填写' : `¥${(minor / 100).toFixed(2)}` }

export function FacilityConfigurationPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const { can } = useAuthorization(); const isOwner = can(Permission.FacilityConfigureAllStores)
  const canConfigure = can(Permission.FacilityConfigure)
  const [storeId, setStoreId] = useState(auth.store?.id)
  const [editingGroup, setEditingGroup] = useState<FacilityConfigurationGroup | 'new'>()
  const [editingFacility, setEditingFacility] = useState<FacilityConfigurationItem | 'new'>()
  const [newFacilityGroupId, setNewFacilityGroupId] = useState<string>()
  const [groupForm] = Form.useForm<GroupValues>()
  const [facilityForm] = Form.useForm<FacilityValues>()

  const stores = useQuery({
    queryKey: ['facility-configuration-stores'],
    enabled: isOwner,
    queryFn: () => apiRequest<FacilityConfigurationStore[]>('/api/v1/facilities/configuration/stores'),
  })
  const types = useQuery({ queryKey: ['facility-types'], enabled: canConfigure, queryFn: () => apiRequest<FacilityType[]>('/api/v1/facilities/types') })
  const configuration = useQuery({
    queryKey: ['facility-configuration', storeId],
    enabled: canConfigure && Boolean(storeId),
    queryFn: () => apiRequest<FacilityConfiguration>(`/api/v1/facilities/configuration?storeId=${storeId}`),
  })

  const storeOptions = useMemo(() => isOwner
    ? (stores.data ?? []).map((store) => ({ value: store.id, label: `${store.name} · ${store.code}` }))
    : (auth.user?.stores ?? []).map((store) => ({ value: store.id, label: `${store.name} · ${store.code}` })),
  [auth.user?.stores, isOwner, stores.data])

  useEffect(() => {
    if (!storeId && storeOptions[0]) setStoreId(storeOptions[0].value)
  }, [storeId, storeOptions])

  const refresh = async () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['facility-configuration', storeId] }),
    queryClient.invalidateQueries({ queryKey: ['facility-configuration-stores'] }),
    queryClient.invalidateQueries({ queryKey: ['facility-board', storeId] }),
  ])
  const onError = (error: unknown) => message.error(error instanceof ApiError ? error.message : '保存失败')
  const saveGroup = useMutation({
    mutationFn: (values: GroupValues) => editingGroup === 'new'
      ? apiRequest('/api/v1/facilities/groups', { method: 'POST', body: JSON.stringify({ ...values, storeId }) })
      : apiRequest(`/api/v1/facilities/groups/${editingGroup?.id}`, { method: 'PUT', body: JSON.stringify({ ...values, storeId, expectedVersion: editingGroup?.version }) }),
    onSuccess: async () => { message.success(editingGroup === 'new' ? '服务区已创建' : '服务区已更新'); setEditingGroup(undefined); groupForm.resetFields(); await refresh() },
    onError,
  })
  const saveFacility = useMutation({
    mutationFn: (values: FacilityValues) => {
      const body = {
        ...values,
        storeId,
        referencePriceMinor: values.referencePriceYuan === undefined ? null : Math.round(values.referencePriceYuan * 100),
        expectedVersion: editingFacility === 'new' ? undefined : editingFacility?.version,
      }
      return editingFacility === 'new'
        ? apiRequest('/api/v1/facilities', { method: 'POST', body: JSON.stringify(body) })
        : apiRequest(`/api/v1/facilities/${editingFacility?.id}`, { method: 'PUT', body: JSON.stringify(body) })
    },
    onSuccess: async () => { message.success(editingFacility === 'new' ? '服务位已创建，编号已由系统自动分配' : '服务位已更新'); setEditingFacility(undefined); facilityForm.resetFields(); await refresh() },
    onError,
  })

  const openGroup = (group: FacilityConfigurationGroup | 'new') => {
    setEditingGroup(group)
    groupForm.resetFields()
    groupForm.setFieldsValue(group === 'new' ? { displayName: '', sortOrder: 10 } : { displayName: group.displayName, sortOrder: group.sortOrder })
  }
  const openFacility = (facility: FacilityConfigurationItem | 'new', groupId?: string) => {
    setEditingFacility(facility)
    setNewFacilityGroupId(groupId)
    facilityForm.resetFields()
    facilityForm.setFieldsValue(facility === 'new' ? {
      groupId, sortOrder: 10, defaultCleaningMinutes: 0, allowReservation: false, lifecycleStatus: 'Enabled',
    } : {
      groupId: facility.groupId, facilityTypeId: facility.facilityTypeId, code: facility.code,
      displayName: facility.displayName, serviceName: facility.serviceName ?? undefined, equipmentName: facility.equipmentName ?? undefined,
      referencePriceYuan: facility.referencePriceMinor === undefined || facility.referencePriceMinor === null ? undefined : facility.referencePriceMinor / 100,
      sortOrder: facility.sortOrder, defaultCleaningMinutes: facility.defaultCleaningMinutes,
      allowReservation: facility.allowReservation, lifecycleStatus: facility.lifecycleStatus,
    })
  }

  if (!canConfigure) return <Alert type="error" showIcon title="当前账号没有门店设施配置权限" />

  const facilityColumns = [
    { title: '服务位', key: 'position', render: (_: unknown, item: FacilityConfigurationItem) => <Space orientation="vertical" size={0}><Typography.Text strong>{item.displayName}</Typography.Text><Typography.Text type="secondary">{item.code} · {item.typeName}</Typography.Text></Space> },
    { title: '可选业务说明', key: 'optional', render: (_: unknown, item: FacilityConfigurationItem) => <Space orientation="vertical" size={0}><span>服务：{item.serviceName ?? '未填写'}</span><span>设施：{item.equipmentName ?? '未填写'}</span></Space> },
    { title: '参考单价', dataIndex: 'referencePriceMinor', render: (value?: number | null) => <Space orientation="vertical" size={0}><span>{money(value)}</span>{value !== undefined && value !== null && <Typography.Text type="secondary">仅提示，不参与收银</Typography.Text>}</Space> },
    { title: '状态', key: 'status', render: (_: unknown, item: FacilityConfigurationItem) => <Space><Tag color={item.lifecycleStatus === 'Enabled' ? 'green' : item.lifecycleStatus === 'Maintenance' ? 'orange' : 'default'}>{lifecycleOptions.find((option) => option.value === item.lifecycleStatus)?.label ?? item.lifecycleStatus}</Tag>{item.hasOpenSession && <Tag color="blue">使用中</Tag>}</Space> },
    { title: '操作', key: 'actions', render: (_: unknown, item: FacilityConfigurationItem) => <Button type="link" icon={<EditOutlined />} onClick={() => openFacility(item)}>编辑</Button> },
  ]

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>门店设施配置</Typography.Title><Typography.Paragraph>集中查看各门店店长、服务区和服务位；这里的参考价格只作信息提示。</Typography.Paragraph></div><Space><Select value={storeId} options={storeOptions} onChange={setStoreId} className="store-select" /><Button icon={<PlusOutlined />} onClick={() => openGroup('new')}>新增服务区</Button></Space></div>
    <Alert type="info" showIcon title="服务区和服务位名称用于看板定位，必须填写；服务名称、内部设施名称、参考单价、设施类型等均可留空。停用代替删除，历史接待记录不会被改写。" />

    {isOwner && <Card title={<Space><ShopOutlined />全部门店概览</Space>} extra={<Typography.Text type="secondary">选择“配置”可切换下方门店</Typography.Text>}>
      <Table rowKey="id" size="small" pagination={false} loading={stores.isLoading} dataSource={stores.data ?? []} columns={[
        { title: '门店', key: 'store', render: (_: unknown, item: FacilityConfigurationStore) => <><Typography.Text strong>{item.name}</Typography.Text><br /><Typography.Text type="secondary">{item.code}</Typography.Text></> },
        { title: '当前店长', dataIndex: 'managerNames', render: (names: string[]) => names.length ? names.map((name) => <Tag key={name} color="blue">{name}</Tag>) : <Tag>尚未设置</Tag> },
        { title: '服务区', dataIndex: 'groupCount', render: (count: number) => `${count} 个` },
        { title: '服务位', key: 'facilities', render: (_: unknown, item: FacilityConfigurationStore) => `${item.enabledFacilityCount} / ${item.facilityCount} 个启用` },
        { title: '操作', key: 'action', render: (_: unknown, item: FacilityConfigurationStore) => <Button type="link" onClick={() => setStoreId(item.id)}>配置</Button> },
      ]} />
    </Card>}

    <Card loading={configuration.isLoading} title={configuration.data ? `${configuration.data.storeName} · ${configuration.data.storeCode}` : '门店配置'} extra={configuration.data && <span>当前店长：{configuration.data.managerNames.length ? configuration.data.managerNames.join('、') : '尚未设置'}</span>}>
      {configuration.error && <Alert type="error" showIcon title={configuration.error instanceof Error ? configuration.error.message : '配置加载失败'} />}
      {!configuration.data?.groups.length && !configuration.isLoading && <Empty description="当前门店还没有服务区" />}
      <Space orientation="vertical" size={16} className="full-width">
        {configuration.data?.groups.map((group) => <Card key={group.id} size="small" title={`${group.displayName} · ${group.facilities.length} 个服务位`} extra={<Space><Button size="small" icon={<EditOutlined />} onClick={() => openGroup(group)}>编辑服务区</Button><Button size="small" type="primary" icon={<PlusOutlined />} onClick={() => openFacility('new', group.id)}>新增服务位</Button></Space>}>
          <Table rowKey="id" size="small" pagination={false} dataSource={group.facilities} columns={facilityColumns} locale={{ emptyText: '该服务区还没有服务位' }} />
        </Card>)}
      </Space>
    </Card>

    <Modal title={editingGroup === 'new' ? '新增服务区' : '编辑服务区'} open={Boolean(editingGroup)} onCancel={() => setEditingGroup(undefined)} onOk={() => groupForm.submit()} okText="保存" confirmLoading={saveGroup.isPending} destroyOnHidden>
      <Form<GroupValues> form={groupForm} layout="vertical" onFinish={(values) => saveGroup.mutate(values)}><Form.Item name="displayName" label="服务区名称" rules={[{ required: true }, { max: 50 }]}><Input placeholder="例如 一楼服务区；名称由门店自定义" maxLength={50} /></Form.Item><Form.Item name="sortOrder" label="显示顺序" rules={[{ required: true }]}><InputNumber precision={0} /></Form.Item></Form>
    </Modal>

    <Modal title={editingFacility === 'new' ? '新增服务位' : '编辑服务位'} width={760} open={Boolean(editingFacility)} onCancel={() => setEditingFacility(undefined)} onOk={() => facilityForm.submit()} okText="保存" confirmLoading={saveFacility.isPending} destroyOnHidden>
      <Alert type="warning" showIcon title="参考单价不进入消费单。最终收费仍由有权限人员在收银录单时明确输入。" className="modal-alert" />
      {editingFacility !== 'new' && editingFacility && <Descriptions size="small" bordered column={2} className="modal-alert" items={[{ key: 'store', label: '门店', children: configuration.data?.storeName }, { key: 'using', label: '当前占用', children: editingFacility.hasOpenSession ? '正在使用，不可维护或停用' : '未占用' }]} />}
      <Form<FacilityValues> form={facilityForm} layout="vertical" onFinish={(values) => saveFacility.mutate(values)} initialValues={{ groupId: newFacilityGroupId }}>
        <Space align="start" className="full-width"><Form.Item name="groupId" label="所属服务区" rules={[{ required: true }]} className="grow"><Select options={configuration.data?.groups.map((group) => ({ value: group.id, label: group.displayName }))} /></Form.Item><Form.Item name="displayName" label="服务位名称" rules={[{ required: true }, { max: 50 }]} className="grow"><Input placeholder="例如 A01服务位" maxLength={50} /></Form.Item></Space>
        {editingFacility === 'new' ? <Alert type="info" showIcon title="保存后系统将自动生成当前门店内唯一编号，例如 F0001。" className="modal-alert" /> : <Form.Item label="服务位编号" extra="系统永久标识，创建后不可修改。"><Input value={editingFacility?.code ?? ''} disabled /></Form.Item>}
        <Form.Item name="facilityTypeId" label="设施类型（可选）"><Select allowClear placeholder="留空使用通用类型" options={types.data?.map((type) => ({ value: type.id, label: type.displayName }))} /></Form.Item>
        <Space align="start" className="full-width"><Form.Item name="serviceName" label="服务名称（可选）" rules={[{ max: 120 }]} className="grow"><Input placeholder="例如 基础护理" maxLength={120} /></Form.Item><Form.Item name="equipmentName" label="内部设施名称（可选）" rules={[{ max: 120 }]} className="grow"><Input placeholder="例如 护理床、仪器名称" maxLength={120} /></Form.Item></Space>
        <Space align="start" className="full-width"><Form.Item name="referencePriceYuan" label="参考单价（元，可选）" className="grow"><InputNumber min={0} max={100000000} precision={2} prefix="¥" className="full-width" /></Form.Item><Form.Item name="lifecycleStatus" label="使用状态" rules={[{ required: true }]} className="grow"><Select disabled={Boolean(editingFacility !== 'new' && editingFacility?.hasOpenSession)} options={lifecycleOptions} /></Form.Item></Space>
        <Space align="start"><Form.Item name="sortOrder" label="显示顺序" rules={[{ required: true }]}><InputNumber precision={0} /></Form.Item><Form.Item name="defaultCleaningMinutes" label="默认清洁分钟（可选）"><InputNumber min={0} max={1440} precision={0} /></Form.Item></Space>
        <Form.Item name="allowReservation" valuePropName="checked"><Checkbox>允许预约（当前只保存配置，不自动创建预约）</Checkbox></Form.Item>
      </Form>
    </Modal>
  </div>
}
