import { AuditOutlined, LogoutOutlined, SafetyCertificateOutlined, ShopOutlined, UserAddOutlined } from '@ant-design/icons'
import { App, Button, Card, DatePicker, Form, Input, Layout, Modal, Select, Space, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Navigate, useNavigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { apiRequest, ApiError, resetCsrfToken } from '../api/client'
import type { LoginSecurityEvent, MerchantRegistrationApplication, PlatformCurrentUser, PlatformMerchant, PlatformPage } from '../api/types'
import { useDebouncedValue } from '../hooks/useDebouncedValue'

const requestError = (error: unknown) => error instanceof ApiError ? error.message : '操作失败，请稍后重试'
const time = (value?: string) => value ? new Date(value).toLocaleString() : '—'
const pageSize = 20

interface ApprovalValues { tenantCode: string; storeCode: string; initialPassword: string; reason: string }
interface ReasonValues { reason: string }

export function PlatformAdminPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [registrationStatus, setRegistrationStatus] = useState<string>()
  const [registrationQuery, setRegistrationQuery] = useState('')
  const appliedRegistrationQuery = useDebouncedValue(registrationQuery.trim())
  const [registrationPage, setRegistrationPage] = useState(1)
  const [merchantStatus, setMerchantStatus] = useState<string>()
  const [merchantQuery, setMerchantQuery] = useState('')
  const appliedMerchantQuery = useDebouncedValue(merchantQuery.trim())
  const [merchantPage, setMerchantPage] = useState(1)
  const [eventScope, setEventScope] = useState<string>()
  const [eventResult, setEventResult] = useState<string>()
  const [eventAccount, setEventAccount] = useState('')
  const appliedEventAccount = useDebouncedValue(eventAccount.trim())
  const [eventDates, setEventDates] = useState<[string, string]>()
  const [eventPage, setEventPage] = useState(1)
  const [approval, setApproval] = useState<MerchantRegistrationApplication>()
  const [rejection, setRejection] = useState<MerchantRegistrationApplication>()
  const [statusMerchant, setStatusMerchant] = useState<PlatformMerchant>()
  const [approvalForm] = Form.useForm<ApprovalValues>()
  const [reasonForm] = Form.useForm<ReasonValues>()
  const [statusForm] = Form.useForm<ReasonValues>()

  useEffect(() => setRegistrationPage(1), [appliedRegistrationQuery])
  useEffect(() => setMerchantPage(1), [appliedMerchantQuery])
  useEffect(() => setEventPage(1), [appliedEventAccount])

  const me = useQuery({
    queryKey: ['platform-me'],
    queryFn: ({ signal }) => apiRequest<PlatformCurrentUser>('/api/v1/platform/auth/me', { signal }),
    retry: false,
  })
  const enabled = !!me.data && !me.data.mustChangePassword
  const registrationParams = new URLSearchParams({ page: String(registrationPage), pageSize: String(pageSize) })
  if (registrationStatus) registrationParams.set('status', registrationStatus)
  if (appliedRegistrationQuery) registrationParams.set('query', appliedRegistrationQuery)
  const merchantParams = new URLSearchParams({ page: String(merchantPage), pageSize: String(pageSize) })
  if (merchantStatus) merchantParams.set('status', merchantStatus)
  if (appliedMerchantQuery) merchantParams.set('query', appliedMerchantQuery)
  const eventParams = new URLSearchParams({ page: String(eventPage), pageSize: String(pageSize) })
  if (eventScope) eventParams.set('scope', eventScope)
  if (eventResult) eventParams.set('resultCode', eventResult)
  if (appliedEventAccount) eventParams.set('account', appliedEventAccount)
  if (eventDates) {
    eventParams.set('fromDate', eventDates[0])
    eventParams.set('toDate', eventDates[1])
  }
  const registrations = useQuery({
    queryKey: ['platform-registrations', registrationStatus, appliedRegistrationQuery, registrationPage],
    enabled,
    queryFn: ({ signal }) => apiRequest<PlatformPage<MerchantRegistrationApplication>>(
      `/api/v1/platform/registration-applications?${registrationParams}`, { signal }),
  })
  const merchants = useQuery({
    queryKey: ['platform-merchants', merchantStatus, appliedMerchantQuery, merchantPage],
    enabled,
    queryFn: ({ signal }) => apiRequest<PlatformPage<PlatformMerchant>>(
      `/api/v1/platform/merchants?${merchantParams}`, { signal }),
  })
  const events = useQuery({
    queryKey: ['platform-security-events', eventScope, eventResult, appliedEventAccount, eventDates, eventPage],
    enabled,
    queryFn: ({ signal }) => apiRequest<PlatformPage<LoginSecurityEvent>>(
      `/api/v1/platform/security-events?${eventParams}`, { signal }),
  })
  const refresh = async (text: string) => {
    message.success(text)
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['platform-registrations'] }),
      queryClient.invalidateQueries({ queryKey: ['platform-merchants'] }),
      queryClient.invalidateQueries({ queryKey: ['platform-security-events'] }),
    ])
  }
  const approveMutation = useMutation({
    mutationFn: (values: ApprovalValues) => apiRequest(
      `/api/v1/platform/registration-applications/${approval!.id}/approval`,
      { method: 'POST', body: JSON.stringify({ ...values, expectedVersion: approval!.version }) }),
    onSuccess: async () => {
      setApproval(undefined)
      approvalForm.resetFields()
      await refresh('商户已创建，初始密码请通过安全渠道线下交付')
    },
    onError: (error) => message.error(requestError(error)),
  })
  const rejectMutation = useMutation({
    mutationFn: (values: ReasonValues) => apiRequest(
      `/api/v1/platform/registration-applications/${rejection!.id}/rejection`,
      { method: 'POST', body: JSON.stringify({ ...values, expectedVersion: rejection!.version }) }),
    onSuccess: async () => {
      setRejection(undefined)
      reasonForm.resetFields()
      await refresh('申请已驳回')
    },
    onError: (error) => message.error(requestError(error)),
  })
  const statusMutation = useMutation({
    mutationFn: (values: ReasonValues) => apiRequest(
      `/api/v1/platform/merchants/${statusMerchant!.id}/status-change`,
      { method: 'POST', body: JSON.stringify({ enable: statusMerchant!.status !== 'Enabled', reason: values.reason, expectedVersion: statusMerchant!.version }) }),
    onSuccess: async () => {
      setStatusMerchant(undefined)
      statusForm.resetFields()
      await refresh('商户状态已更新')
    },
    onError: (error) => message.error(requestError(error)),
  })
  if (me.isLoading) return <div className="screen-loader">正在验证平台会话…</div>
  if (me.isError) return <Navigate to="/platform/login" replace />
  if (me.data?.mustChangePassword) return <Navigate to="/platform/change-password" replace />
  const logout = async () => {
    await apiRequest('/api/v1/platform/auth/logout', { method: 'POST' })
    resetCsrfToken()
    queryClient.removeQueries({ queryKey: ['platform-me'] })
    navigate('/platform/login', { replace: true })
  }

  const registrationView = <Card variant="borderless" title="商户注册申请" extra={<Space wrap>
    <Input allowClear value={registrationQuery} placeholder="输入即查：申请号、商户、门店或账号"
      onChange={(event) => setRegistrationQuery(event.target.value)} style={{ width: 280 }} />
    <Select allowClear placeholder="全部状态" value={registrationStatus} onChange={(value) => {
      setRegistrationStatus(value); setRegistrationPage(1)
    }} options={[{ value: 'PendingReview', label: '待审核' }, { value: 'Approved', label: '已批准' }, { value: 'Rejected', label: '已驳回' }]} />
  </Space>}><Table rowKey="id" loading={registrations.isLoading} dataSource={registrations.data?.items}
    pagination={{ current: registrationPage, pageSize, total: registrations.data?.total, showSizeChanger: false, onChange: setRegistrationPage }}
    columns={[
      { title: '申请号', dataIndex: 'applicationNo' },
      { title: '商户/首店', render: (_: unknown, row: MerchantRegistrationApplication) => <>{row.merchantName}<Typography.Text type="secondary"><br />{row.storeName}</Typography.Text></> },
      { title: '联系人', render: (_: unknown, row: MerchantRegistrationApplication) => <>{row.contactName}<br />{row.maskedMobile}</> },
      { title: '负责人账号', dataIndex: 'desiredOwnerAccount' },
      { title: '提交时间', dataIndex: 'createdAtUtc', render: time },
      { title: '状态', dataIndex: 'status', render: (value: string) => <Tag color={value === 'PendingReview' ? 'gold' : value === 'Approved' ? 'green' : 'red'}>{value}</Tag> },
      { title: '操作', render: (_: unknown, row: MerchantRegistrationApplication) => row.status === 'PendingReview' ? <Space><Button type="primary" size="small" onClick={() => { setApproval(row); approvalForm.setFieldsValue({ tenantCode: '', storeCode: 'S01', initialPassword: '', reason: '资料审核通过' }) }}>批准</Button><Button danger size="small" onClick={() => setRejection(row)}>驳回</Button></Space> : row.reviewReason ?? '—' },
    ]} /></Card>
  const merchantView = <Card variant="borderless" title="全部商户" extra={<Space wrap>
    <Input allowClear value={merchantQuery} placeholder="输入即查：商户名称或编码"
      onChange={(event) => setMerchantQuery(event.target.value)} />
    <Select allowClear placeholder="全部状态" value={merchantStatus} onChange={(value) => {
      setMerchantStatus(value); setMerchantPage(1)
    }} options={[{ value: 'Enabled', label: '启用' }, { value: 'Disabled', label: '停用' }]} />
  </Space>}><Table rowKey="id" loading={merchants.isLoading} dataSource={merchants.data?.items}
    pagination={{ current: merchantPage, pageSize, total: merchants.data?.total, showSizeChanger: false, onChange: setMerchantPage }}
    columns={[
      { title: '编码', dataIndex: 'code' }, { title: '商户名称', dataIndex: 'name' },
      { title: '状态', dataIndex: 'status', render: (value: string) => <Tag color={value === 'Enabled' ? 'green' : 'red'}>{value}</Tag> },
      { title: '门店', dataIndex: 'storeCount' }, { title: '员工', dataIndex: 'employeeCount' },
      { title: '登录账号', dataIndex: 'loginAccountCount' }, { title: '创建时间', dataIndex: 'createdAtUtc', render: time },
      { title: '操作', render: (_: unknown, row: PlatformMerchant) => <Button danger={row.status === 'Enabled'} size="small" onClick={() => setStatusMerchant(row)}>{row.status === 'Enabled' ? '停用商户' : '恢复商户'}</Button> },
    ]} /></Card>
  const securityView = <Card variant="borderless" title="登录安全事件" extra={<Space wrap>
    <Input allowClear value={eventAccount} placeholder="输入完整账号即查" onChange={(event) => setEventAccount(event.target.value)} />
    <Select allowClear placeholder="全部范围" value={eventScope} onChange={(value) => {
      setEventScope(value); setEventPage(1)
    }} options={[{ value: 'Merchant', label: '商户账号' }, { value: 'Platform', label: '平台账号' }]} />
    <Select allowClear placeholder="全部结果" value={eventResult} onChange={(value) => {
      setEventResult(value); setEventPage(1)
    }} options={[{ value: 'SUCCESS', label: '成功' }, { value: 'INVALID_CREDENTIALS', label: '凭据错误' }, { value: 'ACCOUNT_LOCKED', label: '账号锁定' }, { value: 'TENANT_DISABLED', label: '商户停用' }]} />
    <DatePicker.RangePicker onChange={(_, values) => {
      setEventDates(values[0] && values[1] ? [values[0], values[1]] : undefined); setEventPage(1)
    }} />
  </Space>}><Table rowKey="id" loading={events.isLoading} dataSource={events.data?.items}
    pagination={{ current: eventPage, pageSize, total: events.data?.total, showSizeChanger: false, onChange: setEventPage }}
    columns={[
      { title: '时间', dataIndex: 'occurredAtUtc', render: time },
      { title: '范围', dataIndex: 'scope', render: (value: string) => <Tag>{value}</Tag> },
      { title: '事件', dataIndex: 'eventType' },
      { title: '结果', dataIndex: 'resultCode', render: (value: string) => <Tag color={value === 'SUCCESS' ? 'green' : 'red'}>{value}</Tag> },
      { title: '商户', dataIndex: 'tenantName', render: (value?: string) => value ?? '平台' },
      { title: '账号', dataIndex: 'account' }, { title: 'IP', dataIndex: 'ipAddress' },
      { title: '设备摘要', dataIndex: 'userAgentSummary', ellipsis: true },
      { title: '追踪号', dataIndex: 'traceId', ellipsis: true },
    ]} /></Card>
  return <Layout className="platform-shell">
    <Layout.Header className="platform-header"><Space><SafetyCertificateOutlined /><Typography.Title level={4}>ERP 平台管理中心</Typography.Title></Space><Space><Typography.Text>{me.data?.displayName} · PLATFORM_ADMIN</Typography.Text><Button icon={<LogoutOutlined />} onClick={logout}>退出</Button></Space></Layout.Header>
    <Layout.Content className="platform-content"><Tabs items={[
      { key: 'registrations', label: <span><UserAddOutlined />注册审核</span>, children: registrationView },
      { key: 'merchants', label: <span><ShopOutlined />全部商户</span>, children: merchantView },
      { key: 'security', label: <span><AuditOutlined />登录日志</span>, children: securityView },
    ]} /></Layout.Content>
    <Modal title={`批准申请：${approval?.merchantName ?? ''}`} open={!!approval} onCancel={() => setApproval(undefined)} onOk={() => approvalForm.submit()} confirmLoading={approveMutation.isPending} destroyOnHidden><Form form={approvalForm} layout="vertical" onFinish={(values) => approveMutation.mutate(values)}><Form.Item name="tenantCode" label="商户编码" rules={[{ required: true }, { pattern: /^[A-Z0-9_-]{2,32}$/ }]}><Input placeholder="例如 B002" /></Form.Item><Form.Item name="storeCode" label="首店编码" rules={[{ required: true }, { pattern: /^[A-Z0-9_-]{2,32}$/ }]}><Input /></Form.Item><Form.Item name="initialPassword" label="负责人初始密码" extra="保存后不再回显；至少12位，包含大小写、数字和特殊字符" rules={[{ required: true }, { min: 12 }]}><Input.Password /></Form.Item><Form.Item name="reason" label="审批原因" rules={[{ required: true }, { min: 2, max: 500 }]}><Input.TextArea /></Form.Item></Form></Modal>
    <Modal title={`驳回申请：${rejection?.merchantName ?? ''}`} open={!!rejection} onCancel={() => setRejection(undefined)} onOk={() => reasonForm.submit()} confirmLoading={rejectMutation.isPending} okButtonProps={{ danger: true }} destroyOnHidden><Form form={reasonForm} layout="vertical" onFinish={(values) => rejectMutation.mutate(values)}><Form.Item name="reason" label="驳回原因" rules={[{ required: true }, { min: 2, max: 500 }]}><Input.TextArea /></Form.Item></Form></Modal>
    <Modal title={`${statusMerchant?.status === 'Enabled' ? '停用' : '恢复'}商户：${statusMerchant?.name ?? ''}`} open={!!statusMerchant} onCancel={() => setStatusMerchant(undefined)} onOk={() => statusForm.submit()} confirmLoading={statusMutation.isPending} okButtonProps={{ danger: statusMerchant?.status === 'Enabled' }} destroyOnHidden><Form form={statusForm} layout="vertical" onFinish={(values) => statusMutation.mutate(values)}><Form.Item name="reason" label="操作原因" rules={[{ required: true }, { min: 2, max: 500 }]}><Input.TextArea /></Form.Item></Form></Modal>
  </Layout>
}
