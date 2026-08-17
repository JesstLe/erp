import { DeleteOutlined, FileDoneOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Descriptions, Drawer, Empty, Form, Input, InputNumber, Modal, Select, Space, Statistic, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { CashierVisit, CustomerSummary, PriceBook, ServiceOrder } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface OrderLineValues { serviceItemId: string; quantity: number; actualMinutes?: number; enteredPriceYuan: number; priceOverrideReason?: string }
interface OrderValues { source: 'standalone' | 'visit'; visitId?: string; customerId?: string; note?: string; lines: OrderLineValues[] }

const statusMeta: Record<string, { label: string; color: string }> = {
  Draft: { label: '待确认金额', color: 'gold' }, PendingPayment: { label: '待支付', color: 'blue' },
  Settled: { label: '已结算', color: 'green' }, Voided: { label: '已作废', color: 'default' },
}
function money(minor: number) { return `¥${(minor / 100).toFixed(2)}` }
function duration(seconds?: number) { if (seconds === undefined || seconds === null) return '未填写'; const minutes = Math.floor(seconds / 60); return `${Math.floor(minutes / 60)}小时${minutes % 60}分钟` }
function commandId() { return crypto.randomUUID() }

export function CashierPage() {
  const auth = useAuth(); const storeId = auth.store?.id; const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false); const [selectedId, setSelectedId] = useState<string>(); const [form] = Form.useForm<OrderValues>()
  const orders = useQuery({ queryKey: ['cashier-orders', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<ServiceOrder[]>(`/api/v1/cashier/orders?storeId=${storeId}`) })
  const pendingVisits = useQuery({ queryKey: ['cashier-visits', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<CashierVisit[]>(`/api/v1/cashier/pending-visits?storeId=${storeId}`) })
  const priceBooks = useQuery({ queryKey: ['price-books'], queryFn: () => apiRequest<PriceBook[]>('/api/v1/catalog/price-books') })
  const customers = useQuery({ queryKey: ['customers', storeId, 'cashier'], enabled: Boolean(storeId), queryFn: () => apiRequest<CustomerSummary[]>(`/api/v1/customers?storeId=${storeId}&query=`) })
  const selected = useQuery({ queryKey: ['cashier-order', storeId, selectedId], enabled: Boolean(storeId && selectedId), queryFn: () => apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${selectedId}?storeId=${storeId}`) })
  const publishedBook = useMemo(() => priceBooks.data?.filter((book) => book.status === 'PUBLISHED').sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))[0], [priceBooks.data])
  const refresh = async () => Promise.all([queryClient.invalidateQueries({ queryKey: ['cashier-orders', storeId] }), queryClient.invalidateQueries({ queryKey: ['cashier-visits', storeId] })])
  const onError = (error: unknown) => message.error(error instanceof ApiError ? error.message : '操作失败')
  const create = useMutation({ mutationFn: (values: OrderValues) => apiRequest<ServiceOrder>('/api/v1/cashier/orders', { method: 'POST', body: JSON.stringify({ storeId, visitId: values.source === 'visit' ? values.visitId : null, customerId: values.customerId || null, note: values.note, commandId: commandId(), lines: values.lines.map((line) => ({ serviceItemId: line.serviceItemId, quantity: line.quantity, actualSeconds: line.actualMinutes === undefined ? null : Math.round(line.actualMinutes * 60), enteredPriceMinor: Math.round(line.enteredPriceYuan * 100), priceOverrideReason: line.priceOverrideReason })) }) }), onSuccess: async (result) => { message.success('消费单草稿已创建，尚未收款'); setCreateOpen(false); form.resetFields(); setSelectedId(result.id); await refresh() }, onError })
  const confirm = useMutation({ mutationFn: (order: ServiceOrder) => apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${order.id}/confirm`, { method: 'POST', body: JSON.stringify({ storeId, expectedVersion: order.version, commandId: commandId() }) }), onSuccess: async (result) => { message.success('金额已确认，消费单进入待支付'); await Promise.all([refresh(), queryClient.setQueryData(['cashier-order', storeId, result.id], result)]) }, onError })
  const openCreate = () => { const first = publishedBook?.lines[0]; form.setFieldsValue({ source: 'standalone', lines: first ? [{ serviceItemId: first.serviceItemId, quantity: 1, enteredPriceYuan: first.unitPriceMinor / 100 }] : [] }); setCreateOpen(true) }
  const selectedVisitId = Form.useWatch('visitId', form)
  const selectedVisit = pendingVisits.data?.find((visit) => visit.id === selectedVisitId)

  const columns = [
    { title: '消费单', dataIndex: 'orderNo', render: (value: string) => <strong>{value}</strong> },
    { title: '状态', dataIndex: 'status', render: (value: string) => { const meta = statusMeta[value] ?? { label: value, color: 'default' }; return <Tag color={meta.color}>{meta.label}</Tag> } },
    { title: '标准价合计', dataIndex: 'referenceAmountMinor', align: 'right' as const, render: money },
    { title: '应收金额', dataIndex: 'receivableMinor', align: 'right' as const, render: (value: number) => <strong>{money(value)}</strong> },
    { title: '录单时间', dataIndex: 'createdAtUtc', render: (value: string) => new Date(value).toLocaleString('zh-CN', { hour12: false }) },
  ]

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>服务录单与收银</Typography.Title><Typography.Paragraph>店长按实际服务内容和成交金额录单；设施占用时长只作接待参考。</Typography.Paragraph></div><Button type="primary" icon={<PlusOutlined />} onClick={openCreate} disabled={!publishedBook}>新建消费单</Button></div>
    <Alert type="info" showIcon title="当前模块只完成消费单与金额确认；“待支付”不代表微信、支付宝或会员卡已经到账。" />
    <div className="cashier-metrics"><Card variant="borderless"><Statistic title="待录单接待" value={pendingVisits.data?.length ?? 0} suffix="单" /></Card><Card variant="borderless"><Statistic title="待确认金额" value={orders.data?.filter((order) => order.status === 'Draft').length ?? 0} suffix="单" /></Card><Card variant="borderless"><Statistic title="待支付" value={orders.data?.filter((order) => order.status === 'PendingPayment').length ?? 0} suffix="单" /></Card></div>
    {!publishedBook && !priceBooks.isLoading && <Alert type="warning" showIcon title="当前没有已发布价目表，请先由最高权限账号发布价格。" />}
    <Card variant="borderless" className="table-card"><Table<ServiceOrder> rowKey="id" columns={columns} dataSource={orders.data} loading={orders.isLoading} pagination={{ pageSize: 10 }} locale={{ emptyText: <Empty description="还没有消费单" /> }} onRow={(record) => ({ onClick: () => setSelectedId(record.id), className: 'clickable-row' })} /></Card>

    <Modal title="新建消费单" width={900} open={createOpen} onCancel={() => setCreateOpen(false)} onOk={() => form.submit()} okText="保存草稿" confirmLoading={create.isPending} destroyOnHidden>
      <Alert type="warning" showIcon title="价格由有权限的店长输入。若成交价不同于已发布标准价，必须填写改价原因。" className="modal-alert" />
      <Form<OrderValues> form={form} layout="vertical" onFinish={(values) => create.mutate(values)}>
        <Form.Item name="source" label="录单来源" rules={[{ required: true }]}><Select options={[{ value: 'standalone', label: '服务结束后直接补录' }, { value: 'visit', label: '从已结束的设施接待录入' }]} /></Form.Item>
        <Form.Item noStyle shouldUpdate={(before, after) => before.source !== after.source}>{({ getFieldValue }) => getFieldValue('source') === 'visit' ? <><Form.Item name="visitId" label="已结束接待" rules={[{ required: true, message: '请选择接待记录' }]}><Select placeholder="选择接待单" options={pendingVisits.data?.map((visit) => ({ value: visit.id, label: `${visit.visitNo} · 设施占用 ${duration(visit.facilitySeconds)}` }))} /></Form.Item>{selectedVisit && <Alert type="info" showIcon title={`设施累计占用 ${duration(selectedVisit.facilitySeconds)}，该时长不会自动计算费用。`} className="modal-alert" />}</> : null}</Form.Item>
        <Form.Item name="customerId" label="顾客/会员（可选）"><Select allowClear showSearch optionFilterProp="label" placeholder="可按顾客档案关联，也可匿名结算" options={customers.data?.map((customer) => ({ value: customer.id, label: `${customer.displayName} · ${customer.maskedMobile}` }))} /></Form.Item>
        <Typography.Title level={5}>服务项目</Typography.Title>
        <Form.List name="lines" rules={[{ validator: async (_, lines) => { if (!lines?.length) throw new Error('至少添加一个服务项目') } }]}>{(fields, { add, remove }, { errors }) => <><div className="order-line-list">{fields.map((field) => <OrderLineEditor key={field.key} field={field} form={form} priceBook={publishedBook} onRemove={() => remove(field.name)} removable={fields.length > 1} />)}</div><Space><Button icon={<PlusOutlined />} onClick={() => { const first = publishedBook?.lines[0]; add(first ? { serviceItemId: first.serviceItemId, quantity: 1, enteredPriceYuan: first.unitPriceMinor / 100 } : {}) }}>添加项目</Button><Form.ErrorList errors={errors} /></Space></>}</Form.List>
        <Form.Item name="note" label="整单备注（可选）" rules={[{ max: 1000 }]}><Input.TextArea rows={2} maxLength={1000} showCount /></Form.Item>
      </Form>
    </Modal>

    <Drawer title="消费单详情" width={680} open={Boolean(selectedId)} onClose={() => setSelectedId(undefined)} extra={selected.data?.status === 'Draft' && <Button type="primary" icon={<FileDoneOutlined />} loading={confirm.isPending} onClick={() => confirm.mutate(selected.data!)}>确认金额</Button>}>
      {selected.error && <Alert type="error" showIcon title={selected.error instanceof Error ? selected.error.message : '详情加载失败'} />}
      {selected.data && <Space orientation="vertical" size={18} className="full-width"><Alert type={selected.data.status === 'PendingPayment' ? 'warning' : 'info'} showIcon title={selected.data.status === 'PendingPayment' ? '金额已锁定，仍需完成真实支付后才能结算。' : '草稿可核对金额；确认后进入待支付。'} /><Descriptions bordered size="small" column={2} items={[{ key: 'no', label: '消费单号', children: selected.data.orderNo }, { key: 'status', label: '状态', children: statusMeta[selected.data.status]?.label ?? selected.data.status }, { key: 'reference', label: '标准价合计', children: money(selected.data.referenceAmountMinor) }, { key: 'receivable', label: '应收金额', children: <strong>{money(selected.data.receivableMinor)}</strong> }, { key: 'note', label: '备注', span: 2, children: selected.data.note ?? '无' }]} />
        <div><Typography.Title level={5}>项目明细与价格快照</Typography.Title>{selected.data.lines.map((line) => <Card key={line.id} size="small" className="order-detail-line"><div><strong>{line.itemName}</strong><Tag>{line.itemCode}</Tag></div><div className="order-detail-grid"><span>数量 × {line.quantity}</span><span>实际服务 {duration(line.actualSeconds)}</span><span>标准价 {money(line.referencePriceMinor)}</span><strong>成交价 {money(line.enteredPriceMinor)}</strong></div>{line.priceOverrideReason && <Typography.Text type="warning">改价原因：{line.priceOverrideReason}</Typography.Text>}</Card>)}</div>
      </Space>}
    </Drawer>
  </div>
}

function OrderLineEditor({ field, form, priceBook, onRemove, removable }: { field: { key: number; name: number }; form: ReturnType<typeof Form.useForm<OrderValues>>[0]; priceBook?: PriceBook; onRemove: () => void; removable: boolean }) {
  const itemId = Form.useWatch(['lines', field.name, 'serviceItemId'], form)
  const entered = Form.useWatch(['lines', field.name, 'enteredPriceYuan'], form)
  const standardMinor = priceBook?.lines.find((line) => line.serviceItemId === itemId)?.unitPriceMinor
  const changed = standardMinor !== undefined && Math.round(Number(entered ?? 0) * 100) !== standardMinor
  return <Card size="small" className="order-line-editor" extra={removable && <Button type="text" danger icon={<DeleteOutlined />} onClick={onRemove} aria-label="删除项目" />}>
    <div className="order-line-fields"><Form.Item name={[field.name, 'serviceItemId']} label="服务项目" rules={[{ required: true }]}><Select options={priceBook?.lines.map((line) => ({ value: line.serviceItemId, label: `${line.serviceItemName} · 标准价 ${money(line.unitPriceMinor)}` }))} onChange={(value) => { const price = priceBook?.lines.find((line) => line.serviceItemId === value)?.unitPriceMinor ?? 0; form.setFieldValue(['lines', field.name, 'enteredPriceYuan'], price / 100) }} /></Form.Item><Form.Item name={[field.name, 'quantity']} label="数量" rules={[{ required: true }, { type: 'number', min: 1, max: 999 }]}><InputNumber min={1} max={999} precision={0} /></Form.Item><Form.Item name={[field.name, 'actualMinutes']} label="实际时长（分钟，可选）" rules={[{ type: 'number', min: 0, max: 1440 }]}><InputNumber min={0} max={1440} precision={0} /></Form.Item><Form.Item name={[field.name, 'enteredPriceYuan']} label="成交单价（元）" rules={[{ required: true }, { type: 'number', min: 0, max: 100000000 }]}><InputNumber min={0} max={100000000} precision={2} prefix="¥" /></Form.Item></div>
    <Typography.Text type="secondary">标准单价：{standardMinor === undefined ? '请选择项目' : money(standardMinor)}。项目实际时长仅记录，不参与金额计算。</Typography.Text>
    {changed && <Form.Item name={[field.name, 'priceOverrideReason']} label="改价原因" rules={[{ required: true, message: '成交价与标准价不同时必须填写原因' }, { min: 2 }, { max: 500 }]}><Input maxLength={500} placeholder="例如：经负责人确认的现场增减项" /></Form.Item>}
  </Card>
}
