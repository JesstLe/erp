import { DeleteOutlined, EditOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { ServiceItem } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface ItemForm { code?: string; name: string; standardDurationMinutes: number; status: string }

function requestError(error: unknown): string {
  return error instanceof ApiError ? error.message : '操作失败，请稍后重试'
}

export function ServiceItemsPage() {
  const auth = useAuth(); const canManage = auth.user?.roles.includes('OWNER') ?? false
  const [open, setOpen] = useState(false); const [editing, setEditing] = useState<ServiceItem>()
  const [queryText, setQueryText] = useState(''); const [appliedQuery, setAppliedQuery] = useState(''); const [status, setStatus] = useState<string>()
  const [form] = Form.useForm<ItemForm>(); const queryClient = useQueryClient()
  const params = new URLSearchParams(); if (appliedQuery) params.set('query', appliedQuery); if (status) params.set('status', status)
  const path = `/api/v1/catalog/service-items${params.size ? `?${params}` : ''}`
  const query = useQuery({ queryKey: ['service-items', appliedQuery, status], queryFn: () => apiRequest<ServiceItem[]>(path) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['service-items'] })
  const save = useMutation({
    mutationFn: (values: ItemForm) => editing
      ? apiRequest<ServiceItem>(`/api/v1/catalog/service-items/${editing.id}`, { method: 'PUT', body: JSON.stringify({ ...values, expectedVersion: editing.version }) })
      : apiRequest<ServiceItem>('/api/v1/catalog/service-items', { method: 'POST', body: JSON.stringify(values) }),
    onSuccess: async () => { message.success(editing ? '服务项目已更新' : '服务项目已创建'); setOpen(false); setEditing(undefined); form.resetFields(); await refresh() },
    onError: (error) => message.error(requestError(error)),
  })
  const updateStatus = useMutation({
    mutationFn: ({ item, nextStatus }: { item: ServiceItem; nextStatus: string }) => apiRequest<ServiceItem>(`/api/v1/catalog/service-items/${item.id}`, { method: 'PUT', body: JSON.stringify({ name: item.name, standardDurationMinutes: item.standardDurationMinutes, status: nextStatus, expectedVersion: item.version }) }),
    onSuccess: async (_, variables) => { message.success(variables.nextStatus === 'ENABLED' ? '服务项目已恢复' : '服务项目已停用'); await refresh() },
    onError: (error) => message.error(requestError(error)),
  })
  const remove = useMutation({
    mutationFn: (item: ServiceItem) => apiRequest<void>(`/api/v1/catalog/service-items/${item.id}?expectedVersion=${item.version}`, { method: 'DELETE' }),
    onSuccess: async () => { message.success('未使用的服务项目已删除'); await refresh() },
    onError: (error) => message.error(requestError(error)),
  })
  const showCreate = () => { setEditing(undefined); form.resetFields(); form.setFieldsValue({ standardDurationMinutes: 60, status: 'ENABLED' }); setOpen(true) }
  const showEdit = (item: ServiceItem) => { setEditing(item); form.setFieldsValue({ code: item.code, name: item.name, standardDurationMinutes: item.standardDurationMinutes, status: item.status }); setOpen(true) }
  const search = () => setAppliedQuery(queryText.trim())
  const reset = () => { setQueryText(''); setAppliedQuery(''); setStatus(undefined) }

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>服务项目</Typography.Title><Typography.Paragraph>支持查询、修改、停用与恢复；项目编码创建后不变，最终收费由价格版本决定。</Typography.Paragraph></div>{canManage && <Button type="primary" icon={<PlusOutlined />} onClick={showCreate}>新建项目</Button>}</div>
    <Alert type="info" showIcon title="删除仅用于从未进入价格、接待或订单的误建项目；已有业务记录的项目请停用，以保留历史账目。" />
    <Card variant="borderless"><Space wrap><Input value={queryText} onChange={(event) => setQueryText(event.target.value)} onPressEnter={search} allowClear placeholder="输入项目编码或名称" maxLength={100} style={{ width: 280 }} /><Select value={status} onChange={setStatus} allowClear placeholder="全部状态" style={{ width: 140 }} options={[{ value: 'ENABLED', label: '启用' }, { value: 'DISABLED', label: '停用' }]} /><Button type="primary" icon={<SearchOutlined />} onClick={search}>查询</Button><Button onClick={reset}>重置</Button></Space></Card>
    {query.error && <Alert type="error" showIcon title={requestError(query.error)} />}
    <Card variant="borderless"><Table<ServiceItem> rowKey="id" loading={query.isLoading} dataSource={query.data ?? []} pagination={{ pageSize: 10, showSizeChanger: false }} locale={{ emptyText: '没有符合条件的服务项目' }} columns={[
      { title: '项目编码', dataIndex: 'code', width: 150 }, { title: '项目名称', dataIndex: 'name' }, { title: '标准时长', dataIndex: 'standardDurationMinutes', width: 120, render: (value: number) => value ? `${value} 分钟` : '未设置' }, { title: '状态', dataIndex: 'status', width: 90, render: (value: string) => <Tag color={value === 'ENABLED' ? 'green' : 'default'}>{value === 'ENABLED' ? '启用' : '停用'}</Tag> },
      { title: '操作', key: 'actions', width: 260, render: (_: unknown, item: ServiceItem) => canManage ? <Space size="small"><Button size="small" icon={<EditOutlined />} onClick={() => showEdit(item)}>编辑</Button><Popconfirm title={item.status === 'ENABLED' ? '确认停用这个项目？' : '确认恢复这个项目？'} description={item.status === 'ENABLED' ? '停用后不能用于新业务，历史记录不受影响。' : '恢复后可重新用于新业务。'} onConfirm={() => updateStatus.mutateAsync({ item, nextStatus: item.status === 'ENABLED' ? 'DISABLED' : 'ENABLED' })}><Button size="small">{item.status === 'ENABLED' ? '停用' : '恢复'}</Button></Popconfirm><Popconfirm title="永久删除这个项目？" description="只有从未被业务引用的项目可以删除。" okButtonProps={{ danger: true }} onConfirm={() => remove.mutateAsync(item)}><Button size="small" danger icon={<DeleteOutlined />}>删除</Button></Popconfirm></Space> : null },
    ]} /></Card>
    <Modal title={editing ? '编辑服务项目' : '新建服务项目'} open={open} onCancel={() => { setOpen(false); setEditing(undefined) }} onOk={() => form.submit()} confirmLoading={save.isPending} okText="保存" cancelText="取消" destroyOnHidden>
      {save.error && <Alert type="error" showIcon title={requestError(save.error)} className="modal-alert" />}
      <Form<ItemForm> form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark="optional"><Form.Item name="code" label={editing ? '项目编码（只读）' : '项目编码'} rules={[{ required: true, message: '请输入项目编码' }, { max: 40 }]} extra={editing ? '编码用于关联历史记录，创建后不可修改。' : undefined}><Input disabled={Boolean(editing)} maxLength={40} placeholder="例如：SV001" /></Form.Item><Form.Item name="name" label="项目名称" rules={[{ required: true, message: '请输入项目名称' }, { max: 120 }]}><Input maxLength={120} placeholder="名称由业务自行定义" /></Form.Item><Form.Item name="standardDurationMinutes" label="标准时长（分钟）" rules={[{ required: true }]} extra="仅用于服务记录和排班参考，不参与自动计价。"><InputNumber min={0} max={1440} precision={0} className="full-width" /></Form.Item>{editing && <Form.Item name="status" label="状态" rules={[{ required: true }]}><Select options={[{ value: 'ENABLED', label: '启用' }, { value: 'DISABLED', label: '停用' }]} /></Form.Item>}</Form>
    </Modal>
  </div>
}
