import { PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Form, Input, InputNumber, Modal, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { ServiceItem } from '../api/types'

interface ItemForm { code: string; name: string; standardDurationMinutes: number }

export function ServiceItemsPage() {
  const [open, setOpen] = useState(false); const [form] = Form.useForm<ItemForm>(); const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['service-items'], queryFn: () => apiRequest<ServiceItem[]>('/api/v1/catalog/service-items') })
  const create = useMutation({ mutationFn: (values: ItemForm) => apiRequest<ServiceItem>('/api/v1/catalog/service-items', { method: 'POST', body: JSON.stringify(values) }), onSuccess: async () => { message.success('服务项目已创建'); setOpen(false); form.resetFields(); await queryClient.invalidateQueries({ queryKey: ['service-items'] }) } })
  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>服务项目</Typography.Title><Typography.Paragraph>项目名称、标准时长与启停状态在这里维护；最终收费由价格版本决定。</Typography.Paragraph></div><Button type="primary" icon={<PlusOutlined />} onClick={() => setOpen(true)}>新建项目</Button></div>
    {query.error && <Alert type="error" showIcon title={query.error instanceof Error ? query.error.message : '加载失败'} />}
    <Card variant="borderless"><Table<ServiceItem> rowKey="id" loading={query.isLoading} dataSource={query.data ?? []} pagination={{ pageSize: 10, showSizeChanger: false }} locale={{ emptyText: '还没有服务项目，请先新建项目' }} columns={[{ title: '项目编码', dataIndex: 'code', width: 160 }, { title: '项目名称', dataIndex: 'name' }, { title: '标准时长', dataIndex: 'standardDurationMinutes', width: 140, render: (value: number) => value ? `${value} 分钟` : '未设置' }, { title: '状态', dataIndex: 'status', width: 120, render: (value: string) => <Tag color={value === 'ENABLED' ? 'green' : 'default'}>{value === 'ENABLED' ? '启用' : '停用'}</Tag> }]} /></Card>
    <Modal title="新建服务项目" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} confirmLoading={create.isPending} okText="保存" cancelText="取消" destroyOnHidden>
      {create.error && <Alert type="error" showIcon title={create.error instanceof ApiError ? create.error.message : '保存失败'} className="modal-alert" />}
      <Form form={form} layout="vertical" onFinish={(values) => create.mutate(values)} requiredMark="optional"><Form.Item name="code" label="项目编码" rules={[{ required: true, message: '请输入项目编码' }, { max: 40 }]}><Input placeholder="例如：SV001" /></Form.Item><Form.Item name="name" label="项目名称" rules={[{ required: true, message: '请输入项目名称' }, { max: 120 }]}><Input placeholder="名称由门店业务自行定义" /></Form.Item><Form.Item name="standardDurationMinutes" label="标准时长（分钟）" initialValue={60} rules={[{ required: true }]} extra="仅用于服务记录和排班参考，不参与自动计价。"><InputNumber min={0} max={1440} precision={0} className="full-width" /></Form.Item></Form>
    </Modal>
  </div>
}

