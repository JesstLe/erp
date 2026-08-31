import {
  AppstoreOutlined,
  ClockCircleOutlined,
  DeleteOutlined,
  FileTextOutlined,
  PauseCircleOutlined,
  PlayCircleOutlined,
  PrinterOutlined,
  ReloadOutlined,
  SearchOutlined,
  ShoppingOutlined,
  SwapOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Empty, Form, Input, InputNumber, Modal, Select, Spin, Tag, message } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiRequest, ApiError } from '../api/client'
import type {
  CustomerDetail,
  CustomerSummary,
  FacilityBoard,
  FacilityBoardItem,
  InventoryBalance,
  PageResult,
  Payment,
  PaymentMethod,
  PriceBook,
  ProductItem,
  ServiceEmployee,
  ServiceItem,
  ServiceOrder,
} from '../api/types'
import { useAuth } from '../auth/useAuth'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import { buildCashierProductCatalog, buildCashierServiceCatalog } from '../pages/cashierCatalog'
import {
  applyClassicOrderDiscount,
  classicCashierLineAmount,
  classicCashierTotal,
  matchesClassicCatalogSearch,
  type ClassicCashierDraftLine,
} from './classicCashierRules'

type WorkbenchTab = 'main' | 'member' | 'service' | 'product'

interface DiscountValues { percent: number; reason: string }
interface SwitchValues { facilityId: string; reason?: string }
interface SettleValues { methodId: string; amountYuan: number; memberAccountId?: string; verifiedMobile?: string; cashTenderedYuan?: number }

const statusMeta: Record<string, { label: string; short: string; className: string }> = {
  AVAILABLE: { label: '空闲', short: 'A', className: 'is-available' },
  IN_USE: { label: '占用', short: 'B', className: 'is-in-use' },
  PAUSED: { label: '暂停', short: 'C', className: 'is-paused' },
  CLEANING_REQUIRED: { label: '待清洁', short: 'D', className: 'is-cleaning' },
  MAINTENANCE: { label: '维护', short: 'M', className: 'is-disabled' },
  DISABLED: { label: '停用', short: 'X', className: 'is-disabled' },
}

function commandId() { return crypto.randomUUID() }
function money(minor: number) { return `¥${(minor / 100).toFixed(2)}` }
function duration(seconds: number) {
  const whole = Math.max(0, Math.floor(seconds)); const hours = Math.floor(whole / 3600); const minutes = Math.floor((whole % 3600) / 60); const rest = whole % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
}
function requestError(error: unknown) { return error instanceof ApiError ? error.message : '操作失败，请稍后重试' }

export function ClassicCashierFacilitiesPage() {
  const auth = useAuth(); const storeId = auth.store?.id; const navigate = useNavigate(); const queryClient = useQueryClient()
  const [tick, setTick] = useState(Date.now()); const [statusFilter, setStatusFilter] = useState<string>(''); const [compact, setCompact] = useState(false)
  const [selected, setSelected] = useState<FacilityBoardItem>(); const [tab, setTab] = useState<WorkbenchTab>('service'); const [catalogSearch, setCatalogSearch] = useState('')
  const [lines, setLines] = useState<ClassicCashierDraftLine[]>([]); const [customerId, setCustomerId] = useState<string>(); const [memberSearch, setMemberSearch] = useState(''); const [note, setNote] = useState('')
  const [discountOpen, setDiscountOpen] = useState(false); const [employeeOpen, setEmployeeOpen] = useState(false); const [switchOpen, setSwitchOpen] = useState(false); const [settleOpen, setSettleOpen] = useState(false); const [previewOpen, setPreviewOpen] = useState(false)
  const [discountForm] = Form.useForm<DiscountValues>(); const [switchForm] = Form.useForm<SwitchValues>(); const [settleForm] = Form.useForm<SettleValues>()
  const chosenMethodId = Form.useWatch('methodId', settleForm)
  const debouncedMemberSearch = useDebouncedValue(memberSearch.trim())

  useEffect(() => { const timer = window.setInterval(() => setTick(Date.now()), 1000); return () => window.clearInterval(timer) }, [])
  const board = useQuery({ queryKey: ['facility-board', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<FacilityBoard>(`/api/v1/facilities/board?storeId=${storeId}`), refetchInterval: 30_000 })
  const priceBooks = useQuery({ queryKey: ['price-books'], queryFn: () => apiRequest<PriceBook[]>('/api/v1/catalog/price-books') })
  const serviceItems = useQuery({ queryKey: ['service-items'], queryFn: () => apiRequest<ServiceItem[]>('/api/v1/catalog/service-items') })
  const products = useQuery({ queryKey: ['product-items'], queryFn: () => apiRequest<ProductItem[]>('/api/v1/catalog/products') })
  const inventory = useQuery({ queryKey: ['inventory-balances', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<InventoryBalance[]>(`/api/v1/inventory/balances?storeId=${storeId}`) })
  const employees = useQuery({ queryKey: ['service-employees', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<ServiceEmployee[]>(`/api/v1/cashier/service-employees?storeId=${storeId}`) })
  const paymentMethods = useQuery({ queryKey: ['payment-methods', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<PaymentMethod[]>(`/api/v1/payments/methods?storeId=${storeId}`) })
  const customers = useQuery({ queryKey: ['classic-cashier-customers', storeId, debouncedMemberSearch], enabled: Boolean(storeId), queryFn: () => apiRequest<PageResult<CustomerSummary>>('/api/v1/customers/search', { method: 'POST', body: JSON.stringify({ storeId, query: debouncedMemberSearch, page: 1, pageSize: 30 }) }), select: (result) => result.items })
  const customerDetail = useQuery({ queryKey: ['customer-detail', storeId, customerId], enabled: Boolean(storeId && customerId), queryFn: () => apiRequest<CustomerDetail>(`/api/v1/customers/${customerId}?storeId=${storeId}`) })

  const currentPriceBook = useMemo(() => priceBooks.data?.filter((book) => book.status.toUpperCase() === 'PUBLISHED').sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom) || (b.publishedAtUtc ?? '').localeCompare(a.publishedAtUtc ?? ''))[0], [priceBooks.data])
  const facilities = board.data?.groups.flatMap((group) => group.facilities) ?? []
  const availableFacilities = facilities.filter((item) => item.status === 'AVAILABLE')
  const filteredFacilities = statusFilter ? facilities.filter((item) => item.status === statusFilter) : facilities
  const serviceCatalog = useMemo(() => buildCashierServiceCatalog(serviceItems.data, currentPriceBook).filter((item) => matchesClassicCatalogSearch(item.code, item.name, catalogSearch)), [catalogSearch, currentPriceBook, serviceItems.data])
  const productCatalog = useMemo(() => buildCashierProductCatalog(products.data, inventory.data, currentPriceBook).filter((item) => matchesClassicCatalogSearch(item.code, item.name, catalogSearch)), [catalogSearch, currentPriceBook, inventory.data, products.data])
  const totalMinor = classicCashierTotal(lines)
  const selectedCustomer = customers.data?.find((item) => item.id === customerId)
  const memberAccounts = customerDetail.data?.cards.flatMap((card) => card.accounts.filter((account) => account.status === 'Active' || account.status === 'ACTIVE').map((account) => ({ ...account, label: `${card.cardTypeName} · ${account.accountType} · ${money(account.balanceUnits)}` }))) ?? []

  const refreshBoard = () => queryClient.invalidateQueries({ queryKey: ['facility-board', storeId] })
  const facilityMutation = useMutation({ mutationFn: ({ path, body }: { path: string; body: object }) => apiRequest<FacilityBoardItem>(path, { method: 'POST', body: JSON.stringify(body) }), onError: (error) => message.error(requestError(error)) })

  const enterFacility = async (item: FacilityBoardItem) => {
    if (!storeId || !['AVAILABLE', 'IN_USE', 'PAUSED'].includes(item.status)) return
    if (item.status === 'AVAILABLE') {
      const started = await facilityMutation.mutateAsync({ path: '/api/v1/facilities/sessions/start', body: { storeId, facilityId: item.id, commandId: commandId() } })
      setSelected(started); await refreshBoard()
    } else setSelected(item)
    setCustomerId(item.customerId ?? undefined); setLines([]); setNote(item.note ?? ''); setCatalogSearch(''); setTab('service')
  }
  const facilityOperation = async (action: 'pause' | 'resume' | 'end') => {
    if (!storeId || !selected?.sessionId) return
    const result = await facilityMutation.mutateAsync({ path: `/api/v1/facilities/sessions/${selected.sessionId}/${action}`, body: { storeId, commandId: commandId() } })
    setSelected(action === 'end' ? undefined : result); await refreshBoard(); message.success(action === 'pause' ? '计时已暂停' : action === 'resume' ? '计时已继续' : '设施计时已结束，未产生收费')
  }
  const switchFacility = async (values: SwitchValues) => {
    if (!storeId || !selected?.sessionId) return
    const result = await facilityMutation.mutateAsync({ path: `/api/v1/facilities/sessions/${selected.sessionId}/switch`, body: { storeId, targetFacilityId: values.facilityId, reason: values.reason, commandId: commandId() } })
    setSelected(result); setSwitchOpen(false); switchForm.resetFields(); await refreshBoard(); message.success('已更换服务位，原计时段已保留')
  }
  const completeCleaning = async (item: FacilityBoardItem) => {
    if (!storeId) return
    await facilityMutation.mutateAsync({ path: `/api/v1/facilities/${item.id}/cleaning/complete`, body: { storeId, commandId: commandId() } }); await refreshBoard(); message.success('服务位已恢复空闲')
  }

  const addService = (item: typeof serviceCatalog[number]) => {
    setLines((current) => [...current, { key: commandId(), lineType: 'Service', itemId: item.id, code: item.code, name: item.name, quantity: 1, actualMinutes: item.duration, referencePriceMinor: item.priceMinor, referencePriceDefined: item.hasPublishedPrice, enteredPriceMinor: item.priceMinor }])
  }
  const addProduct = (item: typeof productCatalog[number]) => {
    setLines((current) => [...current, { key: commandId(), lineType: 'Product', itemId: item.id, code: item.code, name: item.name, unitName: item.unitName, quantity: 1, referencePriceMinor: item.priceMinor, referencePriceDefined: item.hasPublishedPrice, enteredPriceMinor: item.priceMinor }])
  }
  const updateLine = (key: string, patch: Partial<ClassicCashierDraftLine>) => setLines((current) => current.map((line) => line.key === key ? { ...line, ...patch } : line))
  const updateLinePrice = (line: ClassicCashierDraftLine, yuan: number | null) => {
    const enteredPriceMinor = Math.round(Number(yuan ?? 0) * 100)
    updateLine(line.key, { enteredPriceMinor, priceOverrideReason: enteredPriceMinor === line.referencePriceMinor ? undefined : line.priceOverrideReason?.trim() || '现场调整成交价' })
  }
  const assignEmployee = (employeeId: string) => {
    const employee = employees.data?.find((item) => item.id === employeeId)
    setLines((current) => current.map((line) => line.lineType === 'Service' ? { ...line, employeeId, employeeName: employee?.displayName } : line)); setEmployeeOpen(false)
  }
  const applyDiscount = (values: DiscountValues) => { setLines((current) => applyClassicOrderDiscount(current, values.percent, values.reason)); setDiscountOpen(false); discountForm.resetFields(); message.success('整单折扣已应用，提交时仍按权限策略校验') }
  const clearDraft = () => Modal.confirm({ title: '清空当前账单？', content: '只清空尚未提交的新系统草稿，不删除设施计时或历史账单。', okText: '确认清空', okButtonProps: { danger: true }, onOk: () => setLines([]) })

  const settleMutation = useMutation({
    mutationFn: async (values: SettleValues) => {
      if (!storeId || !selected) throw new Error('当前没有服务位')
      if (!lines.length) throw new Error('请至少选择一个项目或产品')
      if (lines.some((line) => line.referencePriceDefined === false && line.enteredPriceMinor <= 0)) throw new Error('请先为未设置目录价的项目或产品输入本次成交价')
      if (lines.some((line) => line.lineType === 'Service' && !line.employeeId)) throw new Error('请为每个服务项目选择实际服务员工')
      let order = await apiRequest<ServiceOrder>('/api/v1/cashier/orders', { method: 'POST', body: JSON.stringify({
        storeId, visitId: selected.visitId ?? null, customerId: customerId ?? null, note, commandId: commandId(),
        lines: lines.map((line) => ({ lineType: line.lineType, serviceItemId: line.lineType === 'Service' ? line.itemId : null, productItemId: line.lineType === 'Product' ? line.itemId : null, serviceEmployeeId: line.lineType === 'Service' ? line.employeeId : null, quantity: line.quantity, actualSeconds: line.lineType === 'Service' && line.actualMinutes !== undefined ? Math.round(line.actualMinutes * 60) : null, enteredPriceMinor: line.enteredPriceMinor, priceOverrideReason: line.priceOverrideReason })),
      }) })
      if (order.priceAuthorizationStatus === 'PendingApproval') return { order, payment: undefined as Payment | undefined, pendingApproval: true }
      order = await apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${order.id}/confirm`, { method: 'POST', body: JSON.stringify({ storeId, expectedVersion: order.version, commandId: commandId() }) })
      const method = paymentMethods.data?.find((item) => item.id === values.methodId)
      if (!method) throw new Error('请选择支付方式')
      if (method.channelProvider) return { order, payment: undefined as Payment | undefined, channel: true }
      const amountMinor = Math.round(values.amountYuan * 100)
      const payment = await apiRequest<Payment>(`/api/v1/payments/orders/${order.id}/settle`, { method: 'POST', body: JSON.stringify({
        storeId, expectedVersion: order.version, commandId: commandId(), cashTenderedMinor: method.category === 'Cash' ? Math.round((values.cashTenderedYuan ?? values.amountYuan) * 100) : null, verifiedMobile: values.verifiedMobile,
        allocations: [{ methodId: method.id, amountMinor, memberAccountId: method.category === 'InternalAccount' ? values.memberAccountId : null }],
      }) })
      return { order, payment, pendingApproval: false }
    },
    onSuccess: async (result) => {
      if (result.pendingApproval) { message.warning('消费单已创建并进入改价审批，审批后才能收款'); navigate('/ui/new/cashier/checkout'); return }
      if (result.channel) { message.info('官方渠道需要生成付款码并查单，已把该单送到完整收款台'); navigate('/ui/new/cashier/checkout'); return }
      message.success('收款完成，消费单已结算')
      if (selected?.sessionId && storeId) {
        try { await apiRequest(`/api/v1/facilities/sessions/${selected.sessionId}/end`, { method: 'POST', body: JSON.stringify({ storeId, commandId: commandId() }) }) } catch { message.warning('收款已完成，但设施结束失败，请在房台总览手动结束') }
      }
      setSettleOpen(false); settleForm.resetFields(); setLines([]); setSelected(undefined)
      await Promise.all([refreshBoard(), queryClient.invalidateQueries({ queryKey: ['cashier-orders', storeId] }), queryClient.invalidateQueries({ queryKey: ['payments', storeId] })])
    },
    onError: (error) => message.error(requestError(error)),
  })

  const openSettlement = () => {
    if (!lines.length) { message.warning('请先选择项目或产品'); return }
    if (lines.some((line) => line.referencePriceDefined === false && line.enteredPriceMinor <= 0)) { message.warning('请先为未设置目录价的项目或产品输入本次成交价'); return }
    if (lines.some((line) => line.lineType === 'Service' && !line.employeeId)) { message.warning('请先通过“整单员工”或明细选择服务员工'); return }
    const method = paymentMethods.data?.find((item) => !item.channelProvider) ?? paymentMethods.data?.[0]
    settleForm.setFieldsValue({ methodId: method?.id, amountYuan: totalMinor / 100, cashTenderedYuan: totalMinor / 100 }); setSettleOpen(true)
  }

  if (board.isLoading) return <div className="classic-cashier-loading"><Spin /></div>
  if (board.error) return <Alert type="error" showIcon title={requestError(board.error)} />

  if (!selected) return <div className="classic-room-page">
    <header className="classic-room-toolbar">
      <strong>前台收银</strong><span>当前门店：{auth.store?.name}</span>
      <div className="classic-room-statuses">
        {[{ status: '', label: '全部' }, { status: 'AVAILABLE', label: '空闲' }, { status: 'PAUSED', label: '暂停' }, { status: 'IN_USE', label: '占用' }].map((item) => <button type="button" key={item.status || 'all'} className={statusFilter === item.status ? 'active' : ''} onClick={() => setStatusFilter(item.status)}><b>{item.status ? facilities.filter((facility) => facility.status === item.status).length : facilities.length}</b><small>{item.label}</small></button>)}
      </div>
      <div className="classic-room-tools"><button type="button" className={!compact ? 'active' : ''} onClick={() => setCompact(false)} title="大图标"><AppstoreOutlined /></button><button type="button" className={compact ? 'active' : ''} onClick={() => setCompact(true)} title="小图标"><ShoppingOutlined /></button><button type="button" onClick={() => refreshBoard()} title="刷新"><ReloadOutlined /></button></div>
    </header>
    {!filteredFacilities.length ? <Empty description="当前筛选下没有服务位" /> : <div className={`classic-room-grid ${compact ? 'is-compact' : ''}`}>{filteredFacilities.map((item) => {
      const meta = statusMeta[item.status] ?? { label: item.status, short: '?', className: 'is-disabled' }
      const liveSeconds = item.startedAtUtc && item.status === 'IN_USE' ? item.activeSeconds + Math.max(0, Math.floor((tick - new Date(board.data?.serverNowUtc ?? Date.now()).getTime()) / 1000)) : item.activeSeconds
      return <button type="button" key={item.id} className={`classic-room-card ${meta.className}`} disabled={!['AVAILABLE', 'IN_USE', 'PAUSED', 'CLEANING_REQUIRED'].includes(item.status)} onClick={() => item.status === 'CLEANING_REQUIRED' ? completeCleaning(item) : enterFacility(item)}>
        <span className="classic-room-state">{meta.short}</span><strong>{item.displayName}</strong><small>{item.code} · {meta.label}</small><b>{item.sessionId ? duration(liveSeconds) : '0/1'}</b>{item.customerDisplayName && <em>{item.customerDisplayName}</em>}
      </button>
    })}</div>}
    <footer className="classic-room-shortcuts">
      {[['顾客\n开卡', '/ui/new/customer/list'], ['顾客\n储值', '/ui/new/customer/list'], ['顾客\n预约', '/ui/new/cashier/scheduling'], ['顾客\n护理', '/ui/new/legacy/customer/customer-005'], ['签单\n清账', '/ui/new/cashier/checkout'], ['消费\n退货', '/ui/new/cashier/checkout'], ['积分\n增减', '/ui/new/customer/list'], ['兑换\n礼品', '/ui/new/customer/list'], ['兑换\n储值', '/ui/new/customer/list'], ['收银\n交班', '/ui/new/finance/checkout']].map(([label, path]) => <button type="button" key={label} onClick={() => navigate(path)}>{label.split('\n').map((part) => <span key={part}>{part}</span>)}</button>)}
      <button type="button" className="is-exit" onClick={() => navigate('/ui/new/cashier')}>退出<span>前台</span></button>
    </footer>
  </div>

  const liveSeconds = selected.startedAtUtc && selected.status === 'IN_USE' ? selected.activeSeconds + Math.max(0, Math.floor((tick - new Date(board.data?.serverNowUtc ?? Date.now()).getTime()) / 1000)) : selected.activeSeconds
  const chosenMethod = paymentMethods.data?.find((item) => item.id === chosenMethodId)

  return <div className="classic-sell-page">
    <header className="classic-sell-tabs">
      <div className="classic-sell-room"><b>{selected.displayName}</b><span>{selected.code} · {duration(liveSeconds)}</span></div>
      <button type="button" className={tab === 'main' ? 'active' : ''} onClick={() => setTab('main')}><FileTextOutlined />主单<span>信息</span></button>
      <button type="button" onClick={() => navigate('/ui/new/cashier/scheduling')}><ClockCircleOutlined />顾客<span>预约</span></button>
      <button type="button" className={tab === 'member' ? 'active' : ''} onClick={() => setTab('member')}><TeamOutlined />会员<span>刷卡</span></button>
      <button type="button" className={tab === 'service' ? 'active' : ''} onClick={() => setTab('service')}><AppstoreOutlined />项目<span>列表</span></button>
      <button type="button" className={tab === 'product' ? 'active' : ''} onClick={() => setTab('product')}><ShoppingOutlined />产品<span>列表</span></button>
      <button type="button" className="is-settle" onClick={openSettlement}>结算</button>
    </header>
    <div className="classic-sell-body">
      <section className="classic-bill-pane">
        <div className="classic-bill-summary"><span>{selectedCustomer ? `会员：${selectedCustomer.displayName}` : '散客/暂未关联会员'}</span><b>合计 {money(totalMinor)}</b></div>
        {!lines.length ? <div className="classic-bill-empty">当前没有选择消费的任何项目或产品</div> : <div className="classic-bill-lines">{lines.map((line, index) => <article key={line.key}>
          <header><b>{index + 1}. {line.name}</b><Tag>{line.lineType === 'Service' ? '项目' : '产品'}</Tag><button type="button" onClick={() => setLines((current) => current.filter((item) => item.key !== line.key))}><DeleteOutlined /></button></header>
          <div><span>编号 {line.code}</span><span>{line.referencePriceDefined === false ? '未设置目录价' : `目录价 ${money(line.referencePriceMinor)}`}</span><span>小计 {money(classicCashierLineAmount(line))}</span></div>
          <div className="classic-line-edit"><label>数量<InputNumber size="small" min={1} max={999} value={line.quantity} onChange={(value) => updateLine(line.key, { quantity: Number(value ?? 1) })} /></label><label>成交价<InputNumber size="small" min={0} max={100_000_000} precision={2} prefix="¥" value={line.enteredPriceMinor / 100} onChange={(value) => updateLinePrice(line, value)} /></label>{line.lineType === 'Service' && <><label>时长<InputNumber size="small" min={0} max={1440} value={line.actualMinutes} onChange={(value) => updateLine(line.key, { actualMinutes: value === null ? undefined : Number(value) })} addonAfter="分" /></label><label>员工<Select size="small" value={line.employeeId} placeholder="请选择" options={employees.data?.map((employee) => ({ value: employee.id, label: employee.displayName }))} onChange={(value) => updateLine(line.key, { employeeId: value, employeeName: employees.data?.find((employee) => employee.id === value)?.displayName })} /></label></>} </div>
          {line.enteredPriceMinor !== line.referencePriceMinor && <label className="classic-price-override-reason">改价原因<Input size="small" value={line.priceOverrideReason ?? ''} maxLength={500} onChange={(event) => updateLine(line.key, { priceOverrideReason: event.target.value })} /></label>}
        </article>)}</div>}
      </section>
      <section className="classic-catalog-pane">
        {tab === 'main' && <div className="classic-main-order"><h2>主单信息</h2><label>关联顾客<Select allowClear showSearch optionFilterProp="label" value={customerId} placeholder="可暂不识别顾客" options={customers.data?.map((item) => ({ value: item.id, label: `${item.displayName} · ${item.maskedMobile}` }))} onChange={setCustomerId} /></label><label>接待备注<Input.TextArea rows={4} value={note} maxLength={500} onChange={(event) => setNote(event.target.value)} /></label><Alert type="info" showIcon title="老版的男/女客人数、年龄和来源渠道字段尚无新系统主单存储字段，位置已记录到后端缺口文档。" /></div>}
        {tab === 'member' && <div className="classic-member-search"><div className="classic-catalog-search"><Input prefix={<SearchOutlined />} value={memberSearch} onChange={(event) => setMemberSearch(event.target.value)} placeholder="输入姓名、完整手机号或卡号自动查询" allowClear /></div><div className="classic-member-results">{customers.isFetching && <Spin size="small" />}{customers.data?.map((customer) => <button type="button" key={customer.id} className={customer.id === customerId ? 'active' : ''} onClick={() => { setCustomerId(customer.id); setTab('service') }}><UserOutlined /><b>{customer.displayName}</b><span>{customer.maskedMobile}</span><small>{customer.homeStoreName} · {customer.activeCardCount} 张有效卡</small></button>)}</div></div>}
        {(tab === 'service' || tab === 'product') && <><div className="classic-catalog-search"><Select value="all" options={[{ value: 'all', label: '全部分类' }]} /><Input prefix={<SearchOutlined />} value={catalogSearch} onChange={(event) => setCatalogSearch(event.target.value)} placeholder="输入编号或名称自动查询" allowClear /></div><div className="classic-catalog-grid">{tab === 'service' ? serviceCatalog.map((item) => <button type="button" key={item.id} onClick={() => addService(item)}><small>No.{item.code}</small><b>{item.name}</b><span>{item.hasPublishedPrice ? money(item.priceMinor) : '未设置目录价'} / {item.duration ?? '-'} 分钟</span></button>) : productCatalog.map((item) => <button type="button" key={item.id} onClick={() => addProduct(item)}><small>No.{item.code}</small><b>{item.name}</b><span>{item.hasPublishedPrice ? money(item.priceMinor) : '未设置目录价'} / 库存 {item.stock ?? '-'} {item.unitName}</span></button>)}</div></>}
      </section>
    </div>
    <footer className="classic-sell-actions">
      <button type="button" onClick={() => setEmployeeOpen(true)}><TeamOutlined />整单<span>员工</span></button><button type="button" disabled title="缺少独立顾问归属后端字段"><UserOutlined />整单<span>顾问</span></button><button type="button" onClick={() => { discountForm.setFieldsValue({ percent: 100, reason: '' }); setDiscountOpen(true) }}>整单<span>折扣</span></button><button type="button" onClick={() => setSwitchOpen(true)}><SwapOutlined />更换<span>房台</span></button><button type="button" disabled title="后端待接入">合并<span>账单</span></button><button type="button" onClick={() => setPreviewOpen(true)}><PrinterOutlined />预结<span>小票</span></button><button type="button" onClick={clearDraft}><DeleteOutlined />删除<span>账单</span></button>
      {selected.status === 'IN_USE' && <button type="button" onClick={() => facilityOperation('pause')}><PauseCircleOutlined />暂停<span>计时</span></button>}{selected.status === 'PAUSED' && <button type="button" onClick={() => facilityOperation('resume')}><PlayCircleOutlined />继续<span>计时</span></button>}<button type="button" onClick={() => facilityOperation('end')}>结束<span>服务</span></button><button type="button" className="is-return" onClick={() => setSelected(undefined)}>返回<span>房台</span></button>
    </footer>

    <Modal title="整单员工" open={employeeOpen} onCancel={() => setEmployeeOpen(false)} footer={null}><div className="classic-employee-picker">{employees.data?.map((employee) => <button type="button" key={employee.id} onClick={() => assignEmployee(employee.id)}><b>{employee.displayName}</b><span>{employee.employeeNo} · {employee.positionName}</span></button>)}</div></Modal>
    <Modal title="整单折扣" open={discountOpen} onCancel={() => setDiscountOpen(false)} onOk={() => discountForm.submit()} okText="应用折扣"><Form form={discountForm} layout="vertical" onFinish={applyDiscount}><Form.Item name="percent" label="折后比例（%）" rules={[{ required: true }, { type: 'number', min: 0, max: 100 }]}><InputNumber min={0} max={100} precision={2} className="full-width" /></Form.Item><Form.Item name="reason" label="改价原因" rules={[{ required: true, message: '请输入改价原因' }, { max: 500 }]}><Input.TextArea rows={3} maxLength={500} /></Form.Item></Form></Modal>
    <Modal title="更换房台" open={switchOpen} onCancel={() => setSwitchOpen(false)} onOk={() => switchForm.submit()} okText="确认更换" confirmLoading={facilityMutation.isPending}><Form form={switchForm} layout="vertical" onFinish={switchFacility}><Form.Item name="facilityId" label="目标空闲服务位" rules={[{ required: true }]}><Select options={availableFacilities.map((item) => ({ value: item.id, label: `${item.displayName} · ${item.code}` }))} /></Form.Item><Form.Item name="reason" label="更换原因（可选）"><Input maxLength={500} /></Form.Item></Form></Modal>
    <Modal title="预结小票" open={previewOpen} onCancel={() => setPreviewOpen(false)} footer={<><Button onClick={() => setPreviewOpen(false)}>关闭</Button><Button type="primary" onClick={() => window.print()}>打印预览</Button></>}><div className="classic-prebill"><h2>{auth.store?.name}</h2><p>房台：{selected.displayName}　顾客：{selectedCustomer?.displayName ?? '散客'}</p>{lines.map((line) => <div key={line.key}><span>{line.name} × {line.quantity}</span><b>{money(classicCashierLineAmount(line))}</b></div>)}<footer>应收合计 <b>{money(totalMinor)}</b></footer></div></Modal>
    <Modal title="收银结算" open={settleOpen} onCancel={() => setSettleOpen(false)} onOk={() => settleForm.submit()} okText="确认收款" confirmLoading={settleMutation.isPending} width={560}><Alert type="info" showIcon title="设施计时只用于运营记录，本次应收完全来自店长选择的项目、产品和成交价。" /><Form form={settleForm} layout="vertical" onFinish={(values) => settleMutation.mutate(values)}><Form.Item name="methodId" label="支付方式" rules={[{ required: true }]}><Select options={paymentMethods.data?.map((method) => ({ value: method.id, label: `${method.name}${method.channelProvider ? '（官方渠道）' : method.category === 'ManualExternal' ? '（人工登记待核对）' : ''}` }))} onChange={() => { settleForm.setFieldValue('memberAccountId', undefined); settleForm.setFieldValue('verifiedMobile', undefined) }} /></Form.Item><Form.Item name="amountYuan" label={`收款金额（应收 ${money(totalMinor)}）`} rules={[{ required: true }, { type: 'number', min: totalMinor / 100, max: totalMinor / 100 }]}><InputNumber precision={2} className="full-width" prefix="¥" /></Form.Item>{chosenMethod?.category === 'Cash' && <Form.Item name="cashTenderedYuan" label="现金实收" rules={[{ required: true }, { type: 'number', min: totalMinor / 100 }]}><InputNumber precision={2} className="full-width" prefix="¥" /></Form.Item>}{chosenMethod?.category === 'InternalAccount' && <><Form.Item name="memberAccountId" label="会员账户" rules={[{ required: true }]}><Select loading={customerDetail.isLoading} options={memberAccounts.map((account) => ({ value: account.id, label: account.label }))} /></Form.Item><Form.Item name="verifiedMobile" label="会员完整手机号" rules={[{ required: true }, { pattern: /^1[3-9]\d{9}$/, message: '请输入有效手机号' }]}><Input maxLength={11} inputMode="numeric" /></Form.Item></>} </Form></Modal>
  </div>
}
