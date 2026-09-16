import { DeleteOutlined, EditOutlined, FileImageOutlined, PlusOutlined, SettingOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Form, Image, Input, InputNumber, Modal, Pagination, Popconfirm, Select, Space, Table, Tag, Typography, Upload, message } from 'antd'
import type { UploadFile } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { PageResult, ServiceRecord, ServiceRecordCaregiverOption, ServiceRecordCategory, ServiceRecordOrderOption } from '../api/types'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

interface ServiceRecordForm {
  serviceOccurredAt: string
  serviceOrderId?: string
  categoryId?: string
  caregiverEmployeeId?: string
  followUpAt?: string
  conditionNotes?: string
  serviceContent?: string
  followUpNotes?: string
}
type CorrectionForm = Omit<ServiceRecordForm, 'serviceOccurredAt' | 'serviceOrderId' | 'categoryId'>
interface CategoryForm { name: string; sortOrder: number; isEnabled: boolean }

function localDateTimeValue(value = new Date()) {
  const local = new Date(value.getTime() - value.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}
function requestError(error: unknown, fallback: string) {
  return error instanceof ApiError ? error.message : error instanceof Error ? error.message : fallback
}

export function ServiceRecordsSection({ customerId, storeId }: { customerId: string; storeId: string }) {
  const { can } = useAuthorization(); const canManage = can(Permission.ServiceRecordManage)
  const [open, setOpen] = useState(false); const [correcting, setCorrecting] = useState<ServiceRecord>()
  const [categoryOpen, setCategoryOpen] = useState(false); const [editingCategory, setEditingCategory] = useState<ServiceRecordCategory>()
  const [page, setPage] = useState(1); const pageSize = 5
  const [form] = Form.useForm<ServiceRecordForm>(); const [correctionForm] = Form.useForm<CorrectionForm>()
  const [categoryForm] = Form.useForm<CategoryForm>(); const [images, setImages] = useState<UploadFile[]>([])
  const queryClient = useQueryClient()
  const records = useQuery({ queryKey: ['service-records', storeId, customerId, page], enabled: canManage, queryFn: () => apiRequest<PageResult<ServiceRecord>>(`/api/v1/customers/${customerId}/service-records?storeId=${storeId}&page=${page}&pageSize=${pageSize}`) })
  const orders = useQuery({ queryKey: ['service-record-order-options', storeId, customerId], enabled: canManage && open, queryFn: () => apiRequest<ServiceRecordOrderOption[]>(`/api/v1/customers/${customerId}/service-record-order-options?storeId=${storeId}`) })
  const caregivers = useQuery({ queryKey: ['service-record-caregivers', storeId], enabled: canManage && (open || Boolean(correcting)), queryFn: () => apiRequest<ServiceRecordCaregiverOption[]>(`/api/v1/customers/service-record-caregivers?storeId=${storeId}`) })
  const categories = useQuery({ queryKey: ['service-record-categories'], enabled: canManage, queryFn: () => apiRequest<ServiceRecordCategory[]>('/api/v1/customers/service-record-categories') })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['service-records', storeId, customerId] })
  const refreshCategories = () => queryClient.invalidateQueries({ queryKey: ['service-record-categories'] })

  const create = useMutation({ mutationFn: async (values: ServiceRecordForm) => {
    const data = new FormData(); data.append('storeId', storeId); data.append('commandId', crypto.randomUUID())
    data.append('serviceOccurredAtUtc', new Date(values.serviceOccurredAt).toISOString())
    if (values.serviceOrderId) data.append('serviceOrderId', values.serviceOrderId)
    if (values.categoryId) data.append('categoryId', values.categoryId)
    if (values.caregiverEmployeeId) data.append('caregiverEmployeeId', values.caregiverEmployeeId)
    if (values.followUpAt) data.append('followUpAtUtc', new Date(values.followUpAt).toISOString())
    if (values.conditionNotes?.trim()) data.append('conditionNotes', values.conditionNotes.trim())
    if (values.serviceContent?.trim()) data.append('serviceContent', values.serviceContent.trim())
    if (values.followUpNotes?.trim()) data.append('followUpNotes', values.followUpNotes.trim())
    images.forEach((image) => { if (image.originFileObj) data.append('images', image.originFileObj) })
    return apiRequest<ServiceRecord>(`/api/v1/customers/${customerId}/service-records`, { method: 'POST', body: data })
  }, onSuccess: async () => { message.success('服务记录已存档，回访提醒已同步'); setOpen(false); setImages([]); form.resetFields(); await refresh(); await queryClient.invalidateQueries({ queryKey: ['notifications'] }) }, onError: (error) => message.error(requestError(error, '保存失败')) })

  const correct = useMutation({ mutationFn: (values: CorrectionForm) => apiRequest<ServiceRecord>(`/api/v1/customers/${customerId}/service-records/${correcting!.id}/corrections`, { method: 'POST', body: JSON.stringify({ storeId, conditionNotes: values.conditionNotes?.trim() || null, serviceContent: values.serviceContent?.trim() || null, followUpNotes: values.followUpNotes?.trim() || null, caregiverEmployeeId: values.caregiverEmployeeId || null, followUpAtUtc: values.followUpAt ? new Date(values.followUpAt).toISOString() : null, commandId: crypto.randomUUID() }) }), onSuccess: async () => { message.success('补充内容已保存，原始档案保持不变'); setCorrecting(undefined); correctionForm.resetFields(); await refresh(); await queryClient.invalidateQueries({ queryKey: ['notifications'] }) }, onError: (error) => message.error(requestError(error, '保存失败')) })

  const saveCategory = useMutation({ mutationFn: (values: CategoryForm) => editingCategory
    ? apiRequest<ServiceRecordCategory>(`/api/v1/customers/service-record-categories/${editingCategory.id}`, { method: 'PUT', body: JSON.stringify({ name: values.name, sortOrder: values.sortOrder, isEnabled: values.isEnabled, expectedVersion: editingCategory.version }) })
    : apiRequest<ServiceRecordCategory>('/api/v1/customers/service-record-categories', { method: 'POST', body: JSON.stringify({ name: values.name, sortOrder: values.sortOrder }) }), onSuccess: async () => { message.success(editingCategory ? '分类已更新' : '分类已添加'); setEditingCategory(undefined); categoryForm.resetFields(); categoryForm.setFieldsValue({ sortOrder: 100, isEnabled: true }); await refreshCategories() }, onError: (error) => message.error(requestError(error, '分类保存失败')) })
  const deleteCategory = useMutation({ mutationFn: (item: ServiceRecordCategory) => apiRequest<void>(`/api/v1/customers/service-record-categories/${item.id}?expectedVersion=${item.version}`, { method: 'DELETE' }), onSuccess: async () => { message.success('未使用的分类已删除'); await refreshCategories() }, onError: (error) => message.error(requestError(error, '分类删除失败')) })

  if (!canManage) return null
  const chooseImage = (file: File) => {
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { message.error('只允许 JPEG、PNG 或 WebP 图片'); return Upload.LIST_IGNORE }
    if (file.size > 5 * 1024 * 1024) { message.error('单张图片不能超过 5MB'); return Upload.LIST_IGNORE }
    if (images.length >= 6) { message.error('每条服务记录最多 6 张图片'); return Upload.LIST_IGNORE }
    setImages((current) => [...current, { uid: crypto.randomUUID(), name: file.name, size: file.size, type: file.type, status: 'done', originFileObj: file as UploadFile['originFileObj'] }]); return false
  }
  const textBlock = (label: string, value?: string) => value ? <div><Typography.Text type="secondary">{label}</Typography.Text><Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 8 }}>{value}</Typography.Paragraph></div> : null
  const openCorrection = (record: ServiceRecord) => {
    const latest = record.corrections.at(-1); const followUp = latest ? latest.followUpAtUtc : record.followUpAtUtc
    correctionForm.setFieldsValue({ conditionNotes: latest?.conditionNotes ?? record.conditionNotes, serviceContent: latest?.serviceContent ?? record.serviceContent, followUpNotes: latest?.followUpNotes ?? record.followUpNotes, caregiverEmployeeId: latest ? latest.caregiverEmployeeId : record.caregiverEmployeeId, followUpAt: followUp ? localDateTimeValue(new Date(followUp)) : undefined })
    setCorrecting(record)
  }
  const openCategoryManager = () => { setEditingCategory(undefined); categoryForm.resetFields(); categoryForm.setFieldsValue({ sortOrder: 100, isEnabled: true }); setCategoryOpen(true) }

  const recordFields = (correction = false) => <>
    <div className="two-column-form">
      <Form.Item name="caregiverEmployeeId" label="护理老师（可选）"><Select allowClear showSearch optionFilterProp="label" placeholder="选择本次护理老师" options={caregivers.data?.map((item) => ({ value: item.id, label: `${item.displayName} · ${item.employeeNo}` }))} /></Form.Item>
      <Form.Item name="followUpAt" label="回访提醒时间（可选）" extra="到期前 24 小时起会在右上角铃铛显示顾客护理提醒"><Input type="datetime-local" /></Form.Item>
    </div>
    <Form.Item name="conditionNotes" label={`${correction ? '更新后的' : ''}本次情况/需求（可选）`} rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount /></Form.Item>
    <Form.Item name="serviceContent" label={`${correction ? '更新后的' : ''}服务过程与内容（可选）`} rules={[{ max: 4000 }]}><Input.TextArea rows={4} maxLength={4000} showCount /></Form.Item>
    <Form.Item name="followUpNotes" label={`${correction ? '更新后的' : ''}结果与后续建议（可选）`} rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount /></Form.Item>
  </>

  return <div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}><div><Typography.Title level={4} style={{ margin: 0 }}>服务档案</Typography.Title><Typography.Text type="secondary">记录护理老师、服务过程和下次回访时间，不重复录入消费金额</Typography.Text></div><Space><Button icon={<SettingOutlined />} onClick={openCategoryManager}>护理分类设置</Button><Button type="primary" icon={<PlusOutlined />} onClick={() => { form.setFieldsValue({ serviceOccurredAt: localDateTimeValue() }); setImages([]); setOpen(true) }}>新增服务记录</Button></Space></div>
    <Alert type="warning" showIcon title="服务文字和图片属于顾客隐私；历史原文不可覆盖，如需更新请追加补充记录。" style={{ marginBottom: 12 }} />
    {records.error && <Alert type="error" showIcon title={requestError(records.error, '服务档案加载失败')} />}
    {!records.data?.items.length ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="还没有服务记录" /> : <><Space orientation="vertical" className="full-width" size={12}>{records.data.items.map((record) => {
      const latest = record.corrections.at(-1); const caregiverName = latest ? latest.caregiverName : record.caregiverName; const followUpAt = latest ? latest.followUpAtUtc : record.followUpAtUtc
      return <Card key={record.id} size="small"><Space orientation="vertical" className="full-width" size={8}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}><Space wrap><strong>{new Date(record.serviceOccurredAtUtc).toLocaleString('zh-CN', { hour12: false })}</strong>{record.categoryName && <Tag color="purple">{record.categoryName}</Tag>}{record.serviceOrderNo && <Tag color="blue">关联消费单 {record.serviceOrderNo}</Tag>}{record.corrections.length > 0 && <Tag color="gold">已补充 {record.corrections.length} 次</Tag>}</Space><Space><Typography.Text type="secondary">建档：{record.createdByName}</Typography.Text><Button size="small" icon={<EditOutlined />} onClick={() => openCorrection(record)}>追加/更正</Button></Space></div>
        <Space wrap>{caregiverName && <Tag color="cyan">护理老师：{caregiverName}</Tag>}{followUpAt && <Tag color={new Date(followUpAt) <= new Date() ? 'red' : 'orange'}>回访：{new Date(followUpAt).toLocaleString('zh-CN', { hour12: false })}</Tag>}</Space>
        {latest && <Alert type="info" showIcon title={`当前按最后一次补充展示 · ${latest.correctedByName} · ${new Date(latest.createdAtUtc).toLocaleString('zh-CN', { hour12: false })}`} />}
        {textBlock('本次情况/需求', latest ? latest.conditionNotes : record.conditionNotes)}{textBlock('服务过程与内容', latest ? latest.serviceContent : record.serviceContent)}{textBlock('结果与后续建议', latest ? latest.followUpNotes : record.followUpNotes)}
        {record.attachments.length > 0 && <Image.PreviewGroup><Space wrap>{record.attachments.map((image) => <Image key={image.fileId} width={88} height={88} style={{ objectFit: 'cover', borderRadius: 8 }} src={`/api/v1/customers/${customerId}/service-record-files/${image.fileId}?storeId=${storeId}`} />)}</Space></Image.PreviewGroup>}
        {!latest && !record.conditionNotes && !record.serviceContent && !record.followUpNotes && !record.attachments.length && <Typography.Text type="secondary">本次只登记了服务时间。</Typography.Text>}
        {record.corrections.length > 0 && <details><summary>查看原文与全部补充历史</summary><Card size="small" style={{ marginTop: 8 }}>{textBlock('原始情况/需求', record.conditionNotes)}{textBlock('原始服务内容', record.serviceContent)}{textBlock('原始后续建议', record.followUpNotes)}{record.corrections.map((correction, index) => <Card key={correction.id} size="small" type="inner" title={`第 ${index + 1} 次补充 · ${correction.correctedByName}`} extra={new Date(correction.createdAtUtc).toLocaleString('zh-CN', { hour12: false })}><Space wrap>{correction.caregiverName && <Tag>护理老师：{correction.caregiverName}</Tag>}{correction.followUpAtUtc && <Tag>回访：{new Date(correction.followUpAtUtc).toLocaleString('zh-CN', { hour12: false })}</Tag>}</Space>{textBlock('情况/需求', correction.conditionNotes)}{textBlock('服务内容', correction.serviceContent)}{textBlock('后续建议', correction.followUpNotes)}</Card>)}</Card></details>}
      </Space></Card>
    })}</Space><Pagination current={page} pageSize={pageSize} total={records.data.total} showSizeChanger={false} showTotal={(total) => `共 ${total} 条`} onChange={setPage} style={{ marginTop: 12, textAlign: 'right' }} /></>}

    <Modal title="新增服务记录" width={720} open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} confirmLoading={create.isPending} okText="确认存档" destroyOnHidden>
      {create.error && <Alert type="error" showIcon title={requestError(create.error, '保存失败')} className="modal-alert" />}
      <Alert type="info" showIcon title="除服务时间外均为选填；设置回访时间后，右上角铃铛会在到期前提醒。" className="modal-alert" />
      <Form<ServiceRecordForm> form={form} layout="vertical" onFinish={(values) => create.mutate(values)}><Form.Item name="serviceOccurredAt" label="服务时间" rules={[{ required: true, message: '请选择服务时间' }]}><Input type="datetime-local" max={localDateTimeValue()} /></Form.Item><Form.Item label="服务记录分类（可选）"><Space.Compact className="full-width"><Form.Item name="categoryId" noStyle><Select allowClear showSearch placeholder="未分类" options={categories.data?.filter((item) => item.status === 'ENABLED').map((item) => ({ value: item.id, label: `${item.name} · ${item.code}` }))} /></Form.Item><Button icon={<SettingOutlined />} onClick={openCategoryManager}>添加/管理</Button></Space.Compact></Form.Item><Form.Item name="serviceOrderId" label="关联消费单（可选）"><Select allowClear showSearch loading={orders.isLoading} placeholder="不关联也可以存档" options={orders.data?.map((order) => ({ value: order.id, label: `${order.orderNo} · ${new Date(order.createdAtUtc).toLocaleString('zh-CN', { hour12: false })}` }))} /></Form.Item>{recordFields()}<Form.Item label="服务图片（可选，最多6张）"><Upload accept="image/jpeg,image/png,image/webp" multiple listType="picture" fileList={images} beforeUpload={chooseImage} onRemove={(file) => { setImages((current) => current.filter((item) => item.uid !== file.uid)); return true }}><Button icon={<UploadOutlined />} disabled={images.length >= 6}>选择图片</Button></Upload><Typography.Text type="secondary"><FileImageOutlined /> JPEG、PNG、WebP，单张不超过5MB</Typography.Text></Form.Item></Form>
    </Modal>

    <Modal title="追加/更正服务档案" width={720} open={Boolean(correcting)} onCancel={() => setCorrecting(undefined)} onOk={() => correctionForm.submit()} confirmLoading={correct.isPending} okText="保存补充内容" destroyOnHidden>
      {correct.error && <Alert type="error" showIcon title={requestError(correct.error, '保存失败')} className="modal-alert" />}
      <Alert type="info" showIcon title="无需填写更正原因。系统会保留原始档案、操作人和更新时间，当前页面默认展示最新内容。" className="modal-alert" />
      <Form<CorrectionForm> form={correctionForm} layout="vertical" onFinish={(values) => correct.mutate(values)}>{recordFields(true)}</Form>
    </Modal>

    <Modal title="护理/服务记录分类设置" width={760} open={categoryOpen} onCancel={() => setCategoryOpen(false)} footer={<Button type="primary" onClick={() => setCategoryOpen(false)}>完成</Button>} destroyOnHidden>
      <Alert type="info" showIcon title="可建立“回诊顾客”“家属顾客”“其他顾客”等分类；停用后历史档案仍保留。" className="modal-alert" />
      <Form<CategoryForm> form={categoryForm} layout="inline" onFinish={(values) => saveCategory.mutate(values)} style={{ marginBottom: 16 }}><Form.Item name="name" rules={[{ required: true, message: '请输入分类名称' }, { max: 60 }]}><Input placeholder="分类名称" maxLength={60} /></Form.Item><Form.Item name="sortOrder" rules={[{ required: true }]}><InputNumber min={0} max={9999} precision={0} placeholder="排序" /></Form.Item>{editingCategory && <Form.Item name="isEnabled"><Select style={{ width: 100 }} options={[{ value: true, label: '启用' }, { value: false, label: '停用' }]} /></Form.Item>}<Form.Item><Space><Button type="primary" htmlType="submit" loading={saveCategory.isPending}>{editingCategory ? '保存修改' : '添加分类'}</Button>{editingCategory && <Button onClick={() => { setEditingCategory(undefined); categoryForm.resetFields(); categoryForm.setFieldsValue({ sortOrder: 100, isEnabled: true }) }}>取消编辑</Button>}</Space></Form.Item></Form>
      <Table<ServiceRecordCategory> rowKey="id" size="small" pagination={false} dataSource={categories.data ?? []} columns={[{ title: '编码', dataIndex: 'code', width: 120 }, { title: '分类名称', dataIndex: 'name' }, { title: '排序', dataIndex: 'sortOrder', width: 80 }, { title: '状态', dataIndex: 'status', width: 80, render: (value: string) => <Tag color={value === 'ENABLED' ? 'green' : 'default'}>{value === 'ENABLED' ? '启用' : '停用'}</Tag> }, { title: '操作', width: 150, render: (_: unknown, item) => <Space><Button size="small" icon={<EditOutlined />} onClick={() => { setEditingCategory(item); categoryForm.setFieldsValue({ name: item.name, sortOrder: item.sortOrder, isEnabled: item.status === 'ENABLED' }) }}>编辑</Button><Popconfirm title="删除这个分类？" description="已被档案使用的分类不能删除，可改为停用。" onConfirm={() => deleteCategory.mutateAsync(item)}><Button size="small" danger icon={<DeleteOutlined />} /></Popconfirm></Space> }]} />
    </Modal>
  </div>
}
