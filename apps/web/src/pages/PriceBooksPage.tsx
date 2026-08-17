import { CheckCircleOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Space, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { PriceBook, ServiceItem } from '../api/types'

interface PriceForm { name: string; effectiveFrom: Dayjs; prices: Record<string, number> }

export function PriceBooksPage() {
  const [open, setOpen] = useState(false); const [form] = Form.useForm<PriceForm>(); const queryClient = useQueryClient()
  const items = useQuery({ queryKey: ['service-items'], queryFn: () => apiRequest<ServiceItem[]>('/api/v1/catalog/service-items') })
  const books = useQuery({ queryKey: ['price-books'], queryFn: () => apiRequest<PriceBook[]>('/api/v1/catalog/price-books') })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['price-books'] })
  const create = useMutation({ mutationFn: (values: PriceForm) => apiRequest<PriceBook>('/api/v1/catalog/price-books', { method: 'POST', body: JSON.stringify({ name: values.name, effectiveFrom: values.effectiveFrom.format('YYYY-MM-DD'), lines: Object.entries(values.prices ?? {}).filter(([, value]) => value !== undefined).map(([serviceItemId, yuan]) => ({ serviceItemId, unitPriceMinor: Math.round(yuan * 100) })) }) }), onSuccess: async () => { message.success('价格草稿已创建'); setOpen(false); form.resetFields(); await refresh() } })
  const publish = useMutation({ mutationFn: (id: string) => apiRequest<PriceBook>(`/api/v1/catalog/price-books/${id}/publish`, { method: 'POST' }), onSuccess: async () => { message.success('价格版本已发布'); await refresh() } })
  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>价格版本</Typography.Title><Typography.Paragraph>标准价格由最高权限账号建立和发布。已发布版本保留历史快照，不直接覆盖。</Typography.Paragraph></div><Button type="primary" icon={<PlusOutlined />} onClick={() => setOpen(true)} disabled={!items.data?.length}>新建价格版本</Button></div>
    {!items.isLoading && !items.data?.length && <Alert type="info" showIcon title="请先创建服务项目，再建立价格版本。" />}
    <Card variant="borderless"><Table<PriceBook> rowKey="id" loading={books.isLoading} dataSource={books.data ?? []} expandable={{ expandedRowRender: (book) => <Table rowKey="serviceItemId" size="small" pagination={false} dataSource={book.lines} columns={[{ title: '服务项目', dataIndex: 'serviceItemName' }, { title: '标准价格', dataIndex: 'unitPriceMinor', render: (value: number) => `¥${(value / 100).toFixed(2)}` }]} /> }} columns={[{ title: '版本名称', dataIndex: 'name' }, { title: '生效日期', dataIndex: 'effectiveFrom', width: 140 }, { title: '项目数', dataIndex: 'lines', width: 100, render: (lines: PriceBook['lines']) => lines.length }, { title: '状态', dataIndex: 'status', width: 120, render: (value: string) => <Tag color={value === 'PUBLISHED' ? 'green' : 'orange'}>{value === 'PUBLISHED' ? '已发布' : '草稿'}</Tag> }, { title: '操作', key: 'actions', width: 140, render: (_, book) => book.status === 'DRAFT' ? <Button type="link" icon={<CheckCircleOutlined />} loading={publish.isPending} onClick={() => publish.mutate(book.id)}>发布</Button> : <Typography.Text type="secondary">只读</Typography.Text> }]} /></Card>
    <Modal title="新建价格版本" width={700} open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} confirmLoading={create.isPending} okText="保存草稿" destroyOnHidden>
      {create.error && <Alert type="error" showIcon title={create.error instanceof ApiError ? create.error.message : '保存失败'} className="modal-alert" />}
      <Form form={form} layout="vertical" onFinish={(values) => create.mutate(values)} initialValues={{ effectiveFrom: dayjs() }}><Space size={16} align="start" className="full-width"><Form.Item name="name" label="版本名称" rules={[{ required: true, message: '请输入版本名称' }]} className="grow"><Input placeholder="例如：2026年秋季标准价" /></Form.Item><Form.Item name="effectiveFrom" label="生效日期" rules={[{ required: true }]}><DatePicker /></Form.Item></Space><Typography.Title level={5}>项目标准价格</Typography.Title><div className="price-entry-list">{items.data?.map((item) => <div key={item.id}><span><strong>{item.name}</strong><small>{item.code} · {item.standardDurationMinutes}分钟</small></span><Form.Item name={['prices', item.id]} rules={[{ required: true, message: '请输入价格' }]}><InputNumber min={0} precision={2} prefix="¥" /></Form.Item></div>)}</div></Form>
    </Modal>
  </div>
}

