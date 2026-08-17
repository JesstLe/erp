import { PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Form, Input, Modal, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { ProductItem } from '../api/types'

interface ProductForm { code: string; name: string; unitName: string; trackInventory: boolean }

export function ProductsPage() {
  const [open, setOpen] = useState(false); const [form] = Form.useForm<ProductForm>(); const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['product-items'], queryFn: () => apiRequest<ProductItem[]>('/api/v1/catalog/products') })
  const create = useMutation({ mutationFn: (values: ProductForm) => apiRequest<ProductItem>('/api/v1/catalog/products', { method: 'POST', body: JSON.stringify(values) }), onSuccess: async () => { message.success('产品档案已创建，请在价格版本中设置标准价'); setOpen(false); form.resetFields(); await queryClient.invalidateQueries({ queryKey: ['product-items'] }) } })
  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>产品目录</Typography.Title><Typography.Paragraph>维护产品名称、单位和库存属性；标准售价由最高权限账号在价格版本中统一发布。</Typography.Paragraph></div><Button type="primary" icon={<PlusOutlined />} onClick={() => { form.setFieldsValue({ unitName: '件', trackInventory: false }); setOpen(true) }}>新建产品</Button></div>
    <Alert type="info" showIcon title="第一版只建立产品目录和版本化标准价。库存属性是后续库存模块的预留标记，当前不会产生库存余额、出入库或销售记录。" />
    {query.error && <Alert type="error" showIcon title={query.error instanceof Error ? query.error.message : '加载失败'} />}
    <Card variant="borderless"><Table<ProductItem> rowKey="id" loading={query.isLoading} dataSource={query.data ?? []} pagination={{ pageSize: 10, showSizeChanger: false }} locale={{ emptyText: '还没有产品，请先新建产品档案' }} columns={[{ title: '产品编码', dataIndex: 'code', width: 160 }, { title: '产品名称', dataIndex: 'name' }, { title: '计量单位', dataIndex: 'unitName', width: 120 }, { title: '库存属性', dataIndex: 'trackInventory', width: 150, render: (value: boolean) => <Tag color={value ? 'blue' : 'default'}>{value ? '后续纳入库存' : '不管理库存'}</Tag> }, { title: '状态', dataIndex: 'status', width: 120, render: (value: string) => <Tag color={value === 'ENABLED' ? 'green' : 'default'}>{value === 'ENABLED' ? '启用' : '停用'}</Tag> }]} /></Card>
    <Modal title="新建产品" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} confirmLoading={create.isPending} okText="保存产品" cancelText="取消" destroyOnHidden>
      {create.error && <Alert type="error" showIcon title={create.error instanceof ApiError ? create.error.message : '保存失败'} className="modal-alert" />}
      <Form<ProductForm> form={form} layout="vertical" onFinish={(values) => create.mutate(values)} requiredMark="optional"><Form.Item name="code" label="产品编码" rules={[{ required: true }, { max: 40 }]}><Input maxLength={40} placeholder="例如 PD001" /></Form.Item><Form.Item name="name" label="产品名称" rules={[{ required: true }, { max: 120 }]}><Input maxLength={120} /></Form.Item><Form.Item name="unitName" label="计量单位" rules={[{ required: true }, { max: 20 }]}><Input maxLength={20} placeholder="件、盒、套等" /></Form.Item><Form.Item name="trackInventory" valuePropName="checked"><Checkbox>标记为后续需要管理库存</Checkbox></Form.Item><Alert type="warning" showIcon title="该勾选项不代表库存功能已经启用；当前不会生成库存余额。" /></Form>
    </Modal>
  </div>
}
