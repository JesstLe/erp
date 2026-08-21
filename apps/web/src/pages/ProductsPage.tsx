import { DeleteOutlined, EditOutlined, LoadingOutlined, PictureOutlined, PlusOutlined, SearchOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Form, Image, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, Upload, message } from 'antd'
import type { UploadFile } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { ProductItem } from '../api/types'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

interface ProductForm { name: string; unitName: string; trackInventory: boolean; status: string }

function requestError(error: unknown): string {
  return error instanceof ApiError ? error.message : '操作失败，请稍后重试'
}

export function ProductsPage() {
  const { can } = useAuthorization(); const canManage = can(Permission.CatalogWrite)
  const [open, setOpen] = useState(false); const [editing, setEditing] = useState<ProductItem>()
  const [queryText, setQueryText] = useState(''); const [status, setStatus] = useState<string>()
  const normalizedQuery = queryText.trim(); const appliedQuery = useDebouncedValue(normalizedQuery)
  const [form] = Form.useForm<ProductForm>(); const queryClient = useQueryClient()
  const [imageProduct, setImageProduct] = useState<ProductItem>(); const [imageFiles, setImageFiles] = useState<UploadFile[]>([])
  const params = new URLSearchParams(); if (appliedQuery) params.set('query', appliedQuery); if (status) params.set('status', status)
  const path = `/api/v1/catalog/products${params.size ? `?${params}` : ''}`
  const query = useQuery({ queryKey: ['product-items', appliedQuery, status], queryFn: ({ signal }) => apiRequest<ProductItem[]>(path, { signal }) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['product-items'] })
  const save = useMutation({
    mutationFn: (values: ProductForm) => editing
      ? apiRequest<ProductItem>(`/api/v1/catalog/products/${editing.id}`, { method: 'PUT', body: JSON.stringify({ ...values, expectedVersion: editing.version }) })
      : apiRequest<ProductItem>('/api/v1/catalog/products', { method: 'POST', body: JSON.stringify(values) }),
    onSuccess: async (item) => { message.success(editing ? '产品档案已更新' : `产品档案已创建，系统编码 ${item.code}；图片可以稍后上传`); setOpen(false); setEditing(undefined); form.resetFields(); await refresh() },
    onError: (error) => message.error(requestError(error)),
  })
  const updateStatus = useMutation({
    mutationFn: ({ item, nextStatus }: { item: ProductItem; nextStatus: string }) => apiRequest<ProductItem>(`/api/v1/catalog/products/${item.id}`, { method: 'PUT', body: JSON.stringify({ name: item.name, unitName: item.unitName, trackInventory: item.trackInventory, status: nextStatus, expectedVersion: item.version }) }),
    onSuccess: async (_, variables) => { message.success(variables.nextStatus === 'ENABLED' ? '产品已恢复' : '产品已停用'); await refresh() },
    onError: (error) => message.error(requestError(error)),
  })
  const remove = useMutation({
    mutationFn: (item: ProductItem) => apiRequest<void>(`/api/v1/catalog/products/${item.id}?expectedVersion=${item.version}`, { method: 'DELETE' }),
    onSuccess: async () => { message.success('未使用的产品已删除'); await refresh() },
    onError: (error) => message.error(requestError(error)),
  })
  const uploadImage = useMutation({ mutationFn: async ({ product, file }: { product: ProductItem; file: File }) => { const data = new FormData(); data.append('image', file); return apiRequest<ProductItem>(`/api/v1/catalog/products/${product.id}/image`, { method: 'POST', body: data }) }, onSuccess: async () => { message.success('产品图片已保存'); setImageProduct(undefined); setImageFiles([]); await refresh() }, onError: (error) => message.error(requestError(error)) })
  const submitImage = () => { const file = imageFiles[0]?.originFileObj; if (!imageProduct || !file) return message.error('请选择图片'); uploadImage.mutate({ product: imageProduct, file }) }
  const selectImage = (file: File) => {
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { message.error('只允许 JPEG、PNG 或 WebP 图片'); return Upload.LIST_IGNORE }
    if (file.size > 5 * 1024 * 1024) { message.error('单张图片不能超过 5MB'); return Upload.LIST_IGNORE }
    setImageFiles([{ uid: crypto.randomUUID(), name: file.name, size: file.size, type: file.type, status: 'done', originFileObj: file as UploadFile['originFileObj'] }]); return false
  }
  const showCreate = () => { setEditing(undefined); form.resetFields(); form.setFieldsValue({ unitName: '件', trackInventory: false, status: 'ENABLED' }); setOpen(true) }
  const showEdit = (item: ProductItem) => { setEditing(item); form.setFieldsValue({ name: item.name, unitName: item.unitName, trackInventory: item.trackInventory, status: item.status }); setOpen(true) }
  const reset = () => { setQueryText(''); setStatus(undefined) }

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>产品目录</Typography.Title><Typography.Paragraph>产品编码由系统按品牌自动升序生成且永久不变；图片可选，标准售价由最高权限账号在价格版本中统一发布。</Typography.Paragraph></div>{canManage && <Button type="primary" icon={<PlusOutlined />} onClick={showCreate}>新建产品</Button>}</div>
    <Alert type="info" showIcon title="删除仅用于从未上传图片、设置价格、进入订单或库存的误建产品；已有业务记录的产品请停用。" />
    <Card variant="borderless"><Space wrap><Input value={queryText} onChange={(event) => setQueryText(event.target.value)} allowClear placeholder="输入产品编码或名称，自动匹配" maxLength={100} style={{ width: 300 }} prefix={<SearchOutlined />} suffix={normalizedQuery !== appliedQuery || query.isFetching ? <LoadingOutlined spin /> : null} aria-label="实时查询产品" /><Select value={status} onChange={setStatus} allowClear placeholder="全部状态" style={{ width: 140 }} options={[{ value: 'ENABLED', label: '启用' }, { value: 'DISABLED', label: '停用' }]} /><Button onClick={reset}>重置</Button><Typography.Text type="secondary">输入后自动加载，无需点击查询</Typography.Text></Space></Card>
    {query.error && <Alert type="error" showIcon title={requestError(query.error)} />}
    <Card variant="borderless"><Table<ProductItem> rowKey="id" loading={query.isFetching} dataSource={query.data ?? []} pagination={{ pageSize: 10, showSizeChanger: false }} locale={{ emptyText: '没有符合条件的产品' }} scroll={{ x: 1100 }} columns={[
      { title: '图片', dataIndex: 'imageFileId', width: 76, render: (value: string | undefined, item: ProductItem) => value ? <Image width={48} height={48} style={{ objectFit: 'cover', borderRadius: 8 }} src={`/api/v1/catalog/products/${item.id}/image?v=${value}`} /> : <div style={{ width: 48, height: 48, borderRadius: 8, background: '#f3f5f7', display: 'grid', placeItems: 'center' }}><PictureOutlined /></div> },
      { title: '产品编码', dataIndex: 'code', width: 130 }, { title: '产品名称', dataIndex: 'name', width: 170 }, { title: '计量单位', dataIndex: 'unitName', width: 90 },
      { title: '库存属性', dataIndex: 'trackInventory', width: 120, render: (value: boolean) => <Tag color={value ? 'blue' : 'default'}>{value ? '跟踪库存' : '不管理库存'}</Tag> },
      { title: '状态', dataIndex: 'status', width: 80, render: (value: string) => <Tag color={value === 'ENABLED' ? 'green' : 'default'}>{value === 'ENABLED' ? '启用' : '停用'}</Tag> },
      { title: '操作', key: 'actions', width: 370, fixed: 'right', render: (_: unknown, item: ProductItem) => canManage ? <Space size="small"><Button size="small" icon={<EditOutlined />} onClick={() => showEdit(item)}>编辑</Button><Button size="small" icon={<UploadOutlined />} onClick={() => { setImageProduct(item); setImageFiles([]) }}>{item.imageFileId ? '换图' : '图片'}</Button><Popconfirm title={item.status === 'ENABLED' ? '确认停用这个产品？' : '确认恢复这个产品？'} description={item.status === 'ENABLED' ? '停用后不能用于新业务，历史记录不受影响。' : '恢复后可重新用于新业务。'} onConfirm={() => updateStatus.mutateAsync({ item, nextStatus: item.status === 'ENABLED' ? 'DISABLED' : 'ENABLED' })}><Button size="small">{item.status === 'ENABLED' ? '停用' : '恢复'}</Button></Popconfirm><Popconfirm title="永久删除这个产品？" description="只有从未被业务引用且未上传图片的产品可以删除。" okButtonProps={{ danger: true }} onConfirm={() => remove.mutateAsync(item)}><Button size="small" danger icon={<DeleteOutlined />}>删除</Button></Popconfirm></Space> : null },
    ]} /></Card>

    <Modal title={editing ? '编辑产品' : '新建产品'} open={open} onCancel={() => { setOpen(false); setEditing(undefined) }} onOk={() => form.submit()} confirmLoading={save.isPending} okText="保存产品" cancelText="取消" destroyOnHidden>
      {save.error && <Alert type="error" showIcon title={requestError(save.error)} className="modal-alert" />}
      <Form<ProductForm> form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark="optional">{editing ? <Form.Item label="产品编码" extra="系统永久标识，创建后不可修改。"><Input value={editing.code} disabled /></Form.Item> : <Alert type="info" showIcon title="保存后系统将自动生成品牌内唯一编码，例如 PD000001。" className="modal-alert" />}<Form.Item name="name" label="产品名称" rules={[{ required: true }, { max: 120 }]}><Input maxLength={120} /></Form.Item><Form.Item name="unitName" label="计量单位" rules={[{ required: true }, { max: 20 }]}><Input maxLength={20} placeholder="件、盒、套等" /></Form.Item><Form.Item name="trackInventory" valuePropName="checked"><Checkbox>跟踪门店库存</Checkbox></Form.Item>{editing && <Form.Item name="status" label="状态" rules={[{ required: true }]}><Select options={[{ value: 'ENABLED', label: '启用' }, { value: 'DISABLED', label: '停用' }]} /></Form.Item>}<Alert type="warning" showIcon title={editing ? '产品产生订单或库存记录后，库存跟踪属性将被锁定；名称、单位和状态仍可按规则维护。' : '产品保存后可上传可选图片；跟踪库存的产品需先录入期初或收货。'} /></Form>
    </Modal>
    <Modal title={`${imageProduct?.name ?? ''} · ${imageProduct?.imageFileId ? '更换图片' : '上传图片'}`} open={Boolean(imageProduct)} onCancel={() => { setImageProduct(undefined); setImageFiles([]) }} onOk={submitImage} confirmLoading={uploadImage.isPending} okText="保存图片" destroyOnHidden>
      {uploadImage.error && <Alert type="error" showIcon title={requestError(uploadImage.error)} className="modal-alert" />}
      <Space orientation="vertical" size={16} className="full-width"><Alert type="info" showIcon title="图片可选；只允许 JPEG、PNG、WebP，单张不超过 5MB。更换图片不会影响价格、库存或历史订单。" /><Upload accept="image/jpeg,image/png,image/webp" maxCount={1} fileList={imageFiles} beforeUpload={selectImage} onRemove={() => { setImageFiles([]); return true }}><Button icon={<UploadOutlined />}>选择图片</Button></Upload></Space>
    </Modal>
  </div>
}
