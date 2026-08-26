import {
  CheckCircleOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  PlusOutlined,
  SearchOutlined,
  StopOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Checkbox,
  DatePicker,
  Descriptions,
  Drawer,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { PriceBook, ProductItem, ServiceItem } from '../api/types'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

interface PriceForm {
  name: string
  effectiveFrom: Dayjs
  serviceSelected: Record<string, boolean>
  productSelected: Record<string, boolean>
  prices: Record<string, number>
  productPrices: Record<string, number>
}
interface CopyForm { name: string; effectiveFrom: Dayjs }
interface ReasonForm { reason: string }
type DateRange = [Dayjs | null, Dayjs | null] | null
type SensitiveAction = { type: 'delete' | 'retire'; book: PriceBook }

const statusLabel = (status: string) => status === 'PUBLISHED' ? '已发布' : status === 'DRAFT' ? '草稿' : '已停用'
const statusColor = (status: string) => status === 'PUBLISHED' ? 'green' : status === 'DRAFT' ? 'orange' : 'default'
const errorMessage = (error: unknown, fallback: string) => error instanceof ApiError ? error.message : fallback

export function PriceBooksPage() {
  const { can } = useAuthorization()
  const canManage = can(Permission.PricePublish)
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<PriceBook>()
  const [detailId, setDetailId] = useState<string>()
  const [copySource, setCopySource] = useState<PriceBook>()
  const [sensitiveAction, setSensitiveAction] = useState<SensitiveAction>()
  const [queryText, setQueryText] = useState('')
  const [status, setStatus] = useState<string>()
  const [dateRange, setDateRange] = useState<DateRange>(null)
  const appliedQuery = useDebouncedValue(queryText.trim())
  const [form] = Form.useForm<PriceForm>()
  const [copyForm] = Form.useForm<CopyForm>()
  const [reasonForm] = Form.useForm<ReasonForm>()
  const effectiveFrom = Form.useWatch('effectiveFrom', form)

  const items = useQuery({
    queryKey: ['service-items'],
    queryFn: () => apiRequest<ServiceItem[]>('/api/v1/catalog/service-items'),
  })
  const products = useQuery({
    queryKey: ['product-items'],
    queryFn: () => apiRequest<ProductItem[]>('/api/v1/catalog/products'),
  })
  const priceBookPath = useMemo(() => {
    const params = new URLSearchParams()
    if (appliedQuery) params.set('query', appliedQuery)
    if (status) params.set('status', status)
    if (dateRange?.[0]) params.set('effectiveFrom', dateRange[0].format('YYYY-MM-DD'))
    if (dateRange?.[1]) params.set('effectiveTo', dateRange[1].format('YYYY-MM-DD'))
    return `/api/v1/catalog/price-books${params.size ? `?${params.toString()}` : ''}`
  }, [appliedQuery, status, dateRange])
  const books = useQuery({
    queryKey: ['price-books', appliedQuery, status, dateRange?.[0]?.format('YYYY-MM-DD'), dateRange?.[1]?.format('YYYY-MM-DD')],
    queryFn: () => apiRequest<PriceBook[]>(priceBookPath),
  })
  const detail = useQuery({
    queryKey: ['price-book', detailId],
    queryFn: () => apiRequest<PriceBook>(`/api/v1/catalog/price-books/${detailId}`),
    enabled: Boolean(detailId),
  })
  const targetDate = (effectiveFrom ?? dayjs()).format('YYYY-MM-DD')
  const currentBook = useMemo(() => books.data
    ?.filter((book) => book.status === 'PUBLISHED' && book.effectiveFrom <= targetDate)
    .sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom)
      || (b.publishedAtUtc ?? '').localeCompare(a.publishedAtUtc ?? ''))[0], [books.data, targetDate])
  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['price-books'] })
    await queryClient.invalidateQueries({ queryKey: ['price-book'] })
  }

  const save = useMutation({
    mutationFn: (values: PriceForm) => {
      const body = {
        name: values.name,
        effectiveFrom: values.effectiveFrom.format('YYYY-MM-DD'),
        lines: Object.entries(values.serviceSelected ?? {}).filter(([, selected]) => selected)
          .map(([serviceItemId]) => ({ serviceItemId, unitPriceMinor: Math.round(values.prices[serviceItemId] * 100) })),
        productLines: Object.entries(values.productSelected ?? {}).filter(([, selected]) => selected)
          .map(([productItemId]) => ({ productItemId, unitPriceMinor: Math.round(values.productPrices[productItemId] * 100) })),
        expectedVersion: editing?.version,
      }
      return apiRequest<PriceBook>(editing ? `/api/v1/catalog/price-books/${editing.id}` : '/api/v1/catalog/price-books', {
        method: editing ? 'PUT' : 'POST', body: JSON.stringify(body),
      })
    },
    onSuccess: async () => {
      message.success(editing ? '价格草稿已更新' : '价格草稿已创建；未勾选项目已继承当前生效价格')
      setOpen(false); setEditing(undefined); form.resetFields(); await refresh()
    },
  })
  const publish = useMutation({
    mutationFn: (id: string) => apiRequest<PriceBook>(`/api/v1/catalog/price-books/${id}/publish`, { method: 'POST' }),
    onSuccess: async () => { message.success('价格版本已发布'); await refresh() },
    onError: (error) => message.error(errorMessage(error, '发布失败')),
  })
  const copy = useMutation({
    mutationFn: (values: CopyForm) => apiRequest<PriceBook>(`/api/v1/catalog/price-books/${copySource?.id}/copies`, {
      method: 'POST', body: JSON.stringify({ name: values.name, effectiveFrom: values.effectiveFrom.format('YYYY-MM-DD') }),
    }),
    onSuccess: async () => { message.success('已复制为新草稿'); setCopySource(undefined); copyForm.resetFields(); await refresh() },
  })
  const remove = useMutation({
    mutationFn: ({ book, reason }: { book: PriceBook; reason: string }) => apiRequest<void>(`/api/v1/catalog/price-books/${book.id}`, {
      method: 'DELETE', body: JSON.stringify({ expectedVersion: book.version, reason }),
    }),
    onSuccess: async () => { message.success('价格版本已删除'); closeSensitiveAction(); await refresh() },
  })
  const retire = useMutation({
    mutationFn: ({ book, reason }: { book: PriceBook; reason: string }) => apiRequest<PriceBook>(`/api/v1/catalog/price-books/${book.id}/retire`, {
      method: 'POST', body: JSON.stringify({ expectedVersion: book.version, reason }),
    }),
    onSuccess: async () => { message.success('价格版本已停用'); closeSensitiveAction(); await refresh() },
  })

  const closeSensitiveAction = () => { setSensitiveAction(undefined); reasonForm.resetFields() }
  const openCreate = () => {
    setEditing(undefined); form.resetFields()
    form.setFieldsValue({ effectiveFrom: dayjs(), serviceSelected: {}, productSelected: {}, prices: {}, productPrices: {} })
    setOpen(true)
  }
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
  const openCopy = (book: PriceBook) => {
    setCopySource(book)
    copyForm.setFieldsValue({ name: `${book.name}（副本）`.slice(0, 120), effectiveFrom: dayjs() })
  }
  const submitSensitiveAction = (values: ReasonForm) => {
    if (!sensitiveAction) return
    const payload = { book: sensitiveAction.book, reason: values.reason }
    if (sensitiveAction.type === 'delete') remove.mutate(payload)
    else retire.mutate(payload)
  }
  const priceLines = (book?: PriceBook) => book ? [
    ...book.lines.map((line) => ({ key: `service-${line.serviceItemId}`, type: '服务项目', name: line.serviceItemName, unit: '次', unitPriceMinor: line.unitPriceMinor })),
    ...book.productLines.map((line) => ({ key: `product-${line.productItemId}`, type: '产品', name: line.productItemName, unit: line.unitName, unitPriceMinor: line.unitPriceMinor })),
  ] : []
  const lineColumns = [
    { title: '类型', dataIndex: 'type', width: 110, render: (value: string) => <Tag color={value === '产品' ? 'blue' : 'cyan'}>{value}</Tag> },
    { title: '目录名称', dataIndex: 'name' },
    { title: '单位', dataIndex: 'unit', width: 100 },
    { title: '标准价格', dataIndex: 'unitPriceMinor', width: 150, render: (value: number) => `¥${(value / 100).toFixed(2)}` },
  ]

  return <div className="page-stack">
    <div className="page-heading">
      <div><Typography.Title level={2}>价格版本</Typography.Title><Typography.Paragraph>集中管理服务与商品标准价，支持查询、查看、新建、草稿编辑、复制、停用和删除。</Typography.Paragraph></div>
      {canManage && <Button type="primary" icon={<PlusOutlined />} onClick={openCreate} disabled={!items.data?.length && !products.data?.length}>新建价格版本</Button>}
    </div>
    {!items.isLoading && !products.isLoading && !items.data?.length && !products.data?.length && <Alert type="info" showIcon title="请先创建服务项目或产品，再建立价格版本。" />}
    <Card variant="borderless">
      <Space wrap size={12} className="table-toolbar">
        <Input allowClear prefix={<SearchOutlined />} placeholder="输入版本名称自动查询" value={queryText} onChange={(event) => setQueryText(event.target.value)} maxLength={100} style={{ width: 260 }} />
        <Select allowClear placeholder="全部状态" value={status} onChange={setStatus} style={{ width: 150 }} options={[
          { value: 'DRAFT', label: '草稿' }, { value: 'PUBLISHED', label: '已发布' }, { value: 'RETIRED', label: '已停用' },
        ]} />
        <DatePicker.RangePicker value={dateRange} onChange={(value) => setDateRange(value ? [value[0], value[1]] : null)} />
        {(queryText || status || dateRange) && <Button onClick={() => { setQueryText(''); setStatus(undefined); setDateRange(null) }}>清空筛选</Button>}
      </Space>
      {books.error && <Alert type="error" showIcon title={errorMessage(books.error, '价格版本加载失败')} className="modal-alert" />}
      <Table<PriceBook>
        rowKey="id" loading={books.isLoading} dataSource={books.data ?? []} scroll={{ x: 1040 }}
        expandable={{ expandedRowRender: (book) => <Table rowKey="key" size="small" pagination={false} dataSource={priceLines(book)} columns={lineColumns} /> }}
        columns={[
          { title: '版本名称', dataIndex: 'name' },
          { title: '生效日期', dataIndex: 'effectiveFrom', width: 140 },
          { title: '定价条目', key: 'count', width: 100, render: (_, book) => book.lines.length + book.productLines.length },
          { title: '状态', dataIndex: 'status', width: 110, render: (value: string) => <Tag color={statusColor(value)}>{statusLabel(value)}</Tag> },
          { title: '操作', key: 'actions', width: 430, fixed: 'right', render: (_, book) => <Space size={4} wrap>
            <Button type="link" icon={<EyeOutlined />} onClick={() => setDetailId(book.id)}>查看</Button>
            {canManage && book.status === 'DRAFT' && <Button type="link" icon={<EditOutlined />} onClick={() => openEdit(book)}>编辑</Button>}
            {canManage && book.status === 'DRAFT' && <Popconfirm title="确认发布该价格版本？" description="发布后不能直接编辑，但仍可复制或删除。" onConfirm={() => publish.mutate(book.id)}><Button type="link" icon={<CheckCircleOutlined />} loading={publish.isPending}>发布</Button></Popconfirm>}
            {canManage && <Button type="link" icon={<CopyOutlined />} onClick={() => openCopy(book)}>复制</Button>}
            {canManage && book.status === 'PUBLISHED' && <Button type="link" icon={<StopOutlined />} onClick={() => setSensitiveAction({ type: 'retire', book })}>停用</Button>}
            {canManage && <Button type="link" danger icon={<DeleteOutlined />} onClick={() => setSensitiveAction({ type: 'delete', book })}>删除</Button>}
          </Space> },
        ]}
      />
    </Card>

    <Drawer title="价格版本详情" width={720} open={Boolean(detailId)} onClose={() => setDetailId(undefined)}>
      {detail.error && <Alert type="error" showIcon title={errorMessage(detail.error, '详情加载失败')} />}
      {detail.data && <>
        <Descriptions bordered size="small" column={2}>
          <Descriptions.Item label="版本名称">{detail.data.name}</Descriptions.Item>
          <Descriptions.Item label="状态"><Tag color={statusColor(detail.data.status)}>{statusLabel(detail.data.status)}</Tag></Descriptions.Item>
          <Descriptions.Item label="生效日期">{detail.data.effectiveFrom}</Descriptions.Item>
          <Descriptions.Item label="条目数量">{detail.data.lines.length + detail.data.productLines.length}</Descriptions.Item>
          <Descriptions.Item label="版本标识" span={2}><Typography.Text copyable>{detail.data.id}</Typography.Text></Descriptions.Item>
        </Descriptions>
        <Table rowKey="key" size="small" pagination={false} dataSource={priceLines(detail.data)} columns={lineColumns} style={{ marginTop: 20 }} />
      </>}
    </Drawer>

    <Modal title={editing ? '编辑价格草稿' : '新建价格版本'} width={700} open={open} onCancel={() => { setOpen(false); setEditing(undefined) }} onOk={() => form.submit()} confirmLoading={save.isPending} okText="保存草稿" destroyOnHidden>
      {save.error && <Alert type="error" showIcon title={errorMessage(save.error, '保存失败')} className="modal-alert" />}
      <Alert type="info" showIcon title="服务与商品不绑定。新建时未勾选但已有价格的条目会自动继承；编辑草稿时取消勾选会从该草稿移除。" className="modal-alert" />
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} initialValues={{ effectiveFrom: dayjs(), serviceSelected: {}, productSelected: {}, prices: {}, productPrices: {} }}>
        <Space size={16} align="start" className="full-width"><Form.Item name="name" label="版本名称" rules={[{ required: true, message: '请输入版本名称' }, { max: 120 }]} className="grow"><Input placeholder="例如：秋季标准价" /></Form.Item><Form.Item name="effectiveFrom" label="生效日期" rules={[{ required: true }]}><DatePicker /></Form.Item></Space>
        <Form.Item noStyle shouldUpdate>{({ getFieldValue }) => {
          const selectedServices = getFieldValue('serviceSelected') ?? {}; const selectedProducts = getFieldValue('productSelected') ?? {}
          return <><Typography.Title level={5}>服务项目标准价格（按需勾选）</Typography.Title><div className="price-entry-list">{items.data?.map((item) => { const current = currentBook?.lines.find((line) => line.serviceItemId === item.id)?.unitPriceMinor; return <div key={item.id}><span><Form.Item name={['serviceSelected', item.id]} valuePropName="checked" noStyle><Checkbox><strong>{item.name}</strong></Checkbox></Form.Item><small>{item.code} · {item.standardDurationMinutes}分钟 · 当前 {current === undefined ? '未定价' : `¥${(current / 100).toFixed(2)}`}</small></span><Form.Item name={['prices', item.id]} rules={[{ validator: async (_, value) => { if (selectedServices[item.id] && (value === undefined || value === null)) throw new Error('已勾选，请输入价格') } }]}><InputNumber min={0} precision={2} prefix="¥" disabled={!selectedServices[item.id]} /></Form.Item></div>})}</div>{Boolean(products.data?.length) && <><Typography.Title level={5} className="price-section-title">产品标准价格（按需勾选）</Typography.Title><div className="price-entry-list">{products.data?.map((item) => { const current = currentBook?.productLines.find((line) => line.productItemId === item.id)?.unitPriceMinor; return <div key={item.id}><span><Form.Item name={['productSelected', item.id]} valuePropName="checked" noStyle><Checkbox><strong>{item.name}</strong></Checkbox></Form.Item><small>{item.code} · 单位：{item.unitName} · 当前 {current === undefined ? '未定价' : `¥${(current / 100).toFixed(2)}`}</small></span><Form.Item name={['productPrices', item.id]} rules={[{ validator: async (_, value) => { if (selectedProducts[item.id] && (value === undefined || value === null)) throw new Error('已勾选，请输入价格') } }]}><InputNumber min={0} precision={2} prefix="¥" disabled={!selectedProducts[item.id]} /></Form.Item></div>})}</div></>}</>
        }}</Form.Item>
      </Form>
    </Modal>

    <Modal title="复制为新价格草稿" open={Boolean(copySource)} onCancel={() => setCopySource(undefined)} onOk={() => copyForm.submit()} okText="创建草稿" confirmLoading={copy.isPending} destroyOnHidden>
      {copy.error && <Alert type="error" showIcon title={errorMessage(copy.error, '复制失败')} className="modal-alert" />}
      <Alert type="info" showIcon title="将完整复制服务与产品定价，原版本不会变化。" className="modal-alert" />
      <Form form={copyForm} layout="vertical" onFinish={(values) => copy.mutate(values)}><Form.Item name="name" label="新版本名称" rules={[{ required: true, message: '请输入新版本名称' }, { max: 120 }]}><Input /></Form.Item><Form.Item name="effectiveFrom" label="生效日期" rules={[{ required: true, message: '请选择生效日期' }]}><DatePicker /></Form.Item></Form>
    </Modal>

    <Modal title={sensitiveAction?.type === 'delete' ? '删除价格版本' : '停用已发布价格版本'} open={Boolean(sensitiveAction)} onCancel={closeSensitiveAction} onOk={() => reasonForm.submit()} okText={sensitiveAction?.type === 'delete' ? '确认删除' : '确认停用'} okButtonProps={{ danger: true }} confirmLoading={remove.isPending || retire.isPending} destroyOnHidden>
      <Alert type="warning" showIcon title={sensitiveAction?.type === 'delete' ? '草稿或已发布版本都可删除；历史订单保留成交明细，但不再显示该价格版本。' : '停用后不再用于新订单，但仍保留该版本。'} className="modal-alert" />
      {(remove.error || retire.error) && <Alert type="error" showIcon title={errorMessage(remove.error || retire.error, '操作失败')} className="modal-alert" />}
      <Form form={reasonForm} layout="vertical" onFinish={submitSensitiveAction}><Form.Item name="reason" label="操作原因" rules={[{ required: true, message: '请输入操作原因' }, { min: 2 }, { max: 200 }]}><Input.TextArea rows={3} showCount maxLength={200} /></Form.Item></Form>
    </Modal>
  </div>
}
