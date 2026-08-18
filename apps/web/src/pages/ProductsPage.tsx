import { PictureOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Form, Image, Input, Modal, Space, Table, Tag, Typography, Upload, message } from 'antd'
import type { UploadFile } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { ProductItem } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface ProductForm { code: string; name: string; unitName: string; trackInventory: boolean }

export function ProductsPage() {
  const auth = useAuth(); const canManage = auth.user?.roles.includes('OWNER') ?? false
  const [open, setOpen] = useState(false); const [form] = Form.useForm<ProductForm>(); const queryClient = useQueryClient()
  const [imageProduct, setImageProduct] = useState<ProductItem>(); const [imageFiles, setImageFiles] = useState<UploadFile[]>([])
  const query = useQuery({ queryKey: ['product-items'], queryFn: () => apiRequest<ProductItem[]>('/api/v1/catalog/products') })
  const create = useMutation({ mutationFn: (values: ProductForm) => apiRequest<ProductItem>('/api/v1/catalog/products', { method: 'POST', body: JSON.stringify(values) }), onSuccess: async () => { message.success('产品档案已创建；图片可以稍后上传'); setOpen(false); form.resetFields(); await queryClient.invalidateQueries({ queryKey: ['product-items'] }) } })
  const uploadImage = useMutation({ mutationFn: async ({ product, file }: { product: ProductItem; file: File }) => { const data = new FormData(); data.append('image', file); return apiRequest<ProductItem>(`/api/v1/catalog/products/${product.id}/image`, { method: 'POST', body: data }) }, onSuccess: async () => { message.success('产品图片已保存'); setImageProduct(undefined); setImageFiles([]); await queryClient.invalidateQueries({ queryKey: ['product-items'] }) } })
  const submitImage = () => { const file = imageFiles[0]?.originFileObj; if (!imageProduct || !file) return message.error('请选择图片'); uploadImage.mutate({ product: imageProduct, file }) }
  const selectImage = (file: File) => {
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { message.error('只允许 JPEG、PNG 或 WebP 图片'); return Upload.LIST_IGNORE }
    if (file.size > 5 * 1024 * 1024) { message.error('单张图片不能超过 5MB'); return Upload.LIST_IGNORE }
    setImageFiles([{ uid: crypto.randomUUID(), name: file.name, size: file.size, type: file.type, status: 'done', originFileObj: file as UploadFile['originFileObj'] }]); return false
  }

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>产品目录</Typography.Title><Typography.Paragraph>维护产品名称、可选图片、单位和库存属性；标准售价由最高权限账号在价格版本中统一发布。</Typography.Paragraph></div>{canManage && <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.setFieldsValue({ unitName: '件', trackInventory: false }); setOpen(true) }}>新建产品</Button>}</div>
    <Alert type="info" showIcon title="产品图片可选，用于门店快速辨认商品；价格与库存规则不依赖图片。跟踪库存的商品仍在确认、结算、作废和退货时处理库存。" />
    {query.error && <Alert type="error" showIcon title={query.error instanceof Error ? query.error.message : '加载失败'} />}
    <Card variant="borderless"><Table<ProductItem> rowKey="id" loading={query.isLoading} dataSource={query.data ?? []} pagination={{ pageSize: 10, showSizeChanger: false }} locale={{ emptyText: '还没有产品，请先新建产品档案' }} columns={[
      { title: '图片', dataIndex: 'imageFileId', width: 88, render: (value: string | undefined, item: ProductItem) => value ? <Image width={48} height={48} style={{ objectFit: 'cover', borderRadius: 8 }} src={`/api/v1/catalog/products/${item.id}/image?v=${value}`} /> : <div style={{ width: 48, height: 48, borderRadius: 8, background: '#f3f5f7', display: 'grid', placeItems: 'center' }}><PictureOutlined /></div> },
      { title: '产品编码', dataIndex: 'code', width: 150 }, { title: '产品名称', dataIndex: 'name' }, { title: '计量单位', dataIndex: 'unitName', width: 100 },
      { title: '库存属性', dataIndex: 'trackInventory', width: 130, render: (value: boolean) => <Tag color={value ? 'blue' : 'default'}>{value ? '跟踪库存' : '不管理库存'}</Tag> },
      { title: '状态', dataIndex: 'status', width: 90, render: (value: string) => <Tag color={value === 'ENABLED' ? 'green' : 'default'}>{value === 'ENABLED' ? '启用' : '停用'}</Tag> },
      { title: '图片操作', key: 'actions', width: 130, render: (_: unknown, item: ProductItem) => canManage ? <Button size="small" icon={<UploadOutlined />} onClick={() => { setImageProduct(item); setImageFiles([]) }}>{item.imageFileId ? '更换图片' : '上传图片'}</Button> : null },
    ]} /></Card>

    <Modal title="新建产品" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} confirmLoading={create.isPending} okText="保存产品" cancelText="取消" destroyOnHidden>
      {create.error && <Alert type="error" showIcon title={create.error instanceof ApiError ? create.error.message : '保存失败'} className="modal-alert" />}
      <Form<ProductForm> form={form} layout="vertical" onFinish={(values) => create.mutate(values)} requiredMark="optional"><Form.Item name="code" label="产品编码" rules={[{ required: true }, { max: 40 }]}><Input maxLength={40} placeholder="例如 PD001" /></Form.Item><Form.Item name="name" label="产品名称" rules={[{ required: true }, { max: 120 }]}><Input maxLength={120} /></Form.Item><Form.Item name="unitName" label="计量单位" rules={[{ required: true }, { max: 20 }]}><Input maxLength={20} placeholder="件、盒、套等" /></Form.Item><Form.Item name="trackInventory" valuePropName="checked"><Checkbox>跟踪门店库存</Checkbox></Form.Item><Alert type="warning" showIcon title="产品保存后可在列表上传可选图片；跟踪库存的产品需先录入期初或收货。" /></Form>
    </Modal>
    <Modal title={`${imageProduct?.name ?? ''} · ${imageProduct?.imageFileId ? '更换图片' : '上传图片'}`} open={Boolean(imageProduct)} onCancel={() => { setImageProduct(undefined); setImageFiles([]) }} onOk={submitImage} confirmLoading={uploadImage.isPending} okText="保存图片" destroyOnHidden>
      {uploadImage.error && <Alert type="error" showIcon title={uploadImage.error instanceof ApiError ? uploadImage.error.message : '上传失败'} className="modal-alert" />}
      <Space orientation="vertical" size={16} className="full-width"><Alert type="info" showIcon title="图片可选；只允许 JPEG、PNG、WebP，单张不超过 5MB。更换图片不会影响价格、库存或历史订单。" /><Upload accept="image/jpeg,image/png,image/webp" maxCount={1} fileList={imageFiles} beforeUpload={selectImage} onRemove={() => { setImageFiles([]); return true }}><Button icon={<UploadOutlined />}>选择图片</Button></Upload></Space>
    </Modal>
  </div>
}
