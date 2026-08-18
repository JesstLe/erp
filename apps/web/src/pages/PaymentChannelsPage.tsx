import { CloudDownloadOutlined, CloudServerOutlined, EditOutlined, SafetyCertificateOutlined } from '@ant-design/icons'
import { Alert, Button, Card, DatePicker, Descriptions, Empty, Form, Input, Modal, Select, Space, Switch, Table, Tag, Tooltip, Typography, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useState } from 'react'
import { ApiError, apiRequest } from '../api/client'
import type { PageResult, PaymentChannelConfiguration, PaymentChannelReconciliationItem, PaymentChannelReconciliationRun } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

interface ChannelValues { environment: string; displayName: string; credentialProfile: string; isEnabled: boolean }
interface ResolutionValues { reason: string }
const providers = [
  { code: 'WeChatPay', name: '微信支付', mode: 'Native 二维码', defaultProfile: 'PRIMARY_WECHAT' },
  { code: 'Alipay', name: '支付宝', mode: '订单码支付', defaultProfile: 'PRIMARY_ALIPAY' },
]
const runStatus: Record<string, { label: string; color: string }> = {
  Running: { label: '执行中', color: 'processing' }, Matched: { label: '全部匹配', color: 'success' },
  Differences: { label: '存在差异', color: 'warning' }, Failed: { label: '执行失败', color: 'error' },
}
const itemStatus: Record<string, { label: string; color: string }> = {
  Matched: { label: '匹配', color: 'success' }, LocalOnly: { label: '仅本地', color: 'warning' },
  ChannelOnly: { label: '仅渠道', color: 'error' }, AmountMismatch: { label: '金额不一致', color: 'error' },
  StateMismatch: { label: '状态不一致', color: 'warning' }, Resolved: { label: '已人工处置', color: 'default' },
}
const money = (minor?: number) => minor === undefined ? '—' : `¥${(minor / 100).toFixed(2)}`

export function PaymentChannelsPage() {
  const auth = useAuth(); const queryClient = useQueryClient(); const [form] = Form.useForm<ChannelValues>(); const [resolutionForm] = Form.useForm<ResolutionValues>()
  const [editing, setEditing] = useState<(typeof providers)[number]>(); const [businessDate, setBusinessDate] = useState<Dayjs>(dayjs().subtract(1, 'day')); const [resolving, setResolving] = useState<PaymentChannelReconciliationItem>()
  const [page, setPage] = useState(1); const pageSize = 10
  const { can } = useAuthorization(); const storeId = auth.store?.id; const canManage = can(Permission.PaymentChannelManage); const dateText = businessDate.format('YYYY-MM-DD')
  const configurations = useQuery({ queryKey: ['payment-channel-configurations', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<PaymentChannelConfiguration[]>(`/api/v1/payment-channels/configurations?storeId=${storeId}`) })
  useEffect(() => setPage(1), [storeId, dateText])
  const reconciliations = useQuery({ queryKey: ['payment-channel-reconciliations', storeId, dateText, page], enabled: Boolean(storeId), queryFn: () => apiRequest<PageResult<PaymentChannelReconciliationRun>>(`/api/v1/payment-channels/reconciliations?storeId=${storeId}&fromDate=${dateText}&toDate=${dateText}&page=${page}&pageSize=${pageSize}`), refetchInterval: (query) => query.state.data?.items.some((run) => run.status === 'Running') ? 5_000 : false })
  const selected = configurations.data?.find((item) => item.provider === editing?.code)
  const onError = (error: unknown) => message.error(error instanceof ApiError ? error.message : '请求失败，请稍后重试')
  const save = useMutation({ mutationFn: (values: ChannelValues) => apiRequest<PaymentChannelConfiguration>(`/api/v1/payment-channels/configurations/${editing?.code}`, { method: 'PUT', body: JSON.stringify({ storeId, ...values, expectedVersion: selected?.version ?? 0 }) }), onSuccess: async () => { message.success('渠道配置映射已保存'); setEditing(undefined); await queryClient.invalidateQueries({ queryKey: ['payment-channel-configurations', storeId] }) }, onError })
  const runReconciliation = useMutation({ mutationFn: (provider: string) => apiRequest<PaymentChannelReconciliationRun>('/api/v1/payment-channels/reconciliations/run', { method: 'POST', body: JSON.stringify({ storeId, provider, businessDate: dateText }) }), onSuccess: async (run) => { message.success(run.differenceCount ? `对账完成，发现 ${run.differenceCount} 项差异` : '对账完成，渠道与本地账务一致'); await queryClient.invalidateQueries({ queryKey: ['payment-channel-reconciliations', storeId, dateText] }) }, onError })
  const resolve = useMutation({ mutationFn: (values: ResolutionValues) => apiRequest<PaymentChannelReconciliationItem>(`/api/v1/payment-channels/reconciliations/items/${resolving?.id}/resolve`, { method: 'POST', body: JSON.stringify({ storeId, expectedVersion: resolving?.version, reason: values.reason }) }), onSuccess: async () => { message.success('差异已标记为人工处置，原账务金额和状态未被修改'); setResolving(undefined); resolutionForm.resetFields(); await queryClient.invalidateQueries({ queryKey: ['payment-channel-reconciliations', storeId, dateText] }) }, onError })
  const open = (provider: (typeof providers)[number]) => { const item = configurations.data?.find((configuration) => configuration.provider === provider.code); form.setFieldsValue({ environment: item?.environment ?? (provider.code === 'Alipay' ? 'Sandbox' : 'Production'), displayName: item?.displayName ?? `${provider.name}${provider.mode}`, credentialProfile: item?.credentialProfile ?? provider.defaultProfile, isEnabled: item?.isEnabled ?? false }); setEditing(provider) }

  const itemColumns: ColumnsType<PaymentChannelReconciliationItem> = [
    { title: '类型/商户单号', key: 'business', width: 240, render: (_, item) => <div className="audit-action"><strong>{item.itemType === 'Payment' ? '支付' : '退款'}</strong><Typography.Text copyable>{item.outRefundNo ?? item.outTradeNo}</Typography.Text></div> },
    { title: '结果', dataIndex: 'status', width: 120, render: (status: string) => <Tag color={itemStatus[status]?.color}>{itemStatus[status]?.label ?? status}</Tag> },
    { title: '本地金额/状态', key: 'local', render: (_, item) => <div className="audit-action"><strong>{money(item.localAmountMinor)}</strong><Typography.Text type="secondary">{item.localStatus ?? '本地无记录'}</Typography.Text></div> },
    { title: '渠道金额/状态', key: 'channel', render: (_, item) => <div className="audit-action"><strong>{money(item.channelAmountMinor)}</strong><Typography.Text type="secondary">{item.channelStatus ?? '渠道无记录'}</Typography.Text></div> },
    { title: '处置', key: 'action', width: 145, render: (_, item) => item.status === 'Resolved' ? <Tooltip title={item.resolutionReason}><Tag>已处置</Tag></Tooltip> : item.status === 'Matched' ? '—' : canManage ? <Button size="small" onClick={() => { resolutionForm.resetFields(); setResolving(item) }}>登记处置</Button> : <Tag color="gold">等待最高权限</Tag> },
  ]
  const runColumns: ColumnsType<PaymentChannelReconciliationRun> = [
    { title: '渠道', dataIndex: 'provider', render: (value: string) => value === 'WeChatPay' ? '微信支付' : '支付宝' },
    { title: '账单日', dataIndex: 'businessDate' },
    { title: '批次', dataIndex: 'attemptNo', render: (value: number) => `第 ${value} 次` },
    { title: '结果', dataIndex: 'status', render: (status: string) => <Tag color={runStatus[status]?.color}>{runStatus[status]?.label ?? status}</Tag> },
    { title: '渠道条目', dataIndex: 'channelEntryCount' }, { title: '匹配', dataIndex: 'matchedCount' },
    { title: '差异', dataIndex: 'differenceCount', render: (value: number) => <Typography.Text type={value ? 'danger' : undefined}>{value}</Typography.Text> },
    { title: '完成时间', dataIndex: 'completedAtUtc', render: (value?: string) => value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—' },
  ]

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>支付渠道配置与对账</Typography.Title><Typography.Paragraph>密钥只保存在服务器；渠道账单只保留 SHA-256 摘要和业务差异，不保存原始账单。</Typography.Paragraph></div></div>
    <Alert type="warning" showIcon title="真实渠道未完成商户联调前请保持停用。系统不会把人工微信/支付宝登记自动升级为渠道成功。" />
    <div className="metric-grid">
      {providers.map((provider) => { const item = configurations.data?.find((configuration) => configuration.provider === provider.code); return <Card key={provider.code} title={<Space><CloudServerOutlined />{provider.name}<Tag>{provider.mode}</Tag></Space>} extra={canManage && <Button icon={<EditOutlined />} onClick={() => open(provider)}>配置</Button>}>
        {!item ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="尚未建立门店配置映射" /> : <Space orientation="vertical" size={16} className="full-width">
          <Descriptions size="small" column={1} items={[{ key: 'name', label: '显示名称', children: item.displayName }, { key: 'environment', label: '接口环境', children: <Tag color={item.environment === 'Production' ? 'purple' : 'blue'}>{item.environment === 'Production' ? '生产' : '沙箱'}</Tag> }, { key: 'profile', label: '凭据配置名', children: <Typography.Text code>{item.credentialProfile}</Typography.Text> }, { key: 'credentials', label: '服务器凭据', children: <Tag color={item.credentialsPresent ? 'green' : 'red'}>{item.credentialsPresent ? '结构完整' : '缺少配置'}</Tag> }, { key: 'enabled', label: '门店状态', children: <Tag color={item.isEnabled ? 'green' : 'default'}>{item.isEnabled ? '已启用' : '已停用'}</Tag> }]} />
          {!item.credentialsPresent && <Alert type="error" showIcon title="服务器环境仍缺少必要配置" description={item.missingRequirements.join('、')} />}
        </Space>}
      </Card> })}
    </div>
    <Alert type="info" showIcon icon={<SafetyCertificateOutlined />} title="启用时服务端会重新检查商户号、应用号、HTTPS 回调地址、密钥长度以及密钥文件是否存在；任一项不满足都拒绝启用。" />

    <Card title={<Space><CloudDownloadOutlined />渠道账单自动对账</Space>} extra={<Space><Typography.Text type="secondary">账单日</Typography.Text><DatePicker value={businessDate} allowClear={false} onChange={(value) => value && setBusinessDate(value)} disabledDate={(value) => value.startOf('day').isAfter(dayjs().subtract(1, 'day').endOf('day')) || value.startOf('day').isBefore(dayjs().subtract(90, 'day').startOf('day'))} /></Space>}>
      <Alert type="warning" showIcon title="对账只发现并记录差异，不会自动补记收款、退款或改动会员余额。退款最终状态仍以渠道查单结果为准。" className="modal-alert" />
      <Space className="reconciliation-actions" wrap>
        {providers.map((provider) => { const configuration = configurations.data?.find((item) => item.provider === provider.code); const unavailable = !canManage || !configuration?.credentialsPresent; const reason = !canManage ? '只有最高权限账号可以执行对账' : !configuration ? '尚未建立渠道配置' : !configuration.credentialsPresent ? '服务器凭据不完整' : ''; return <Tooltip key={provider.code} title={unavailable ? reason : ''}><span><Button icon={<CloudDownloadOutlined />} disabled={unavailable} loading={runReconciliation.isPending && runReconciliation.variables === provider.code} onClick={() => runReconciliation.mutate(provider.code)}>下载并核对{provider.name}账单</Button></span></Tooltip> })}
      </Space>
      <Table rowKey="id" loading={reconciliations.isLoading} dataSource={reconciliations.data?.items} columns={runColumns} pagination={{ current: page, pageSize, total: reconciliations.data?.total ?? 0, showSizeChanger: false, showTotal: (total) => `共 ${total} 个批次`, onChange: setPage }} locale={{ emptyText: '该账单日还没有对账记录' }} expandable={{ expandedRowRender: (run) => run.failureCode ? <Alert type="error" showIcon title={`执行失败：${run.failureCode}`} /> : <Table rowKey="id" size="small" dataSource={run.items} columns={itemColumns} pagination={false} locale={{ emptyText: '没有需要人工处理的差异，匹配数量见批次汇总' }} /> }} />
    </Card>

    <Modal title={`配置${editing?.name ?? ''}`} open={Boolean(editing)} onCancel={() => setEditing(undefined)} onOk={() => form.submit()} okText="保存配置" confirmLoading={save.isPending} destroyOnHidden>
      <Alert type="info" showIcon title="凭据配置名对应服务器环境变量中的安全配置；页面不会读取私钥内容。" className="modal-alert" />
      <Form<ChannelValues> form={form} layout="vertical" onFinish={(values) => save.mutate(values)}>
        <Form.Item name="displayName" label="收银台显示名称" rules={[{ required: true }, { max: 80 }]}><Input maxLength={80} /></Form.Item>
        <Form.Item name="environment" label="接口环境" rules={[{ required: true }]}><Select options={[{ value: 'Sandbox', label: '沙箱/联调环境' }, { value: 'Production', label: '生产环境' }]} /></Form.Item>
        <Form.Item name="credentialProfile" label="服务器凭据配置名" rules={[{ required: true }, { pattern: /^[A-Z][A-Z0-9_]{2,39}$/, message: '3-40位大写字母、数字或下划线，且以字母开头' }]}><Input maxLength={40} placeholder={editing?.defaultProfile} /></Form.Item>
        <Form.Item name="isEnabled" label="门店启用" valuePropName="checked"><Switch checkedChildren="启用" unCheckedChildren="停用" /></Form.Item>
      </Form>
    </Modal>
    <Modal title={`处置对账差异 · ${resolving?.outRefundNo ?? resolving?.outTradeNo ?? ''}`} open={Boolean(resolving)} onCancel={() => setResolving(undefined)} onOk={() => resolutionForm.submit()} okText="确认登记" confirmLoading={resolve.isPending} destroyOnHidden>
      <Alert type="warning" showIcon title="该操作只记录已人工核对，不会修改原支付、退款或会员账务。需要补账或冲正时必须走独立审批流程。" className="modal-alert" />
      <Descriptions size="small" column={2} bordered items={[{ key: 'local', label: '本地金额', children: money(resolving?.localAmountMinor) }, { key: 'channel', label: '渠道金额', children: money(resolving?.channelAmountMinor) }, { key: 'localStatus', label: '本地状态', children: resolving?.localStatus ?? '无记录' }, { key: 'channelStatus', label: '渠道状态', children: resolving?.channelStatus ?? '无记录' }]} />
      <Form<ResolutionValues> form={resolutionForm} layout="vertical" onFinish={(values) => resolve.mutate(values)} className="resolution-form"><Form.Item name="reason" label="核对结果与处置说明" rules={[{ required: true, whitespace: true }, { min: 2 }, { max: 500 }]}><Input.TextArea rows={4} maxLength={500} showCount /></Form.Item></Form>
    </Modal>
  </div>
}
