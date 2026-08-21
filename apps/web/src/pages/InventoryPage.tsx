import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Form, Input, InputNumber, Modal, Select, Space, Statistic, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { ApiError, apiRequest } from '../api/client'
import type { InventoryBalance, InventoryDocument, InventoryMovement, PageResult } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

interface DocumentValues { documentType: string; reason: string; lines: { productItemId: string; quantity: number }[] }
const movementLabels: Record<string, string> = { Opening: '期初入库', Receipt: '采购/收货入库', SaleIssue: '销售出库', SalesReturn: '销售退货入库', AdjustmentIn: '盘盈入库', AdjustmentOut: '盘亏出库' }
const documentTypes = [{ value: 'Opening', label: '期初库存' }, { value: 'Receipt', label: '收货入库' }, { value: 'AdjustmentIn', label: '盘盈调整' }, { value: 'AdjustmentOut', label: '盘亏调整' }]
function commandId() { return crypto.randomUUID() }

export function InventoryPage() {
  const auth = useAuth(); const { can } = useAuthorization(); const storeId = auth.store?.id; const owner = can(Permission.InventoryWrite)
  const queryClient = useQueryClient(); const [open, setOpen] = useState(false); const [form] = Form.useForm<DocumentValues>()
  const [movementPage, setMovementPage] = useState(1); const [documentPage, setDocumentPage] = useState(1)
  const movementPageSize = 10; const documentPageSize = 5
  const balances = useQuery({ queryKey: ['inventory-balances', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<InventoryBalance[]>(`/api/v1/inventory/balances?storeId=${storeId}`) })
  const movements = useQuery({ queryKey: ['inventory-movements', storeId, movementPage], enabled: Boolean(storeId), queryFn: () => apiRequest<PageResult<InventoryMovement>>(`/api/v1/inventory/movements?storeId=${storeId}&page=${movementPage}&pageSize=${movementPageSize}`) })
  const documents = useQuery({ queryKey: ['inventory-documents', storeId, documentPage], enabled: Boolean(storeId), queryFn: () => apiRequest<PageResult<InventoryDocument>>(`/api/v1/inventory/documents?storeId=${storeId}&page=${documentPage}&pageSize=${documentPageSize}`) })
  const tracked = useMemo(() => balances.data?.filter((item) => item.trackInventory) ?? [], [balances.data])
  const post = useMutation({ mutationFn: (values: DocumentValues) => apiRequest<InventoryDocument>('/api/v1/inventory/documents', { method: 'POST', body: JSON.stringify({ storeId, ...values, commandId: commandId() }) }), onSuccess: async () => { message.success('库存单据已过账，流水不可修改；如需纠正请新增反向调整单。'); setOpen(false); form.resetFields(); await Promise.all([queryClient.invalidateQueries({ queryKey: ['inventory-balances', storeId] }), queryClient.invalidateQueries({ queryKey: ['inventory-movements', storeId] }), queryClient.invalidateQueries({ queryKey: ['inventory-documents', storeId] })]) }, onError: (error) => message.error(error instanceof ApiError ? error.message : '库存单据过账失败') })
  const onOpen = (productItemId?: string) => { const first = tracked.find((item) => item.productItemId === productItemId) ?? tracked[0]; form.setFieldsValue({ documentType: 'Receipt', reason: '', lines: first ? [{ productItemId: first.productItemId, quantity: 1 }] : [] }); setOpen(true) }
  const onHand = tracked.reduce((sum, item) => sum + item.onHandQuantity, 0); const reserved = tracked.reduce((sum, item) => sum + item.reservedQuantity, 0)
  const balanceColumns = [
    { title: '产品', key: 'product', render: (_: unknown, item: InventoryBalance) => <div><strong>{item.productName}</strong><br /><Typography.Text type="secondary">{item.productCode} · {item.unitName}</Typography.Text></div> },
    { title: '账面库存', dataIndex: 'onHandQuantity', render: (value: number, item: InventoryBalance) => `${value} ${item.unitName}` },
    { title: '销售预占', dataIndex: 'reservedQuantity', render: (value: number, item: InventoryBalance) => <Tag color={value ? 'gold' : 'default'}>{value} {item.unitName}</Tag> },
    { title: '可用库存', dataIndex: 'availableQuantity', render: (value: number, item: InventoryBalance) => <strong>{value} {item.unitName}</strong> },
    ...(owner ? [{ title: '操作', key: 'action', width: 120, render: (_: unknown, item: InventoryBalance) => <Button size="small" icon={<EditOutlined />} onClick={() => onOpen(item.productItemId)}>调整数量</Button> }] : []),
  ]
  const movementColumns = [
    { title: '时间', dataIndex: 'occurredAtUtc', render: (value: string) => new Date(value).toLocaleString('zh-CN') },
    { title: '产品', key: 'product', render: (_: unknown, item: InventoryMovement) => `${item.productName} · ${item.productCode}` },
    { title: '类型', dataIndex: 'movementType', render: (value: string) => <Tag color={value === 'SaleIssue' || value === 'AdjustmentOut' ? 'orange' : 'green'}>{movementLabels[value] ?? value}</Tag> },
    { title: '数量', key: 'quantity', render: (_: unknown, item: InventoryMovement) => <strong>{item.direction === 'Out' ? '−' : '+'}{item.quantity} {item.unitName}</strong> },
    { title: '变动后', dataIndex: 'onHandAfter', render: (value: number, item: InventoryMovement) => `${value} ${item.unitName}` },
  ]
  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>商品库存</Typography.Title><Typography.Paragraph>数量不能直接覆盖：通过期初、入库、盘盈或盘亏形成可追溯变动；完成收款才销售出库。</Typography.Paragraph></div>{owner && <Button type="primary" icon={<PlusOutlined />} disabled={!tracked.length} onClick={() => onOpen()}>调整库存</Button>}</div>
    <Alert type="warning" showIcon title="库存流水过账后不可编辑或删除；录错时新增反向调整。产品退货只回库，不会自动发起资金退款。" />
    <div className="cashier-metrics"><Card variant="borderless"><Statistic title="跟踪库存产品" value={tracked.length} suffix="项" /></Card><Card variant="borderless"><Statistic title="账面总数量" value={onHand} /></Card><Card variant="borderless"><Statistic title="销售预占" value={reserved} /></Card></div>
    <Card variant="borderless" title="库存余额"><Table<InventoryBalance> rowKey="productItemId" columns={balanceColumns} dataSource={tracked} loading={balances.isLoading} pagination={false} locale={{ emptyText: <Empty description="暂无跟踪库存的产品" /> }} /></Card>
    <Card variant="borderless" title="库存流水" extra={<Typography.Text type="secondary">共 {movements.data?.total ?? 0} 条</Typography.Text>}><Table<InventoryMovement> rowKey="id" columns={movementColumns} dataSource={movements.data?.items} loading={movements.isLoading} pagination={{ current: movementPage, pageSize: movementPageSize, total: movements.data?.total ?? 0, showSizeChanger: false, showTotal: (total) => `共 ${total} 条`, onChange: setMovementPage }} locale={{ emptyText: <Empty description="尚无库存流水" /> }} /></Card>
    <Card variant="borderless" title="过账单据"><Table<InventoryDocument> rowKey="id" size="small" dataSource={documents.data?.items} loading={documents.isLoading} pagination={{ current: documentPage, pageSize: documentPageSize, total: documents.data?.total ?? 0, showSizeChanger: false, showTotal: (total) => `共 ${total} 张`, onChange: setDocumentPage }} columns={[{ title: '单号', dataIndex: 'documentNo' }, { title: '类型', dataIndex: 'documentType', render: (value: string) => documentTypes.find((item) => item.value === value)?.label ?? value }, { title: '原因', dataIndex: 'reason' }, { title: '明细', dataIndex: 'lines', render: (lines: InventoryDocument['lines']) => lines.map((line) => `${line.productName} × ${line.quantity}${line.unitName}`).join('、') }, { title: '过账时间', dataIndex: 'postedAtUtc', render: (value: string) => new Date(value).toLocaleString('zh-CN') }]} /></Card>
    <Modal title="调整库存数量" width={760} open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="确认调整并过账" confirmLoading={post.isPending} destroyOnHidden><Alert type="warning" showIcon title="这是不可逆的库存事实。请通过单据类型表达增加或减少；录错时新增反向调整单。" className="modal-alert" /><Form<DocumentValues> form={form} layout="vertical" onFinish={(values) => post.mutate(values)}><Form.Item name="documentType" label="调整类型" rules={[{ required: true }]}><Select options={documentTypes} /></Form.Item><Form.List name="lines" rules={[{ validator: async (_, lines) => { if (!lines?.length) throw new Error('至少添加一个产品') } }]}>{(fields, { add, remove }, { errors }) => <><Space orientation="vertical" className="full-width">{fields.map((field) => <Card key={field.key} size="small"><div className="payment-line-fields"><Form.Item name={[field.name, 'productItemId']} label="产品" rules={[{ required: true }]}><Select options={tracked.map((item) => ({ value: item.productItemId, label: `${item.productName} · 当前 ${item.onHandQuantity}${item.unitName}` }))} /></Form.Item><Form.Item name={[field.name, 'quantity']} label="变动数量" rules={[{ required: true }, { type: 'number', min: 1, max: 1000000000 }]}><InputNumber min={1} max={1000000000} precision={0} /></Form.Item></div>{fields.length > 1 && <Button danger onClick={() => remove(field.name)}>移除此行</Button>}</Card>)}</Space><Space><Button icon={<PlusOutlined />} onClick={() => add({ quantity: 1 })}>添加产品</Button><Form.ErrorList errors={errors} /></Space></>}</Form.List><Form.Item name="reason" label="变动原因" rules={[{ required: true, whitespace: true }, { max: 500 }]}><Input.TextArea rows={3} maxLength={500} showCount /></Form.Item></Form></Modal>
  </div>
}
