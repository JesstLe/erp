import { FileImageOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Form, Image, Input, Modal, Select, Space, Tag, Typography, Upload, message } from 'antd'
import type { UploadFile } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { ServiceRecord, ServiceRecordOrderOption } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface ServiceRecordForm { serviceOccurredAt: string; serviceOrderId?: string; conditionNotes?: string; serviceContent?: string; followUpNotes?: string }

function localDateTimeValue() {
  const now = new Date(); const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

export function ServiceRecordsSection({ customerId, storeId }: { customerId: string; storeId: string }) {
  const auth = useAuth(); const canManage = auth.user?.roles.some((role) => role === 'OWNER' || role === 'STORE_MANAGER') ?? false
  const [open, setOpen] = useState(false); const [form] = Form.useForm<ServiceRecordForm>(); const [images, setImages] = useState<UploadFile[]>([]); const queryClient = useQueryClient()
  const records = useQuery({ queryKey: ['service-records', storeId, customerId], enabled: canManage, queryFn: () => apiRequest<ServiceRecord[]>(`/api/v1/customers/${customerId}/service-records?storeId=${storeId}`) })
  const orders = useQuery({ queryKey: ['service-record-order-options', storeId, customerId], enabled: canManage && open, queryFn: () => apiRequest<ServiceRecordOrderOption[]>(`/api/v1/customers/${customerId}/service-record-order-options?storeId=${storeId}`) })
  const create = useMutation({ mutationFn: async (values: ServiceRecordForm) => { const data = new FormData(); data.append('storeId', storeId); data.append('commandId', crypto.randomUUID()); data.append('serviceOccurredAtUtc', new Date(values.serviceOccurredAt).toISOString()); if (values.serviceOrderId) data.append('serviceOrderId', values.serviceOrderId); if (values.conditionNotes?.trim()) data.append('conditionNotes', values.conditionNotes.trim()); if (values.serviceContent?.trim()) data.append('serviceContent', values.serviceContent.trim()); if (values.followUpNotes?.trim()) data.append('followUpNotes', values.followUpNotes.trim()); images.forEach((image) => { if (image.originFileObj) data.append('images', image.originFileObj) }); return apiRequest<ServiceRecord>(`/api/v1/customers/${customerId}/service-records`, { method: 'POST', body: data }) }, onSuccess: async () => { message.success('服务记录已存档'); setOpen(false); setImages([]); form.resetFields(); await queryClient.invalidateQueries({ queryKey: ['service-records', storeId, customerId] }) } })
  if (!canManage) return null
  const chooseImage = (file: File) => {
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { message.error('只允许 JPEG、PNG 或 WebP 图片'); return Upload.LIST_IGNORE }
    if (file.size > 5 * 1024 * 1024) { message.error('单张图片不能超过 5MB'); return Upload.LIST_IGNORE }
    if (images.length >= 6) { message.error('每条服务记录最多 6 张图片'); return Upload.LIST_IGNORE }
    setImages((current) => [...current, { uid: crypto.randomUUID(), name: file.name, size: file.size, type: file.type, status: 'done', originFileObj: file as UploadFile['originFileObj'] }]); return false
  }
  const textBlock = (label: string, value?: string) => value ? <div><Typography.Text type="secondary">{label}</Typography.Text><Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 8 }}>{value}</Typography.Paragraph></div> : null

  return <div><div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}><div><Typography.Title level={4} style={{ margin: 0 }}>服务档案</Typography.Title><Typography.Text type="secondary">存档服务过程，不重复录入消费金额</Typography.Text></div><Button type="primary" icon={<PlusOutlined />} onClick={() => { form.setFieldsValue({ serviceOccurredAt: localDateTimeValue() }); setImages([]); setOpen(true) }}>新增服务记录</Button></div>
    <Alert type="warning" showIcon title="服务文字和图片属于顾客隐私，仅最高权限账号和店长可查看；历史记录不直接覆盖。" style={{ marginBottom: 12 }} />
    {records.error && <Alert type="error" showIcon title={records.error instanceof Error ? records.error.message : '服务档案加载失败'} />}
    {!records.data?.length ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="还没有服务记录" /> : <Space orientation="vertical" className="full-width" size={12}>{records.data.map((record) => <Card key={record.id} size="small"><Space orientation="vertical" className="full-width" size={8}><div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}><Space wrap><strong>{new Date(record.serviceOccurredAtUtc).toLocaleString('zh-CN', { hour12: false })}</strong>{record.serviceOrderNo && <Tag color="blue">关联消费单 {record.serviceOrderNo}</Tag>}</Space><Typography.Text type="secondary">建档：{record.createdByName}</Typography.Text></div>{textBlock('本次情况/需求', record.conditionNotes)}{textBlock('服务过程与内容', record.serviceContent)}{textBlock('结果与后续建议', record.followUpNotes)}{record.attachments.length > 0 && <Image.PreviewGroup><Space wrap>{record.attachments.map((image) => <Image key={image.fileId} width={88} height={88} style={{ objectFit: 'cover', borderRadius: 8 }} src={`/api/v1/customers/${customerId}/service-record-files/${image.fileId}?storeId=${storeId}`} />)}</Space></Image.PreviewGroup>}{!record.conditionNotes && !record.serviceContent && !record.followUpNotes && !record.attachments.length && <Typography.Text type="secondary">本次只登记了服务时间。</Typography.Text>}</Space></Card>)}</Space>}

    <Modal title="新增服务记录" width={680} open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} confirmLoading={create.isPending} okText="确认存档" destroyOnHidden>
      {create.error && <Alert type="error" showIcon title={create.error instanceof ApiError ? create.error.message : '保存失败'} className="modal-alert" />}
      <Alert type="info" showIcon title="除服务时间外均为选填。可关联原消费单，但金额直接引用原单，不在这里重复输入。" className="modal-alert" />
      <Form<ServiceRecordForm> form={form} layout="vertical" onFinish={(values) => create.mutate(values)}><Form.Item name="serviceOccurredAt" label="服务时间" rules={[{ required: true, message: '请选择服务时间' }]}><Input type="datetime-local" max={localDateTimeValue()} /></Form.Item><Form.Item name="serviceOrderId" label="关联消费单（可选）"><Select allowClear showSearch loading={orders.isLoading} placeholder="不关联也可以存档" options={orders.data?.map((order) => ({ value: order.id, label: `${order.orderNo} · ${new Date(order.createdAtUtc).toLocaleString('zh-CN', { hour12: false })}` }))} /></Form.Item><Form.Item name="conditionNotes" label="本次情况/需求（可选）" rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount placeholder="例如顾客本次提出的需求、服务前状态或注意事项" /></Form.Item><Form.Item name="serviceContent" label="服务过程与内容（可选）" rules={[{ max: 4000 }]}><Input.TextArea rows={4} maxLength={4000} showCount placeholder="记录本次实际进行了哪些服务及处理过程" /></Form.Item><Form.Item name="followUpNotes" label="结果与后续建议（可选）" rules={[{ max: 2000 }]}><Input.TextArea rows={3} maxLength={2000} showCount placeholder="记录服务结果、后续关注事项或下次建议" /></Form.Item><Form.Item label="服务图片（可选，最多6张）"><Upload accept="image/jpeg,image/png,image/webp" multiple listType="picture" fileList={images} beforeUpload={chooseImage} onRemove={(file) => { setImages((current) => current.filter((item) => item.uid !== file.uid)); return true }}><Button icon={<UploadOutlined />} disabled={images.length >= 6}>选择图片</Button></Upload><Typography.Text type="secondary"><FileImageOutlined /> JPEG、PNG、WebP，单张不超过5MB</Typography.Text></Form.Item></Form>
    </Modal>
  </div>
}
