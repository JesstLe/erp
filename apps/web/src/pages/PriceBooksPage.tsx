import { CheckCircleOutlined, EditOutlined, PlusOutlined, StopOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Space, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { PriceBook, ProductItem, ServiceItem } from '../api/types'

interface PriceForm { name: string; effectiveFrom: Dayjs; serviceSelected: Record<string, boolean>; productSelected: Record<string, boolean>; prices: Record<string, number>; productPrices: Record<string, number> }

export function PriceBooksPage() {
  const [open, setOpen] = useState(false); const [editing, setEditing] = useState<PriceBook>()
  const [form] = Form.useForm<PriceForm>(); const queryClient = useQueryClient()
  const effectiveFrom = Form.useWatch('effectiveFrom', form)
  const items = useQuery({ queryKey: ['service-items'], queryFn: () => apiRequest<ServiceItem[]>('/api/v1/catalog/service-items') })
  const products = useQuery({ queryKey: ['product-items'], queryFn: () => apiRequest<ProductItem[]>('/api/v1/catalog/products') })
  const books = useQuery({ queryKey: ['price-books'], queryFn: () => apiRequest<PriceBook[]>('/api/v1/catalog/price-books') })
  const targetDate = (effectiveFrom ?? dayjs()).format('YYYY-MM-DD')
  const currentBook = useMemo(() => books.data?.filter((book) => book.status === 'PUBLISHED' && book.effectiveFrom <= targetDate).sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom) || (b.publishedAtUtc ?? '').localeCompare(a.publishedAtUtc ?? ''))[0], [books.data, targetDate])
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['price-books'] })
  const save = useMutation({
    mutationFn: (values: PriceForm) => {
      const body = {
        name: values.name, effectiveFrom: values.effectiveFrom.format('YYYY-MM-DD'),
        lines: Object.entries(values.serviceSelected ?? {}).filter(([, selected]) => selected).map(([serviceItemId]) => ({ serviceItemId, unitPriceMinor: Math.round(values.prices[serviceItemId] * 100) })),
        productLines: Object.entries(values.productSelected ?? {}).filter(([, selected]) => selected).map(([productItemId]) => ({ productItemId, unitPriceMinor: Math.round(values.productPrices[productItemId] * 100) })),
        expectedVersion: editing?.version,
      }
      return apiRequest<PriceBook>(editing ? `/api/v1/catalog/price-books/${editing.id}` : '/api/v1/catalog/price-books', { method: editing ? 'PUT' : 'POST', body: JSON.stringify(body) })
    },
    onSuccess: async () => { message.success(editing ? '价格草稿已更新' : '价格草稿已创建；未勾选项目已继承当前生效价格'); setOpen(false); setEditing(undefined); form.resetFields(); await refresh() },
  })
  const publish = useMutation({ mutationFn: (id: string) => apiRequest<PriceBook>(`/api/v1/catalog/price-books/${id}/publish`, { method: 'POST' }), onSuccess: async () => { message.success('价格版本已发布'); await refresh() } })
  const cancel = useMutation({ mutationFn: (book: PriceBook) => apiRequest<PriceBook>(`/api/v1/catalog/price-books/${book.id}/cancel`, { method: 'POST', body: JSON.stringify({ expectedVersion: book.version }) }), onSuccess: async () => { message.success('价格草稿已取消并保留历史'); await refresh() } })
  const openCreate = () => { setEditing(undefined); form.resetFields(); form.setFieldsValue({ effectiveFrom: dayjs(), serviceSelected: {}, productSelected: {}, prices: {}, productPrices: {} }); setOpen(true) }
  const openEdit = (book: PriceBook) => {
    setEditing(book)
    form.setFieldsValue({
      name: book.name, effectiveFrom: dayjs(book.effectiveFrom),
      serviceSelected: Object.fromEntries(book.lines.map((line) => [line.serviceItemId, true])),
      prices: Object.fromEntries(book.lines.map((line) => [line.serviceItemId, line.unitPriceMinor / 100])),
      productSelected: Object.fromEntries(book.productLines.map((line) => [line.productItemId, true])),
      productPrices: Object.fromEntries(book.productLines.map((line) => [line.productItemId, line.unitPriceMinor / 100])),
    })
    setOpen(true)
  }

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>价格版本</Typography.Title><Typography.Paragraph>服务与商品独立定价。草稿可继续编辑或取消；已发布版本永久只读，改价必须建立新版本。</Typography.Paragraph></div><Button type="primary" icon={<PlusOutlined />} onClick={openCreate} disabled={!items.data?.length && !products.data?.length}>新建价格版本</Button></div>
    {!items.isLoading && !products.isLoading && !items.data?.length && !products.data?.length && <Alert type="info" showIcon title="请先创建服务项目或产品，再建立价格版本。" />}
    <Card variant="borderless"><Table<PriceBook> rowKey="id" loading={books.isLoading} dataSource={books.data ?? []} expandable={{ expandedRowRender: (book) => <Table rowKey="key" size="small" pagination={false} dataSource={[...book.lines.map((line) => ({ key: `service-${line.serviceItemId}`, type: '服务项目', name: line.serviceItemName, unit: '次', unitPriceMinor: line.unitPriceMinor })), ...book.productLines.map((line) => ({ key: `product-${line.productItemId}`, type: '产品', name: line.productItemName, unit: line.unitName, unitPriceMinor: line.unitPriceMinor }))]} columns={[{ title: '类型', dataIndex: 'type', width: 110, render: (value: string) => <Tag color={value === '产品' ? 'blue' : 'cyan'}>{value}</Tag> }, { title: '目录名称', dataIndex: 'name' }, { title: '单位', dataIndex: 'unit', width: 100 }, { title: '标准价格', dataIndex: 'unitPriceMinor', width: 150, render: (value: number) => `¥${(value / 100).toFixed(2)}` }]} /> }} columns={[
      { title: '版本名称', dataIndex: 'name' },
      { title: '生效日期', dataIndex: 'effectiveFrom', width: 140 },
      { title: '定价条目', key: 'count', width: 100, render: (_, book) => book.lines.length + book.productLines.length },
      { title: '状态', dataIndex: 'status', width: 120, render: (value: string) => <Tag color={value === 'PUBLISHED' ? 'green' : value === 'DRAFT' ? 'orange' : 'default'}>{value === 'PUBLISHED' ? '已发布' : value === 'DRAFT' ? '草稿' : '已取消'}</Tag> },
      { title: '操作', key: 'actions', width: 240, render: (_, book) => book.status === 'DRAFT' ? <Space><Button type="link" icon={<EditOutlined />} onClick={() => openEdit(book)}>编辑</Button><Button type="link" icon={<CheckCircleOutlined />} loading={publish.isPending} onClick={() => publish.mutate(book.id)}>发布</Button><Popconfirm title="确认取消该草稿？" description="取消后不能恢复，但会保留历史记录。" onConfirm={() => cancel.mutate(book)}><Button type="link" danger icon={<StopOutlined />} loading={cancel.isPending}>取消</Button></Popconfirm></Space> : <Typography.Text type="secondary">只读</Typography.Text> },
    ]} /></Card>
    <Modal title={editing ? '编辑价格草稿' : '新建价格版本'} width={700} open={open} onCancel={() => { setOpen(false); setEditing(undefined) }} onOk={() => form.submit()} confirmLoading={save.isPending} okText="保存草稿" destroyOnHidden>
      {save.error && <Alert type="error" showIcon title={save.error instanceof ApiError ? save.error.message : '保存失败'} className="modal-alert" />}
      <Alert type="info" showIcon title="服务与商品不绑定：可以只调整服务、只调整商品或混合调整。未勾选但已有价格的条目会自动继承；从未定价的条目保持暂不可销售。" className="modal-alert" />
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} initialValues={{ effectiveFrom: dayjs(), serviceSelected: {}, productSelected: {}, prices: {}, productPrices: {} }}>
        <Space size={16} align="start" className="full-width"><Form.Item name="name" label="版本名称" rules={[{ required: true, message: '请输入版本名称' }]} className="grow"><Input placeholder="例如：2026年秋季标准价" /></Form.Item><Form.Item name="effectiveFrom" label="生效日期" rules={[{ required: true }]}><DatePicker /></Form.Item></Space>
        <Form.Item noStyle shouldUpdate>{({ getFieldValue }) => {
          const selectedServices = getFieldValue('serviceSelected') ?? {}; const selectedProducts = getFieldValue('productSelected') ?? {}
          return <><Typography.Title level={5}>服务项目标准价格（按需勾选）</Typography.Title><div className="price-entry-list">{items.data?.map((item) => { const current = currentBook?.lines.find((line) => line.serviceItemId === item.id)?.unitPriceMinor; return <div key={item.id}><span><Form.Item name={['serviceSelected', item.id]} valuePropName="checked" noStyle><Checkbox><strong>{item.name}</strong></Checkbox></Form.Item><small>{item.code} · {item.standardDurationMinutes}分钟 · 当前 {current === undefined ? '未定价' : `¥${(current / 100).toFixed(2)}`}</small></span><Form.Item name={['prices', item.id]} rules={[{ validator: async (_, value) => { if (selectedServices[item.id] && (value === undefined || value === null)) throw new Error('已勾选，请输入价格') } }]}><InputNumber min={0} precision={2} prefix="¥" disabled={!selectedServices[item.id]} /></Form.Item></div>})}</div>{Boolean(products.data?.length) && <><Typography.Title level={5} className="price-section-title">产品标准价格（按需勾选）</Typography.Title><div className="price-entry-list">{products.data?.map((item) => { const current = currentBook?.productLines.find((line) => line.productItemId === item.id)?.unitPriceMinor; return <div key={item.id}><span><Form.Item name={['productSelected', item.id]} valuePropName="checked" noStyle><Checkbox><strong>{item.name}</strong></Checkbox></Form.Item><small>{item.code} · 单位：{item.unitName} · 当前 {current === undefined ? '未定价' : `¥${(current / 100).toFixed(2)}`}</small></span><Form.Item name={['productPrices', item.id]} rules={[{ validator: async (_, value) => { if (selectedProducts[item.id] && (value === undefined || value === null)) throw new Error('已勾选，请输入价格') } }]}><InputNumber min={0} precision={2} prefix="¥" disabled={!selectedProducts[item.id]} /></Form.Item></div>})}</div></>}</>
        }}</Form.Item>
      </Form>
    </Modal>
  </div>
}
