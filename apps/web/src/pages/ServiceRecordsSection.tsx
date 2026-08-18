import { EditOutlined, FileImageOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Form, Image, Input, Modal, Pagination, Select, Space, Tag, Typography, Upload, message } from 'antd'
import type { UploadFile } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { PageResult, ServiceRecord, ServiceRecordOrderOption } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface ServiceRecordForm { serviceOccurredAt: string; serviceOrderId?: string; conditionNotes?: string; serviceContent?: string; followUpNotes?: string }
interface CorrectionForm { reason: string; conditionNotes?: string; serviceContent?: string; followUpNotes?: string }

function localDateTimeValue() {
  const now = new Date(); const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

export function ServiceRecordsSection({ customerId, storeId }: { customerId: string; storeId: string }) {
  const auth = useAuth(); const canManage = auth.user?.roles.some((role) => role === 'OWNER' || role === 'STORE_MANAGER') ?? false
  const [open, setOpen] = useState(false); const [correcting, setCorrecting] = useState<ServiceRecord>()
  const [page, setPage] = useState(1); const pageSize = 5; const [form] = Form.useForm<ServiceRecordForm>()
  const [correctionForm] = Form.useForm<CorrectionForm>(); const [images, setImages] = useState<UploadFile[]>([])
  const queryClient = useQueryClient()
  const records = useQuery({ queryKey: ['service-records', storeId, customerId, page], enabled: canManage, queryFn: () => apiRequest<PageResult<ServiceRecord>>(`/api/v1/customers/${customerId}/service-records?storeId=${storeId}&page=${page}&pageSize=${pageSize}`) })
  const orders = useQuery({ queryKey: ['service-record-order-options', storeId, customerId], enabled: canManage && open, queryFn: () => apiRequest<ServiceRecordOrderOption[]>(`/api/v1/customers/${customerId}/service-record-order-options?storeId=${storeId}`) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['service-records', storeId, customerId] })
  const create = useMutation({ mutationFn: async (values: ServiceRecordForm) => { const data = new FormData(); data.append('storeId', storeId); data.append('commandId', crypto.randomUUID()); data.append('serviceOccurredAtUtc', new Date(values.serviceOccurredAt).toISOString()); if (values.serviceOrderId) data.append('serviceOrderId', values.serviceOrderId); if (values.conditionNotes?.trim()) data.append('conditionNotes', values.conditionNotes.trim()); if (values.serviceContent?.trim()) data.append('serviceContent', values.serviceContent.trim()); if (values.followUpNotes?.trim()) data.append('followUpNotes', values.followUpNotes.trim()); images.forEach((image) => { if (image.originFileObj) data.append('images', image.originFileObj) }); return apiRequest<ServiceRecord>(`/api/v1/customers/${customerId}/service-records`, { method: 'POST', body: data }) }, onSuccess: async () => { message.success('服务记录已存档'); setOpen(false); setImages([]); form.resetFields(); await refresh() } })
  const correct = useMutation({ mutationFn: (values: CorrectionForm) => apiRequest<ServiceRecord>(`/api/v1/customers/${customerId}/service-records/${correcting!.id}/corrections`, { method: 'POST', body: JSON.stringify({ storeId, ...values, commandId: crypto.randomUUID() }) }), onSuccess: async () => { message.success('更正已追加，原始档案保持不变'); setCorrecting(undefined); correctionForm.resetFields(); await refresh() } })
  if (!canManage) return null
  const chooseImage = (file: File) => {
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { message.error('只允许 JPEG、PNG 或 WebP 图片'); return Upload.LIST_IGNORE }
    if (file.size > 5 * 1024 * 1024) { message.error('单张图片不能超过 5MB'); return Upload.LIST_IGNORE }
    if (images.length >= 6) { message.error('每条服务记录最多 6 张图片'); return Upload.LIST_IGNORE }
    setImages((current) => [...current, { uid: crypto.randomUUID(), name: file.name, size: file.size, type: file.type, status: 'done', originFileObj: file as UploadFile['originFileObj'] }]); return false
  }
  const textBlock = (label: string, value?: string) => value ? <div><Typography.Text type="secondary">{label}</Typography.Text><Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 8 }}>{value}</Typography.Paragraph></div> : null
  const openCorrection = (record: ServiceRecord) => {
    const latest = record.corrections.at(-1)
    correctionForm.setFieldsValue({ reason: '', conditionNotes: latest?.conditionNotes ?? record.conditionNotes, serviceContent: latest?.serviceContent ?? record.serviceContent, followUpNotes: latest?.followUpNotes ?? record.followUpNotes })
    setCorrecting(record)
  }

  return <div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}><div><Typography.Title level={4} style={{ margin: 0 }}>服务档案</Typography.Title><Typography.Text type="secondary">存档服务过程，不重复录入消费金额</Typography.Text></div><Button type="primary" icon={<PlusOutlined />} onClick={() => { form.setFieldsValue({ serviceOccurredAt: localDateTimeValue() }); setImages([]); setOpen(true) }}>新增服务记录</Button></div>
    <Alert type="warning" showIcon title="服务文字和图片属于顾客隐私；历史原文不可覆盖，发现错误时只能追加带原因的更正。" style={{ marginBottom: 12 }} />
    {records.error && <Alert type="error" showIcon title={records.error instanceof Error ? records.error.message : '服务档案加载失败'} />}
    {!records.data?.items.length ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="还没有服务记录" /> : <><Space orientation="vertical" className="full-width" size={12}>{records.data.items.map((record) => {
      const latest = record.corrections.at(-1)
      return <Card key={record.id} size="small"><Space orientation="vertical" className="full-width" size={8}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}><Space wrap><strong>{new Date(record.serviceOccurredAtUtc).toLocaleString('zh-CN', { hour12: false })}</strong>{record.serviceOrderNo && <Tag color="blue">关联消费单 {record.serviceOrderNo}</Tag>}{record.corrections.length > 0 && <Tag color="gold">已更正 {record.corrections.length} 次</Tag>}</Space><Space><Typography.Text type="secondary">建档：{record.createdByName}</Typography.Text><Button size="small" icon={<EditOutlined />} onClick={() => openCorrection(record)}>追加更正</Button></Space></div>
        {latest && <Alert type="info" showIcon title={`当前按最后一次更正展示 · ${latest.correctedByName} · ${new Date(latest.createdAtUtc).toLocaleString('zh-CN', { hour12: false })}`} description={`原因：${latest.reason}`} />}
        {textBlock('本次情况/需求', latest ? latest.conditionNotes : record.conditionNotes)}{textBlock('服务过程与内容', latest ? latest.serviceContent : record.serviceContent)}{textBlock('结果与后续建议', latest ? latest.followUpNotes : record.followUpNotes)}
        {record.attachments.length > 0 && <Image.PreviewGroup><Space wrap>{record.attachments.map((image) => <Image key={image.fileId} width={88} height={88} style={{ objectFit: 'cover', borderRadius: 8 }} src={`/api/v1/customers/${customerId}/service-record-files/${image.fileId}?storeId=${storeId}`} />)}</Space></Image.PreviewGroup>}
        {!latest && !record.conditionNotes && !record.serviceContent && !record.followUpNotes && !record.attachments.length && <Typography.Text type="secondary">本次只登记了服务时间。</Typography.Text>}
        {record.corrections.length > 0 && <details><summary>查看原文与全部更正历史</summary><Card size="small" style={{ marginTop: 8 }}>{textBlock('原始情况/需求', record.conditionNotes)}{textBlock('原始服务内容', record.serviceContent)}{textBlock('原始后续建议', record.followUpNotes)}{record.corrections.map((correction, index) => <Card key={correction.id} size="small" type="inner" title={`第 ${index + 1} 次更正 · ${correction.correctedByName}`} extra={new Date(correction.createdAtUtc).toLocaleString('zh-CN', { hour12: false })}><Typography.Paragraph>原因：{correction.reason}</Typography.Paragraph>{textBlock('情况/需求', correction.conditionNotes)}{textBlock('服务内容', correction.serviceContent)}{textBlock('后续建议', correction.followUpNotes)}</Card>)}</Card></details>}
      </Space></Card>
    })}</Space><Pagination current={page} pageSize={pageSize} total={records.data.total} showSizeChanger={false} showTotal={(total) => `共 ${total} 条`} onChange={setPage} style={{ marginTop: 12, textAlign: 'right' }} /></>}

    <Modal title="新增服务记录" width={680} open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} confirmLoading={create.isPending} okText="确认存档" destroyOnHidden>
      {create.error && <Alert type="error" showIcon title={create.error instanceof ApiError ? create.error.message : '保存失败'} className="modal-alert" />}
      <Alert type="info" showIcon title="除服务时间外均为选填。可关联原消费单，但金额直接引用原单，不在这里重复输入。" className="modal-alert" />
      <Form<ServiceRecordForm> form={form} layout="vertical" onFinish={(values) => create.mutate(values)}><Form.Item name="serviceOccurredAt" label="服务时间" rules={[{ required: true, message: '请选择服务时间' }]}><Input type="datetime-local" max={localDateTimeValue()} /></Form.Item><Form.Item name="serviceOrderId" label="关联消费单（可选）"><Select allowClear showSearch loading={orders.isLoading} placeholder="不关联也可以存档" options={orders.data?.map((order) => ({ value: order.id, label: `${order.orderNo} · ${new Date(order.createdAtUtc).toLocaleString('zh-CN', { hour12: false })}` }))} /></Form.Item><Form.Item name="conditionNotes" label="本次情况/需求（可选）" rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount /></Form.Item><Form.Item name="serviceContent" label="服务过程与内容（可选）" rules={[{ max: 4000 }]}><Input.TextArea rows={4} maxLength={4000} showCount /></Form.Item><Form.Item name="followUpNotes" label="结果与后续建议（可选）" rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount /></Form.Item><Form.Item label="服务图片（可选，最多6张）"><Upload accept="image/jpeg,image/png,image/webp" multiple listType="picture" fileList={images} beforeUpload={chooseImage} onRemove={(file) => { setImages((current) => current.filter((item) => item.uid !== file.uid)); return true }}><Button icon={<UploadOutlined />} disabled={images.length >= 6}>选择图片</Button></Upload><Typography.Text type="secondary"><FileImageOutlined /> JPEG、PNG、WebP，单张不超过5MB</Typography.Text></Form.Item></Form>
    </Modal>

    <Modal title="追加服务档案更正" width={680} open={Boolean(correcting)} onCancel={() => setCorrecting(undefined)} onOk={() => correctionForm.submit()} confirmLoading={correct.isPending} okText="确认追加更正" destroyOnHidden>
      {correct.error && <Alert type="error" showIcon title={correct.error instanceof ApiError ? correct.error.message : '更正失败'} className="modal-alert" />}
      <Alert type="warning" showIcon title="本操作不会覆盖或删除原文。保存后默认展示本次更正内容，并可展开查看完整历史。" className="modal-alert" />
      <Form<CorrectionForm> form={correctionForm} layout="vertical" onFinish={(values) => correct.mutate(values)}><Form.Item name="reason" label="更正原因" rules={[{ required: true }, { min: 2 }, { max: 500 }]}><Input.TextArea rows={3} maxLength={500} showCount /></Form.Item><Form.Item name="conditionNotes" label="更正后的本次情况/需求（可为空）" rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount /></Form.Item><Form.Item name="serviceContent" label="更正后的服务过程与内容（可为空）" rules={[{ max: 4000 }]}><Input.TextArea rows={4} maxLength={4000} showCount /></Form.Item><Form.Item name="followUpNotes" label="更正后的结果与后续建议（可为空）" rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount /></Form.Item></Form>
    </Modal>
  </div>
}
