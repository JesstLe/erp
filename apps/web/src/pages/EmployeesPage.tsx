import { PlusOutlined, SafetyCertificateOutlined, StopOutlined, UserOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Descriptions, Drawer, Empty, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { Employee, EmployeeRole } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface EmployeeValues { employeeNo: string; displayName: string; positionCode: string; storeIds: string[]; createLoginAccount: boolean; account?: string; initialPassword?: string; roles?: string[] }
const positionOptions = [
  { value: 'OWNER', label: '负责人' }, { value: 'STORE_MANAGER', label: '店长' },
  { value: 'FRONT_DESK', label: '前台' }, { value: 'CASHIER', label: '收银员' },
  { value: 'TECHNICIAN', label: '服务员工' }, { value: 'OTHER', label: '其他岗位' },
]
const roleColor: Record<string, string> = { OWNER: 'purple', STORE_MANAGER: 'blue', FRONT_DESK: 'cyan', CASHIER: 'green', TECHNICIAN: 'default' }

export function EmployeesPage() {
  const auth = useAuth(); const queryClient = useQueryClient(); const [form] = Form.useForm<EmployeeValues>()
  const [createOpen, setCreateOpen] = useState(false); const [selected, setSelected] = useState<Employee>()
  const employees = useQuery({ queryKey: ['employees'], queryFn: () => apiRequest<Employee[]>('/api/v1/employees') })
  const roles = useQuery({ queryKey: ['employee-roles'], queryFn: () => apiRequest<EmployeeRole[]>('/api/v1/employees/roles') })
  const loginEnabled = Form.useWatch('createLoginAccount', form)
  const onError = (error: unknown) => message.error(error instanceof ApiError ? error.message : '操作失败')
  const create = useMutation({ mutationFn: (values: EmployeeValues) => apiRequest<Employee>('/api/v1/employees', { method: 'POST', body: JSON.stringify({ ...values, account: values.createLoginAccount ? values.account : null, initialPassword: values.createLoginAccount ? values.initialPassword : null, roles: values.createLoginAccount ? values.roles : [] }) }), onSuccess: async (employee) => { message.success('员工已创建；初始密码不会回显'); setCreateOpen(false); form.resetFields(); setSelected(employee); await queryClient.invalidateQueries({ queryKey: ['employees'] }) }, onError })
  const setStatus = useMutation({ mutationFn: ({ employee, isEnabled }: { employee: Employee; isEnabled: boolean }) => apiRequest<Employee>(`/api/v1/employees/${employee.id}/account-status`, { method: 'POST', body: JSON.stringify({ isEnabled }) }), onSuccess: async (employee) => { message.success(employee.accountEnabled ? '登录账号已启用' : '登录账号已停用'); setSelected(employee); await queryClient.invalidateQueries({ queryKey: ['employees'] }) }, onError })
  const openCreate = () => { form.setFieldsValue({ createLoginAccount: true, storeIds: auth.store ? [auth.store.id] : [], positionCode: 'STORE_MANAGER', roles: ['STORE_MANAGER'] }); setCreateOpen(true) }
  const columns = [
    { title: '员工', key: 'employee', render: (_: unknown, record: Employee) => <div className="employee-cell"><span><UserOutlined /></span><div><strong>{record.displayName}</strong><Typography.Text type="secondary">{record.employeeNo}</Typography.Text></div></div> },
    { title: '岗位', dataIndex: 'positionCode', render: (value: string) => positionOptions.find((item) => item.value === value)?.label ?? value },
    { title: '所属门店', dataIndex: 'stores', render: (stores: Employee['stores']) => stores.map((store) => <Tag key={store.id} color={store.isPrimary ? 'blue' : undefined}>{store.name}</Tag>) },
    { title: '登录账号', key: 'account', render: (_: unknown, record: Employee) => record.account ? <div className="account-state"><strong>{record.account}</strong><Tag color={record.accountEnabled ? 'green' : 'default'}>{record.accountEnabled ? '可登录' : '已停用'}</Tag></div> : <Typography.Text type="secondary">未开通</Typography.Text> },
    { title: '角色', dataIndex: 'roles', render: (values: string[]) => values.length ? values.map((role) => <Tag key={role} color={roleColor[role]}>{role}</Tag>) : <Typography.Text type="secondary">无登录角色</Typography.Text> },
  ]
  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>员工与登录账号</Typography.Title><Typography.Paragraph>员工档案、登录凭据、角色和门店范围分别管理，停用账号不会删除历史业务。</Typography.Paragraph></div><Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>新增员工</Button></div>
    <Alert type="info" showIcon title="只有最高权限账号可以创建登录账号和分配角色。新账号首次登录必须先修改初始密码。" />
    <Card variant="borderless" className="table-card"><Table<Employee> rowKey="id" columns={columns} dataSource={employees.data} loading={employees.isLoading} pagination={{ pageSize: 10 }} locale={{ emptyText: <Empty description="还没有员工档案" /> }} onRow={(record) => ({ onClick: () => setSelected(record), className: 'clickable-row' })} /></Card>

    <Modal title="新增员工" width={720} open={createOpen} onCancel={() => setCreateOpen(false)} onOk={() => form.submit()} okText="创建员工" confirmLoading={create.isPending} destroyOnHidden>
      <Alert type="warning" showIcon title="初始密码仅用于本次提交，保存后不会再次显示。请通过安全方式单独告知员工。" className="modal-alert" />
      <Form<EmployeeValues> form={form} layout="vertical" onFinish={(values) => create.mutate(values)} requiredMark="optional">
        <div className="employee-form-grid"><Form.Item name="employeeNo" label="员工工号" rules={[{ required: true, message: '请输入员工工号' }, { pattern: /^[A-Za-z0-9_-]{2,32}$/, message: '仅限2-32位字母、数字、下划线或短横线' }]}><Input maxLength={32} placeholder="例如 E0002" /></Form.Item><Form.Item name="displayName" label="员工姓名" rules={[{ required: true }, { min: 2 }, { max: 100 }]}><Input maxLength={100} /></Form.Item></div>
        <div className="employee-form-grid"><Form.Item name="positionCode" label="岗位" rules={[{ required: true }]}><Select options={positionOptions} /></Form.Item><Form.Item name="storeIds" label="所属门店" rules={[{ required: true, type: 'array', min: 1, message: '至少选择一个门店' }]}><Select mode="multiple" options={auth.user?.stores.map((store) => ({ value: store.id, label: `${store.name} · ${store.code}` }))} /></Form.Item></div>
        <Form.Item name="createLoginAccount" valuePropName="checked"><Checkbox>同时开通登录账号</Checkbox></Form.Item>
        {loginEnabled && <Card size="small" className="login-account-fields"><div className="employee-form-grid"><Form.Item name="account" label="登录账号" rules={[{ required: true }, { pattern: /^[A-Za-z0-9._@-]{4,100}$/, message: '仅限4-100位字母、数字及 . _ @ -' }]}><Input maxLength={100} autoComplete="off" /></Form.Item><Form.Item name="initialPassword" label="初始密码" rules={[{ required: true }, { min: 12, message: '至少12位' }, { pattern: /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).+$/, message: '需同时包含大小写字母、数字和特殊字符' }]}><Input.Password maxLength={256} autoComplete="new-password" /></Form.Item></div><Form.Item name="roles" label="登录角色" rules={[{ required: true, type: 'array', min: 1, message: '至少选择一个角色' }]}><Select mode="multiple" options={roles.data?.map((role) => ({ value: role.code, label: `${role.name} · ${role.code}` }))} /></Form.Item></Card>}
      </Form>
    </Modal>

    <Drawer title="员工与账号详情" width={620} open={Boolean(selected)} onClose={() => setSelected(undefined)} extra={selected?.account && selected.userId !== auth.user?.id && <Popconfirm title={selected.accountEnabled ? '确认停用该登录账号？' : '确认重新启用该登录账号？'} description="员工档案和历史业务不会被删除。" okText="确认" cancelText="取消" onConfirm={() => setStatus.mutate({ employee: selected, isEnabled: !selected.accountEnabled })}><Button danger={selected.accountEnabled} icon={selected.accountEnabled ? <StopOutlined /> : <SafetyCertificateOutlined />} loading={setStatus.isPending}>{selected.accountEnabled ? '停用账号' : '启用账号'}</Button></Popconfirm>}>
      {selected && <Space orientation="vertical" size={18} className="full-width"><Descriptions bordered size="small" column={1} items={[{ key: 'name', label: '员工', children: `${selected.displayName} · ${selected.employeeNo}` }, { key: 'position', label: '岗位', children: positionOptions.find((item) => item.value === selected.positionCode)?.label ?? selected.positionCode }, { key: 'store', label: '所属门店', children: selected.stores.map((store) => <Tag key={store.id}>{store.name}{store.isPrimary ? '（主）' : ''}</Tag>) }, { key: 'account', label: '登录账号', children: selected.account ?? '未开通' }, { key: 'state', label: '账号状态', children: selected.account ? <Tag color={selected.accountEnabled ? 'green' : 'default'}>{selected.accountEnabled ? '可登录' : '已停用'}</Tag> : '不适用' }, { key: 'roles', label: '角色', children: selected.roles.length ? selected.roles.map((role) => <Tag key={role} color={roleColor[role]}>{role}</Tag>) : '无' }, { key: 'password', label: '首次改密', children: selected.mustChangePassword === undefined ? '不适用' : selected.mustChangePassword ? <Tag color="gold">待完成</Tag> : <Tag color="green">已完成</Tag> }]} /><Alert type="info" showIcon title="角色与门店范围决定可访问的数据和动作；前端隐藏按钮不能替代服务端鉴权。" /></Space>}
    </Drawer>
  </div>
}
