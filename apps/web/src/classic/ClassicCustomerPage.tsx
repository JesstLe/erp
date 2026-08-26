import {
  DeleteOutlined,
  DownloadOutlined,
  EditOutlined,
  FileExcelOutlined,
  ImportOutlined,
  MenuOutlined,
  PlusOutlined,
  PrinterOutlined,
  ReloadOutlined,
  SearchOutlined,
  SettingOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button, Form, Input, Modal, Pagination, Select, Spin, message } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Cell, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useNavigate } from 'react-router-dom'
import { apiDownload, apiRequest, ApiError } from '../api/client'
import type { CustomerDetail, CustomerSummary, MemberCardType, PageResult } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

const customerManagement = [
  ['顾客信息', '/ui/new/customer/list'],
  ['顾客开卡', '/ui/new/legacy/customer/customer-002'],
  ['顾客储值', '/ui/new/legacy/customer/customer-003'],
  ['顾客预约', '/ui/new/legacy/customer/customer-004'],
  ['顾客护理', '/ui/new/legacy/customer/customer-005'],
  ['消费退货', '/ui/new/legacy/customer/customer-006'],
  ['储值退款', '/ui/new/legacy/customer/customer-007'],
  ['次卡退卡', '/ui/new/legacy/customer/customer-008'],
  ['积分增减', '/ui/new/legacy/customer/customer-009'],
  ['兑换储值', '/ui/new/legacy/customer/customer-010'],
  ['兑换礼品', '/ui/new/legacy/customer/customer-011'],
] as const

const customerReports = [
  '顾客消费单查询', '顾客储值单查询', '次卡销售单查询', '顾客欠款单查询', '顾客还款单查询',
  '积分增减单查询', '积分兑换储值查询', '积分兑换礼品查询', '顾客剩余卡次查询', '顾客消费赠送查询',
  '顾客积分变动查询', '顾客分销佣金汇总表', '顾客分销佣金明细表',
].map((label, index) => [label, `/ui/new/legacy/customer/customer-${String(index + 12).padStart(3, '0')}`] as const)

const genderLabels: Record<string, string> = { Male: '男', Female: '女', Other: '其他', Unknown: '未填写' }
const statusLabels: Record<string, string> = { Active: '正常', Disabled: '停用', Lost: '挂失', Merged: '已合并' }
const chartColors = ['#78ace2', '#83c988', '#efb25a', '#9c74cb', '#74c5c8', '#e4808d']

function commandId() {
  return crypto.randomUUID()
}

function formatTime(value?: string) {
  if (!value) return '—'
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}

function onRequestError(error: unknown) {
  message.error(error instanceof ApiError ? error.message : '操作失败')
}

function ClassicCustomerQuickGroup({ title, items }: { title: string; items: readonly (readonly [string, string])[] }) {
  const navigate = useNavigate()
  const [expanded, setExpanded] = useState(false)
  const visible = expanded ? items : items.slice(0, 5)
  return <section className="classic-quick-group classic-customer-quick">
    <h2>{title}</h2>
    {visible.map(([label, path]) => <button key={label} type="button" onClick={() => navigate(path)}><SearchOutlined /><span>{label}</span></button>)}
    {items.length > 5 && <button className="classic-more" type="button" onClick={() => setExpanded((value) => !value)}><span>{expanded ? '收起' : title === '顾客管理' ? '查看更多功能' : '查看更多报表'}</span><MenuOutlined /></button>}
  </section>
}

export function ClassicCustomerDashboard() {
  const auth = useAuth()
  const storeId = auth.store?.id
  const navigate = useNavigate()
  const customers = useQuery({
    queryKey: ['classic-customer-latest', storeId],
    enabled: Boolean(storeId),
    queryFn: () => apiRequest<PageResult<CustomerSummary>>('/api/v1/customers/search', {
      method: 'POST',
      body: JSON.stringify({ storeId, query: '', page: 1, pageSize: 5 }),
    }),
  })
  const cardTypes = useQuery({
    queryKey: ['member-card-types'],
    queryFn: () => apiRequest<MemberCardType[]>('/api/v1/customers/membership/card-types'),
  })
  const cardLegend = (cardTypes.data ?? []).filter((item) => item.status === 'Active').slice(0, 14)
  const chartData = cardLegend.length ? cardLegend.map((item, index) => ({ name: item.name, value: index === 0 ? 1 : 0 })) : [{ name: '暂无卡类', value: 1 }]
  const trend = Array.from({ length: 31 }, (_, index) => ({ day: String(index + 1).padStart(2, '0'), amount: 0 }))
  return <div className="classic-module-dashboard classic-customer-dashboard">
    <div className="classic-dashboard-left">
      <section className="classic-module-charts">
        <section className="classic-panel classic-chart-panel">
          <header><strong>顾客卡类数量占比</strong><MenuOutlined /></header>
          <div className="classic-chart-wrap classic-customer-card-chart">
            <ResponsiveContainer width="48%" height="100%"><PieChart><Pie data={chartData} dataKey="value" nameKey="name" innerRadius={48} outerRadius={78} isAnimationActive={false}>{chartData.map((item, index) => <Cell key={item.name} fill={chartColors[index % chartColors.length]} opacity={item.value ? 1 : .18} />)}</Pie><Tooltip /></PieChart></ResponsiveContainer>
            <div className="classic-customer-card-legend">{cardLegend.length ? cardLegend.map((item, index) => <span key={item.id}><i style={{ background: chartColors[index % chartColors.length] }} />{item.name}</span>) : <span>暂无卡类数据</span>}</div>
            <small className="classic-data-gap">卡类数量汇总接口待接入</small>
          </div>
        </section>
        <section className="classic-panel classic-chart-panel">
          <header><strong>本月储值金额走势</strong><MenuOutlined /></header>
          <div className="classic-chart-wrap classic-trend-gap"><ResponsiveContainer width="100%" height="100%"><LineChart data={trend}><XAxis dataKey="day" tick={{ fontSize: 10 }} interval={2} /><YAxis tick={{ fontSize: 10 }} width={42} /><Tooltip formatter={(value) => `¥${Number(value).toFixed(2)}`} /><Line dataKey="amount" stroke="#75a9df" strokeWidth={2} dot={false} isAnimationActive={false} /></LineChart></ResponsiveContainer><span>储值日趋势汇总接口待接入</span></div>
        </section>
      </section>
      <section className="classic-panel classic-latest classic-customer-latest">
        <header><strong>最新登记顾客列表</strong><button type="button" onClick={() => navigate('/ui/new/customer/list')}><SearchOutlined /> 查询</button></header>
        <div className="classic-table-scroll"><table><thead><tr>{['会员卡号', '会员姓名', '性别', '手机号码', '办卡门店', '会员卡类', '来店渠道', '登记时间'].map((label) => <th key={label}>{label}</th>)}</tr></thead><tbody>
          {customers.isLoading && <tr><td colSpan={8}><Spin size="small" /></td></tr>}
          {!customers.isLoading && !customers.data?.items.length && <tr><td colSpan={8}>暂无顾客数据</td></tr>}
          {customers.data?.items.map((item) => <tr key={item.id} onDoubleClick={() => navigate('/ui/new/customer/list')}><td>—</td><td>{item.displayName}</td><td>—</td><td>{item.maskedMobile}</td><td>{item.homeStoreName}</td><td>{item.activeCardCount ? `${item.activeCardCount} 张有效卡` : '普通顾客'}</td><td>—</td><td>{formatTime(item.createdAtUtc)}</td></tr>)}
        </tbody></table></div>
      </section>
    </div>
    <aside className="classic-quick-column">
      <ClassicCustomerQuickGroup title="顾客管理" items={customerManagement} />
      <ClassicCustomerQuickGroup title="报表查询" items={customerReports} />
    </aside>
  </div>
}

interface CustomerFormValues {
  name: string
  mobile: string
  gender: string
  birthDate?: string
  sourceCode?: string
  serviceNotificationConsent: boolean
  marketingConsent: boolean
}

export function ClassicCustomerListPage() {
  const auth = useAuth()
  const { can } = useAuthorization()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const storeId = auth.store?.id
  const [query, setQuery] = useState('')
  const debouncedQuery = useDebouncedValue(query.trim())
  const [page, setPage] = useState(1)
  const pageSize = 50
  const [selectedId, setSelectedId] = useState<string>()
  const [queryOpen, setQueryOpen] = useState(false)
  const [compact, setCompact] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [editOpen, setEditOpen] = useState(false)
  const [revealOpen, setRevealOpen] = useState(false)
  const [statusOpen, setStatusOpen] = useState(false)
  const [selectedCardType, setSelectedCardType] = useState('全部卡类')
  const [createForm] = Form.useForm<CustomerFormValues>()
  const [editForm] = Form.useForm<CustomerFormValues>()
  const [revealForm] = Form.useForm<{ purpose: string }>()
  const [statusForm] = Form.useForm<{ reason: string }>()
  const canWrite = can(Permission.CustomerWrite)
  const canManage = can(Permission.CustomerManage)
  const canExport = can(Permission.CustomerExport)

  useEffect(() => setPage(1), [storeId, debouncedQuery])
  const customers = useQuery({
    queryKey: ['classic-customers', storeId, debouncedQuery, page],
    enabled: Boolean(storeId),
    queryFn: ({ signal }) => apiRequest<PageResult<CustomerSummary>>('/api/v1/customers/search', {
      method: 'POST',
      body: JSON.stringify({ storeId, query: debouncedQuery, page, pageSize }),
      signal,
    }),
  })
  const detail = useQuery({
    queryKey: ['classic-customer-detail', storeId, selectedId],
    enabled: Boolean(storeId && selectedId),
    queryFn: () => apiRequest<CustomerDetail>(`/api/v1/customers/${selectedId}?storeId=${storeId}`),
  })
  const cardTypes = useQuery({
    queryKey: ['member-card-types'],
    queryFn: () => apiRequest<MemberCardType[]>('/api/v1/customers/membership/card-types'),
  })

  const createCustomer = useMutation({
    mutationFn: (values: CustomerFormValues) => apiRequest<CustomerDetail>('/api/v1/customers', { method: 'POST', body: JSON.stringify({ ...values, storeId, commandId: commandId() }) }),
    onSuccess: async (result) => { message.success('顾客档案已创建'); setCreateOpen(false); createForm.resetFields(); setSelectedId(result.id); await queryClient.invalidateQueries({ queryKey: ['classic-customers', storeId] }) },
    onError: onRequestError,
  })
  const revealMobile = useMutation({
    mutationFn: ({ purpose }: { purpose: string }) => apiRequest<{ mobile: string }>(`/api/v1/customers/${selectedId}/mobile/reveal`, { method: 'POST', body: JSON.stringify({ storeId, purpose, commandId: commandId() }) }),
    onSuccess: (result) => {
      if (!detail.data) return
      editForm.setFieldsValue({ name: detail.data.displayName, mobile: result.mobile, gender: detail.data.gender, birthDate: detail.data.birthDate, sourceCode: detail.data.sourceCode, serviceNotificationConsent: detail.data.serviceNotificationConsent, marketingConsent: detail.data.marketingConsent })
      setRevealOpen(false); setEditOpen(true); revealForm.resetFields()
    },
    onError: onRequestError,
  })
  const updateCustomer = useMutation({
    mutationFn: (values: CustomerFormValues) => apiRequest<CustomerDetail>(`/api/v1/customers/${selectedId}`, { method: 'PUT', body: JSON.stringify({ ...values, storeId, expectedVersion: detail.data?.version, commandId: commandId() }) }),
    onSuccess: async () => { message.success('顾客资料已修改'); setEditOpen(false); await Promise.all([queryClient.invalidateQueries({ queryKey: ['classic-customers', storeId] }), queryClient.invalidateQueries({ queryKey: ['classic-customer-detail', storeId, selectedId] })]) },
    onError: onRequestError,
  })
  const disableCustomer = useMutation({
    mutationFn: ({ reason }: { reason: string }) => apiRequest<CustomerDetail>(`/api/v1/customers/${selectedId}/status`, { method: 'POST', body: JSON.stringify({ storeId, restore: detail.data?.status !== 'Active', reason, expectedVersion: detail.data?.version, commandId: commandId() }) }),
    onSuccess: async (result) => { message.success(result.status === 'Active' ? '顾客档案已恢复' : '顾客档案已停用，历史资料仍保留'); setStatusOpen(false); statusForm.resetFields(); await Promise.all([queryClient.invalidateQueries({ queryKey: ['classic-customers', storeId] }), queryClient.invalidateQueries({ queryKey: ['classic-customer-detail', storeId, selectedId] })]) },
    onError: onRequestError,
  })
  const exportCustomers = useMutation({
    mutationFn: () => apiDownload('/api/v1/customers/export', { method: 'POST', body: JSON.stringify({ storeId, query: debouncedQuery, includeFullMobile: false, purpose: '经典版顾客名单导出', commandId: commandId() }) }),
    onSuccess: ({ blob, filename }) => { const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = filename; link.click(); window.setTimeout(() => URL.revokeObjectURL(url), 1000); message.success('已导出脱敏顾客名单') },
    onError: onRequestError,
  })

  const selected = useMemo(() => customers.data?.items.find((item) => item.id === selectedId), [customers.data?.items, selectedId])
  const requireSelection = (action: () => void) => selectedId ? action() : message.info('请先单击选择一位顾客')
  const openEdit = () => requireSelection(() => { if (!detail.data) return message.info('顾客资料正在加载'); setRevealOpen(true) })
  const visibleRows = customers.data?.items ?? []
  const toolbar = [
    ['新增', <PlusOutlined />, () => setCreateOpen(true), !canWrite],
    ['修改', <EditOutlined />, openEdit, !canManage],
    ['批量修改', <SettingOutlined />, () => message.info('批量修改后端尚未接入，已登记在缺口文档'), !canManage],
    ['删除', <DeleteOutlined />, () => requireSelection(() => setStatusOpen(true)), !canManage],
    ['查询', <SearchOutlined />, () => setQueryOpen((value) => !value), false],
    ['导入', <ImportOutlined />, () => message.info('顾客导入后端尚未接入，已登记在缺口文档'), !canWrite],
    ['刷新', <ReloadOutlined />, () => void customers.refetch(), false],
    ['表格', <FileExcelOutlined />, () => setCompact((value) => !value), false],
    ['打印', <PrinterOutlined />, () => window.print(), false],
    ['导出', <DownloadOutlined />, () => exportCustomers.mutate(), !canExport],
    ['退出', <TeamOutlined />, () => navigate('/ui/new/customer'), false],
  ] as const

  return <div className="classic-customer-list-page">
    <div className="classic-customer-toolbar">{toolbar.map(([label, icon, action, disabled]) => <button key={label} type="button" onClick={action} disabled={disabled}>{icon}<span>{label}</span></button>)}</div>
    {queryOpen && <section className="classic-customer-query"><label>顾客查询<Input value={query} allowClear placeholder="输入姓名、完整手机号或卡号自动检索" prefix={<SearchOutlined />} onChange={(event) => setQuery(event.target.value)} /></label><label>会员状态<Select value="全部" options={['全部', '正常', '停用', '挂失'].map((value) => ({ value, label: value }))} /></label><label>生日月份<Select value="全部" options={['全部', ...Array.from({ length: 12 }, (_, index) => `${String(index + 1).padStart(2, '0')}月`)].map((value) => ({ value, label: value }))} /></label><Button onClick={() => { setQuery(''); setQueryOpen(false) }}>取消</Button></section>}
    <div className="classic-customer-workspace">
      <aside className="classic-customer-card-tree"><h3>顾客卡类</h3><button type="button" className={selectedCardType === '全部卡类' ? 'active' : ''} onClick={() => setSelectedCardType('全部卡类')}>全部卡类</button>{(cardTypes.data ?? []).filter((item) => item.status === 'Active').map((item) => <button key={item.id} type="button" className={selectedCardType === item.name ? 'active' : ''} onClick={() => { setSelectedCardType(item.name); message.info('已保留旧版卡类入口；按卡类服务端筛选接口待接入') }}>{item.name}</button>)}</aside>
      <section className={`classic-customer-grid ${compact ? 'is-compact' : ''}`}>
        <div className="classic-customer-table-scroll"><table><thead><tr>{['会员卡号', '会员姓名', '性别', '手机号码', '办卡分店', '会员卡类', '来店渠道', '储值余额', '储值奖励', '消费总额', '欠款金额', '签单额度', '最后来店时间', '登记时间', '更新时间', '会员状态', '备注'].map((label) => <th key={label}>{label}</th>)}</tr></thead><tbody>
          {customers.isLoading && <tr><td colSpan={17}><Spin size="small" /></td></tr>}
          {!customers.isLoading && !visibleRows.length && <tr><td colSpan={17}>没有符合条件的顾客</td></tr>}
          {visibleRows.map((item) => <tr key={item.id} className={selectedId === item.id ? 'selected' : ''} onClick={() => setSelectedId(item.id)} onDoubleClick={openEdit}><td>—</td><td>{item.displayName}</td><td>{selectedId === item.id ? genderLabels[detail.data?.gender ?? 'Unknown'] : '—'}</td><td>{item.maskedMobile}</td><td>{item.homeStoreName}</td><td>{item.activeCardCount ? `${item.activeCardCount} 张有效卡` : '普通顾客'}</td><td>{selectedId === item.id ? detail.data?.sourceCode ?? '—' : '—'}</td><td>—</td><td>—</td><td>—</td><td>—</td><td>—</td><td>—</td><td>{formatTime(item.createdAtUtc)}</td><td>—</td><td>{statusLabels[item.status] ?? item.status}</td><td>—</td></tr>)}
        </tbody></table></div>
        <footer><span>{selected ? `已选择：${selected.displayName}　${selected.maskedMobile}` : `卡类：${selectedCardType}`}</span><Pagination size="small" current={page} pageSize={pageSize} total={customers.data?.total ?? 0} showSizeChanger={false} showTotal={(total) => `共 ${total} 条`} onChange={setPage} /></footer>
      </section>
    </div>

    <Modal title="新增顾客档案" open={createOpen} okText="确定" cancelText="取消" confirmLoading={createCustomer.isPending} onCancel={() => setCreateOpen(false)} onOk={() => createForm.submit()}><CustomerForm form={createForm} onFinish={(values) => createCustomer.mutate(values)} /></Modal>
    <Modal title="修改顾客档案" open={editOpen} okText="确定" cancelText="取消" confirmLoading={updateCustomer.isPending} onCancel={() => setEditOpen(false)} onOk={() => editForm.submit()}><CustomerForm form={editForm} onFinish={(values) => updateCustomer.mutate(values)} /></Modal>
    <Modal title="查看完整手机号以修改资料" open={revealOpen} okText="继续修改" cancelText="取消" confirmLoading={revealMobile.isPending} onCancel={() => setRevealOpen(false)} onOk={() => revealForm.submit()}><p>完整手机号只在本次修改中显示，本次查看会记录审计。</p><Form form={revealForm} layout="vertical" onFinish={(values) => revealMobile.mutate(values)}><Form.Item name="purpose" label="查看用途" rules={[{ required: true, message: '请填写查看用途' }, { min: 4, message: '至少填写4个字' }]}><Input placeholder="例如：核对并修改顾客资料" /></Form.Item></Form></Modal>
    <Modal title={detail.data?.status === 'Active' ? '停用顾客档案' : '恢复顾客档案'} open={statusOpen} okText="确定" cancelText="取消" okButtonProps={{ danger: detail.data?.status === 'Active' }} confirmLoading={disableCustomer.isPending} onCancel={() => setStatusOpen(false)} onOk={() => statusForm.submit()}><p>经典版“删除”按安全规则映射为停用：历史订单、余额和服务记录不会被物理删除。</p><Form form={statusForm} layout="vertical" onFinish={(values) => disableCustomer.mutate(values)}><Form.Item name="reason" label="原因" rules={[{ required: true, message: '请输入原因' }, { min: 4, message: '至少填写4个字' }]}><Input.TextArea rows={3} /></Form.Item></Form></Modal>
  </div>
}

function CustomerForm({ form, onFinish }: { form: ReturnType<typeof Form.useForm<CustomerFormValues>>[0]; onFinish: (values: CustomerFormValues) => void }) {
  return <Form form={form} layout="vertical" initialValues={{ gender: 'Unknown', serviceNotificationConsent: true, marketingConsent: false }} onFinish={onFinish} className="classic-customer-form">
    <Form.Item name="name" label="会员姓名" rules={[{ required: true, message: '请输入会员姓名' }, { max: 80 }]}><Input /></Form.Item>
    <Form.Item name="mobile" label="手机号码" rules={[{ required: true, message: '请输入手机号码' }, { pattern: /^1\d{10}$/, message: '请输入11位手机号' }]}><Input maxLength={11} /></Form.Item>
    <Form.Item name="gender" label="性别"><Select options={[{ value: 'Unknown', label: '未填写' }, { value: 'Male', label: '男' }, { value: 'Female', label: '女' }, { value: 'Other', label: '其他' }]} /></Form.Item>
    <Form.Item name="birthDate" label="生日（可选）"><Input type="date" /></Form.Item>
    <Form.Item name="sourceCode" label="来店渠道（可选）"><Select allowClear options={['陌生来店', '朋友介绍', '媒体杂志', '网络推广', '楼宇广告', '美团来店', '抖音来店', '微信平台'].map((value) => ({ value, label: value }))} /></Form.Item>
    <Form.Item name="serviceNotificationConsent" label="消费/服务通知"><Select options={[{ value: true, label: '正常' }, { value: false, label: '停用' }]} /></Form.Item>
    <Form.Item name="marketingConsent" label="营销通知"><Select options={[{ value: true, label: '正常' }, { value: false, label: '停用' }]} /></Form.Item>
  </Form>
}
