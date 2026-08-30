import {
  AppstoreOutlined,
  ClockCircleOutlined,
  DeleteOutlined,
  FileTextOutlined,
  MergeCellsOutlined,
  PauseCircleOutlined,
  PlayCircleOutlined,
  PrinterOutlined,
  SearchOutlined,
  ShoppingOutlined,
  SwapOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Empty, Form, Input, InputNumber, Modal, Select, Spin, Tag, message } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiRequest, ApiError } from '../api/client'
import type {
  CustomerDetail,
  CustomerSummary,
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
  ServiceOrderPrebill,
} from '../api/types'
import { useAuth } from '../auth/useAuth'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import {
  applyClassicOrderDiscount,
  classicCashierLineAmount,
  classicCashierTotal,
  matchesClassicCatalogSearch,
  type ClassicCashierDraftLine,
} from '../classic/classicCashierRules'
import { createSerialTaskQueue, retryVersionConflictOnce } from './modernFacilityCashierConcurrency'

type WorkbenchTab = 'main' | 'member' | 'service' | 'product'
interface DiscountValues { percent: number; reason: string }
interface SwitchValues { facilityId: string; reason?: string }
interface SettleValues { methodId: string; amountYuan: number; memberAccountId?: string; verifiedMobile?: string; cashTenderedYuan?: number }
interface DraftUpdate {
  lines?: ClassicCashierDraftLine[]
  customerId?: string
  consultantEmployeeId?: string
  note?: string
  sourceChannel?: string
  manualTicketNo?: string
  maleGuestCount?: number
  maleAgeBand?: string
  femaleGuestCount?: number
  femaleAgeBand?: string
}
const ageBands = ['0-5', '6-16', '17-29', '30-45', '46-60', '60以上'].map((value) => ({ value, label: value }))

interface Props {
  facility: FacilityBoardItem
  availableFacilities: FacilityBoardItem[]
  onFacilityChanged: (facility: FacilityBoardItem) => void
  onExit: () => void
  onCompleted: () => Promise<void> | void
}

function commandId() { return crypto.randomUUID() }
function money(minor: number) { return `¥${(minor / 100).toFixed(2)}` }
function duration(seconds: number) {
  const whole = Math.max(0, Math.floor(seconds)); const hours = Math.floor(whole / 3600); const minutes = Math.floor((whole % 3600) / 60); const rest = whole % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
}
function requestError(error: unknown) { return error instanceof ApiError ? error.message : error instanceof Error ? error.message : '操作失败，请稍后重试' }

function fromOrder(order: ServiceOrder): ClassicCashierDraftLine[] {
  return order.lines.map((line) => ({
    key: line.id,
    lineType: line.lineType,
    itemId: line.lineType === 'Service' ? line.serviceItemId! : line.productItemId!,
    code: line.itemCode,
    name: line.itemName,
    unitName: line.unitName,
    quantity: line.quantity,
    actualMinutes: line.actualSeconds === undefined ? undefined : Math.round(line.actualSeconds / 60),
    referencePriceMinor: line.referencePriceMinor,
    enteredPriceMinor: line.enteredPriceMinor,
    priceOverrideReason: line.priceOverrideReason,
    employeeId: line.serviceEmployeeId,
    employeeName: line.employeeName,
  }))
}

export function ModernFacilityCashierWorkbench({ facility, availableFacilities, onFacilityChanged, onExit, onCompleted }: Props) {
  const auth = useAuth(); const storeId = auth.store?.id; const navigate = useNavigate(); const queryClient = useQueryClient()
  const [modal, modalContextHolder] = Modal.useModal()
  const isBeforeStart = facility.status === 'AVAILABLE' && !facility.sessionId && !facility.visitId
  const [tick, setTick] = useState(Date.now()); const [tab, setTab] = useState<WorkbenchTab>('service'); const [catalogSearch, setCatalogSearch] = useState('')
  const [lines, setLines] = useState<ClassicCashierDraftLine[]>([]); const [customerId, setCustomerId] = useState<string>(); const [note, setNote] = useState('')
  const [sourceChannel, setSourceChannel] = useState(''); const [manualTicketNo, setManualTicketNo] = useState(''); const [maleGuestCount, setMaleGuestCount] = useState(0); const [maleAgeBand, setMaleAgeBand] = useState<string>(); const [femaleGuestCount, setFemaleGuestCount] = useState(0); const [femaleAgeBand, setFemaleAgeBand] = useState<string>()
  const [consultantEmployeeId, setConsultantEmployeeId] = useState<string>()
  const [productToAddId, setProductToAddId] = useState<string>(); const [productAddedByEmployeeId, setProductAddedByEmployeeId] = useState<string>()
  const [memberSearch, setMemberSearch] = useState(''); const [discountOpen, setDiscountOpen] = useState(false); const [employeeOpen, setEmployeeOpen] = useState(false); const [consultantOpen, setConsultantOpen] = useState(false)
  const [switchOpen, setSwitchOpen] = useState(false); const [mergeOpen, setMergeOpen] = useState(false); const [mergeOrderId, setMergeOrderId] = useState<string>(); const [settleOpen, setSettleOpen] = useState(false); const [prebill, setPrebill] = useState<ServiceOrderPrebill>()
  const [serviceEnded, setServiceEnded] = useState(!isBeforeStart && (!facility.sessionId || !['IN_USE', 'PAUSED'].includes(facility.status)))
  const [discountForm] = Form.useForm<DiscountValues>(); const [switchForm] = Form.useForm<SwitchValues>(); const [settleForm] = Form.useForm<SettleValues>()
  const chosenMethodId = Form.useWatch('methodId', settleForm); const debouncedMemberSearch = useDebouncedValue(memberSearch.trim())
  const draftCommand = useRef(commandId())
  const draftSaveQueue = useRef(createSerialTaskQueue())
  const hydratedOrderId = useRef<string | undefined>(undefined)
  const linesRef = useRef<ClassicCashierDraftLine[]>([])
  const liveAnchor = useRef({ facilityId: facility.id, status: facility.status, at: Date.now(), activeSeconds: facility.activeSeconds })

  useEffect(() => { const timer = window.setInterval(() => setTick(Date.now()), 1000); return () => window.clearInterval(timer) }, [])
  useEffect(() => { liveAnchor.current = { facilityId: facility.id, status: facility.status, at: Date.now(), activeSeconds: facility.activeSeconds } }, [facility.activeSeconds, facility.id, facility.status])
  const orderKey = ['facility-cashier-order', storeId, facility.visitId]
  const draft = useQuery({
    queryKey: orderKey,
    enabled: Boolean(storeId && facility.visitId),
    queryFn: () => apiRequest<ServiceOrder>(`/api/v1/cashier/visits/${facility.visitId}/draft`, { method: 'POST', body: JSON.stringify({ storeId, commandId: draftCommand.current }) }),
  })
  useEffect(() => {
    if (!draft.data || hydratedOrderId.current === draft.data.id) return
    hydratedOrderId.current = draft.data.id
    const hydratedLines = fromOrder(draft.data); linesRef.current = hydratedLines; setLines(hydratedLines); setCustomerId(draft.data.customerId); setNote(draft.data.note ?? '')
    setConsultantEmployeeId(draft.data.consultantEmployeeId)
    setSourceChannel(draft.data.sourceChannel ?? ''); setManualTicketNo(draft.data.manualTicketNo ?? ''); setMaleGuestCount(draft.data.maleGuestCount ?? 0); setMaleAgeBand(draft.data.maleAgeBand); setFemaleGuestCount(draft.data.femaleGuestCount ?? 0); setFemaleAgeBand(draft.data.femaleAgeBand)
  }, [draft.data])

  const priceBooks = useQuery({ queryKey: ['price-books'], queryFn: () => apiRequest<PriceBook[]>('/api/v1/catalog/price-books') })
  const serviceItems = useQuery({ queryKey: ['service-items'], queryFn: () => apiRequest<ServiceItem[]>('/api/v1/catalog/service-items') })
  const products = useQuery({ queryKey: ['product-items'], queryFn: () => apiRequest<ProductItem[]>('/api/v1/catalog/products') })
  const inventory = useQuery({ queryKey: ['inventory-balances', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<InventoryBalance[]>(`/api/v1/inventory/balances?storeId=${storeId}`) })
  const employees = useQuery({ queryKey: ['service-employees', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<ServiceEmployee[]>(`/api/v1/cashier/service-employees?storeId=${storeId}`) })
  const paymentMethods = useQuery({ queryKey: ['payment-methods', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<PaymentMethod[]>(`/api/v1/payments/methods?storeId=${storeId}`) })
  const customers = useQuery({ queryKey: ['modern-cashier-customers', storeId, debouncedMemberSearch], enabled: Boolean(storeId), queryFn: () => apiRequest<PageResult<CustomerSummary>>('/api/v1/customers/search', { method: 'POST', body: JSON.stringify({ storeId, query: debouncedMemberSearch, page: 1, pageSize: 30 }) }), select: (result) => result.items })
  const customerDetail = useQuery({ queryKey: ['customer-detail', storeId, customerId], enabled: Boolean(storeId && customerId), queryFn: () => apiRequest<CustomerDetail>(`/api/v1/customers/${customerId}?storeId=${storeId}`) })
  const mergeCandidates = useQuery({ queryKey: ['cashier-merge-candidates', storeId, draft.data?.id], enabled: Boolean(storeId && mergeOpen), queryFn: () => apiRequest<PageResult<ServiceOrder>>(`/api/v1/cashier/orders?storeId=${storeId}&status=Draft&page=1&pageSize=100`), select: (result) => result.items.filter((order) => order.id !== draft.data?.id) })

  const currentPriceBook = useMemo(() => priceBooks.data?.filter((book) => book.status.toUpperCase() === 'PUBLISHED').sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom) || (b.publishedAtUtc ?? '').localeCompare(a.publishedAtUtc ?? ''))[0], [priceBooks.data])
  const serviceCatalog = useMemo(() => (currentPriceBook?.lines ?? []).map((price) => { const item = serviceItems.data?.find((entry) => entry.id === price.serviceItemId); return { id: price.serviceItemId, code: item?.code ?? '', name: price.serviceItemName, duration: item?.standardDurationMinutes, priceMinor: price.unitPriceMinor } }).filter((item) => matchesClassicCatalogSearch(item.code, item.name, catalogSearch)), [catalogSearch, currentPriceBook, serviceItems.data])
  const productCatalog = useMemo(() => (currentPriceBook?.productLines ?? []).map((price) => { const item = products.data?.find((entry) => entry.id === price.productItemId); const stock = inventory.data?.find((entry) => entry.productItemId === price.productItemId); return { id: price.productItemId, code: item?.code ?? '', name: price.productItemName, unitName: price.unitName, priceMinor: price.unitPriceMinor, stock: stock?.availableQuantity } }).filter((item) => matchesClassicCatalogSearch(item.code, item.name, catalogSearch)), [catalogSearch, currentPriceBook, inventory.data, products.data])
  const totalMinor = classicCashierTotal(lines); const selectedCustomer = customers.data?.find((item) => item.id === customerId)
  const memberAccounts = customerDetail.data?.cards.flatMap((card) => card.accounts.filter((account) => ['Active', 'ACTIVE'].includes(account.status)).map((account) => ({ ...account, label: `${card.cardTypeName} · ${account.accountType} · ${money(account.balanceUnits)}` }))) ?? []
  const chosenMethod = paymentMethods.data?.find((item) => item.id === chosenMethodId)
  const liveSeconds = facility.startedAtUtc && facility.status === 'IN_USE' ? liveAnchor.current.activeSeconds + Math.max(0, Math.floor((tick - liveAnchor.current.at) / 1000)) : facility.activeSeconds
  const editable = isBeforeStart || draft.data?.status === 'Draft'

  const updateDraft = async (order: ServiceOrder, next: DraftUpdate) => {
    if (!storeId) throw new Error('当前门店无效')
    const nextLines = next.lines ?? fromOrder(order)
    const nextMaleGuestCount = Object.hasOwn(next, 'maleGuestCount') ? next.maleGuestCount ?? 0 : order.maleGuestCount ?? 0
    const nextFemaleGuestCount = Object.hasOwn(next, 'femaleGuestCount') ? next.femaleGuestCount ?? 0 : order.femaleGuestCount ?? 0
    return apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${order.id}/draft`, { method: 'PUT', body: JSON.stringify({
      storeId,
      customerId: Object.hasOwn(next, 'customerId') ? next.customerId || null : order.customerId ?? null,
      consultantEmployeeId: Object.hasOwn(next, 'consultantEmployeeId') ? next.consultantEmployeeId || null : order.consultantEmployeeId ?? null,
      note: Object.hasOwn(next, 'note') ? next.note : order.note ?? null,
      sourceChannel: (Object.hasOwn(next, 'sourceChannel') ? next.sourceChannel : order.sourceChannel) || null,
      manualTicketNo: (Object.hasOwn(next, 'manualTicketNo') ? next.manualTicketNo : order.manualTicketNo) || null,
      maleGuestCount: nextMaleGuestCount,
      maleAgeBand: nextMaleGuestCount ? (Object.hasOwn(next, 'maleAgeBand') ? next.maleAgeBand : order.maleAgeBand) ?? null : null,
      femaleGuestCount: nextFemaleGuestCount,
      femaleAgeBand: nextFemaleGuestCount ? (Object.hasOwn(next, 'femaleAgeBand') ? next.femaleAgeBand : order.femaleAgeBand) ?? null : null,
      lines: nextLines.map((line) => ({ lineType: line.lineType, serviceItemId: line.lineType === 'Service' ? line.itemId : null, productItemId: line.lineType === 'Product' ? line.itemId : null, serviceEmployeeId: line.employeeId ?? null, quantity: line.quantity, actualSeconds: line.lineType === 'Service' && line.actualMinutes !== undefined ? Math.round(line.actualMinutes * 60) : null, enteredPriceMinor: line.enteredPriceMinor, priceOverrideReason: line.priceOverrideReason ?? null })),
      expectedVersion: order.version,
      commandId: commandId(),
    }) })
  }

  const saveDraft = useMutation({
    mutationFn: (next: DraftUpdate) => draftSaveQueue.current.run(async () => {
      const order = queryClient.getQueryData<ServiceOrder>(orderKey) ?? draft.data
      if (!storeId || !order) throw new Error('消费单草稿尚未加载')
      let saved: ServiceOrder
      try {
        saved = await updateDraft(order, next)
      } catch (error) {
        if (!(error instanceof ApiError) || error.code !== 'VERSION_CONFLICT') throw error
        const latest = await apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${order.id}?storeId=${storeId}`)
        queryClient.setQueryData(orderKey, latest)
        if (latest.status !== 'Draft') throw error
        saved = await updateDraft(latest, next)
      }
      queryClient.setQueryData(orderKey, saved)
      return saved
    }),
    onSuccess: (order) => queryClient.setQueryData(orderKey, order),
    onError: (error) => message.error(requestError(error)),
  })
  const setLocalLines = (nextLines: ClassicCashierDraftLine[]) => { linesRef.current = nextLines; setLines(nextLines) }
  const persistLines = async (nextLines: ClassicCashierDraftLine[]) => {
    setLocalLines(nextLines)
    if (!isBeforeStart) await saveDraft.mutateAsync({ lines: nextLines })
  }
  const addService = async (item: typeof serviceCatalog[number]) => persistLines([...linesRef.current, { key: commandId(), lineType: 'Service', itemId: item.id, code: item.code, name: item.name, quantity: 1, actualMinutes: item.duration, referencePriceMinor: item.priceMinor, enteredPriceMinor: item.priceMinor }])
  const openAddProduct = (item: typeof productCatalog[number]) => { setProductToAddId(item.id); setProductAddedByEmployeeId(undefined) }
  const confirmAddProduct = async () => {
    const item = productCatalog.find((entry) => entry.id === productToAddId)
    if (!item) return
    const employee = employees.data?.find((entry) => entry.id === productAddedByEmployeeId)
    setProductToAddId(undefined); setProductAddedByEmployeeId(undefined)
    await persistLines([...linesRef.current, { key: commandId(), lineType: 'Product', itemId: item.id, code: item.code, name: item.name, unitName: item.unitName, quantity: 1, referencePriceMinor: item.priceMinor, enteredPriceMinor: item.priceMinor, employeeId: employee?.id, employeeName: employee?.displayName }])
  }
  const updateLine = (key: string, patch: Partial<ClassicCashierDraftLine>) => {
    const next = linesRef.current.map((line) => line.key === key ? { ...line, ...patch } : line)
    setLocalLines(next)
  }
  const persistCurrentLines = () => isBeforeStart ? Promise.resolve() : saveDraft.mutateAsync({ lines: linesRef.current })

  const facilityMutation = useMutation({ mutationFn: ({ path, body }: { path: string; body: object }) => retryVersionConflictOnce(() => apiRequest<FacilityBoardItem>(path, { method: 'POST', body: JSON.stringify(body) })), onError: (error) => message.error(requestError(error)) })
  const startReception = useMutation({
    mutationFn: async () => {
      if (!storeId || !isBeforeStart) throw new Error('当前设施不能开始计时')
      if (linesRef.current.some((line) => line.lineType === 'Service' && !line.employeeId))
        throw new Error('请先为已选服务项目指定服务员工')
      const plannedService = linesRef.current.find((line) => line.lineType === 'Service')
      let started: FacilityBoardItem | undefined
      let initialOrder: ServiceOrder | undefined
      try {
        started = await apiRequest<FacilityBoardItem>('/api/v1/facilities/sessions/start', { method: 'POST', body: JSON.stringify({
          storeId, facilityId: facility.id, customerId: customerId ?? null,
          plannedServiceItemId: plannedService?.itemId ?? null,
          expectedDurationMinutes: plannedService?.actualMinutes ?? null,
          note: note || null, commandId: commandId(),
        }) })
        if (!started.visitId) throw new Error('设施已开始，但接待编号生成失败')
        initialOrder = await apiRequest<ServiceOrder>(`/api/v1/cashier/visits/${started.visitId}/draft`, { method: 'POST', body: JSON.stringify({ storeId, commandId: commandId() }) })
        const saved = await updateDraft(initialOrder, { lines: linesRef.current, customerId, consultantEmployeeId, note, sourceChannel, manualTicketNo, maleGuestCount, maleAgeBand, femaleGuestCount, femaleAgeBand })
        queryClient.setQueryData(['facility-cashier-order', storeId, started.visitId], saved)
        hydratedOrderId.current = saved.id
        return { started, saved }
      } catch (error) {
        if (started) {
          if (initialOrder) { queryClient.setQueryData(['facility-cashier-order', storeId, started.visitId], initialOrder); hydratedOrderId.current = initialOrder.id }
          onFacilityChanged(started); setServiceEnded(false); await onCompleted()
        }
        throw error
      }
    },
    onSuccess: async ({ started }) => { onFacilityChanged(started); setServiceEnded(false); await onCompleted(); message.success('接待信息已保存，设施现在开始计时') },
    onError: (error) => message.error(requestError(error)),
  })
  const facilityOperation = async (action: 'pause' | 'resume' | 'end') => {
    if (!storeId || !facility.sessionId) return
    await draftSaveQueue.current.idle()
    const result = await facilityMutation.mutateAsync({ path: `/api/v1/facilities/sessions/${facility.sessionId}/${action}`, body: { storeId, commandId: commandId() } })
    if (action === 'end') { setServiceEnded(true); onFacilityChanged({ ...facility, status: 'SERVICE_ENDED', sessionId: undefined, activeSeconds: liveSeconds }); message.success('设施计时已结束，账单草稿继续保留') }
    else { onFacilityChanged(result); message.success(action === 'pause' ? '计时已暂停' : '计时已继续') }
    await onCompleted()
  }
  const switchFacility = async (values: SwitchValues) => { if (!storeId || !facility.sessionId) return; await draftSaveQueue.current.idle(); const result = await facilityMutation.mutateAsync({ path: `/api/v1/facilities/sessions/${facility.sessionId}/switch`, body: { storeId, targetFacilityId: values.facilityId, reason: values.reason, commandId: commandId() } }); onFacilityChanged(result); setSwitchOpen(false); switchForm.resetFields(); await onCompleted(); message.success('已更换服务位，原计时段与账单草稿均已保留') }

  const assignEmployee = async (employeeId: string) => { const employee = employees.data?.find((item) => item.id === employeeId); const next = lines.map((line) => line.lineType === 'Service' ? { ...line, employeeId, employeeName: employee?.displayName } : line); setEmployeeOpen(false); await persistLines(next) }
  const assignConsultant = async (employeeId?: string) => { setConsultantEmployeeId(employeeId); setConsultantOpen(false); if (!isBeforeStart) await saveDraft.mutateAsync({ consultantEmployeeId: employeeId }) }
  const applyDiscount = async (values: DiscountValues) => { const next = applyClassicOrderDiscount(lines, values.percent, values.reason); setDiscountOpen(false); discountForm.resetFields(); await persistLines(next); message.success('整单折扣已保存，并已按权限策略完成授权判断') }
  const clearDraft = () => modal.confirm({ title: '删除当前账单内容？', content: isBeforeStart ? '已选择的项目、产品、员工、顾问和主单信息会被清空；设施仍保持空闲。' : '项目、产品、员工、顾问和主单信息会被清空；设施计时与接待记录不会删除，操作会保留审计记录。', okText: '确认删除', okButtonProps: { danger: true }, onOk: async () => { setLocalLines([]); setCustomerId(undefined); setConsultantEmployeeId(undefined); setNote(''); setSourceChannel(''); setManualTicketNo(''); setMaleGuestCount(0); setMaleAgeBand(undefined); setFemaleGuestCount(0); setFemaleAgeBand(undefined); if (!isBeforeStart) await saveDraft.mutateAsync({ lines: [], customerId: undefined, consultantEmployeeId: undefined, note: '', sourceChannel: '', manualTicketNo: '', maleGuestCount: 0, maleAgeBand: undefined, femaleGuestCount: 0, femaleAgeBand: undefined }) } })

  const mergeMutation = useMutation({ mutationFn: async () => { await draftSaveQueue.current.idle(); const target = queryClient.getQueryData<ServiceOrder>(orderKey) ?? draft.data; const source = mergeCandidates.data?.find((item) => item.id === mergeOrderId); if (!storeId || !target || !source) throw new Error('请选择待合并账单'); return apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${target.id}/merge`, { method: 'POST', body: JSON.stringify({ storeId, sourceOrderId: source.id, expectedTargetVersion: target.version, expectedSourceVersion: source.version, commandId: commandId() }) }) }, onSuccess: (order) => { queryClient.setQueryData(orderKey, order); setLines(fromOrder(order)); setCustomerId(order.customerId); setMergeOpen(false); setMergeOrderId(undefined); queryClient.invalidateQueries({ queryKey: ['cashier-orders', storeId] }); message.success('账单已合并，关联接待将在同一笔收款完成') }, onError: (error) => message.error(requestError(error)) })
  const prebillMutation = useMutation({ mutationFn: async () => { const order = queryClient.getQueryData<ServiceOrder>(orderKey) ?? draft.data; if (!storeId || !order) throw new Error('消费单草稿尚未加载'); await persistCurrentLines(); const latest = queryClient.getQueryData<ServiceOrder>(orderKey) ?? order; return apiRequest<ServiceOrderPrebill>(`/api/v1/cashier/orders/${latest.id}/prebill`, { method: 'POST', body: JSON.stringify({ storeId, expectedVersion: latest.version, commandId: commandId() }) }) }, onSuccess: setPrebill, onError: (error) => message.error(requestError(error)) })

  const settleMutation = useMutation({
    mutationFn: async (values: SettleValues) => {
      if (!storeId) throw new Error('当前门店无效')
      if (!lines.length) throw new Error('请至少选择一个项目或产品')
      if (lines.some((line) => line.lineType === 'Service' && !line.employeeId)) throw new Error('请为每个服务项目选择实际服务员工')
      let order = queryClient.getQueryData<ServiceOrder>(orderKey) ?? draft.data
      if (!order) throw new Error('消费单草稿尚未加载')
      if (order.status === 'Draft') order = await saveDraft.mutateAsync({ lines })
      if (!serviceEnded && facility.sessionId) { await facilityMutation.mutateAsync({ path: `/api/v1/facilities/sessions/${facility.sessionId}/end`, body: { storeId, commandId: commandId() } }); setServiceEnded(true); onFacilityChanged({ ...facility, status: 'SERVICE_ENDED', sessionId: undefined, activeSeconds: liveSeconds }); await onCompleted() }
      if (order.priceAuthorizationStatus === 'PendingApproval') return { order, payment: undefined as Payment | undefined, pendingApproval: true }
      if (order.status === 'Draft') { order = await apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${order.id}/confirm`, { method: 'POST', body: JSON.stringify({ storeId, expectedVersion: order.version, commandId: commandId() }) }); queryClient.setQueryData(orderKey, order) }
      if (order.status !== 'PendingPayment') throw new Error('当前账单状态不能继续收款，请返回收银待处理列表查看')
      const method = paymentMethods.data?.find((item) => item.id === values.methodId)
      if (!method) throw new Error('请选择支付方式')
      if (method.channelProvider) return { order, payment: undefined as Payment | undefined, channel: true }
      const payment = await apiRequest<Payment>(`/api/v1/payments/orders/${order.id}/settle`, { method: 'POST', body: JSON.stringify({ storeId, expectedVersion: order.version, commandId: commandId(), cashTenderedMinor: method.category === 'Cash' ? Math.round((values.cashTenderedYuan ?? values.amountYuan) * 100) : null, verifiedMobile: values.verifiedMobile, allocations: [{ methodId: method.id, amountMinor: Math.round(values.amountYuan * 100), memberAccountId: method.category === 'InternalAccount' ? values.memberAccountId : null }] }) })
      return { order, payment, pendingApproval: false, channel: false }
    },
    onSuccess: async (result) => { if (result.pendingApproval) { message.warning('改价已提交审批；审批完成后可在收银待处理列表继续收款'); setSettleOpen(false); navigate('/cashier'); return } if (result.channel) { message.info('官方渠道付款码将在完整收款台生成，当前账单已进入待支付'); setSettleOpen(false); navigate('/cashier'); return } message.success('收款完成，接待和账单均已完成'); setSettleOpen(false); settleForm.resetFields(); await onCompleted(); onExit() },
    onError: (error) => message.error(requestError(error)),
  })
  const openSettlement = () => { if (isBeforeStart) { message.warning('请先确认录单信息并点击“开始计时”'); return } if (!lines.length) { message.warning('请先选择项目或产品'); return } if (lines.some((line) => line.lineType === 'Service' && !line.employeeId)) { message.warning('请先通过“整单员工”或明细选择服务员工'); return } const method = paymentMethods.data?.find((item) => !item.channelProvider) ?? paymentMethods.data?.[0]; settleForm.setFieldsValue({ methodId: method?.id, amountYuan: totalMinor / 100, cashTenderedYuan: totalMinor / 100 }); setSettleOpen(true) }

  if (!isBeforeStart && draft.isLoading) return <div className="modern-cashier-loading"><Spin size="large" description="正在恢复该服务位的账单草稿" /></div>
  if (!isBeforeStart && (draft.error || !draft.data)) return <Alert type="error" showIcon title={requestError(draft.error)} action={<Button onClick={() => draft.refetch()}>重新加载</Button>} />

  return <div className="modern-facility-cashier">{modalContextHolder}
    <header className="modern-cashier-tabs">
      <div className="modern-cashier-room"><b>{facility.displayName}</b><span>{facility.code} · {duration(isBeforeStart ? 0 : liveSeconds)}</span><Tag color={isBeforeStart ? 'gold' : serviceEnded ? 'default' : facility.status === 'PAUSED' ? 'orange' : 'processing'}>{isBeforeStart ? '待开始计时' : serviceEnded ? '服务已结束' : facility.status === 'PAUSED' ? '已暂停' : '服务中'}</Tag></div>
      <button type="button" className={tab === 'main' ? 'active' : ''} onClick={() => setTab('main')}><FileTextOutlined /><span>主单</span><small>信息</small></button>
      <button type="button" onClick={() => navigate('/scheduling')}><ClockCircleOutlined /><span>顾客</span><small>预约</small></button>
      <button type="button" className={tab === 'member' ? 'active' : ''} onClick={() => setTab('member')}><TeamOutlined /><span>会员</span><small>刷卡</small></button>
      <button type="button" className={tab === 'service' ? 'active' : ''} onClick={() => setTab('service')}><AppstoreOutlined /><span>项目</span><small>列表</small></button>
      <button type="button" className={tab === 'product' ? 'active' : ''} onClick={() => setTab('product')}><ShoppingOutlined /><span>产品</span><small>列表</small></button>
      <button type="button" className="is-settle" disabled={isBeforeStart} onClick={openSettlement}>结算</button>
    </header>
    <div className="modern-cashier-body">
      <section className="modern-bill-pane">
        <div className="modern-bill-summary"><span>{selectedCustomer ? `会员：${selectedCustomer.displayName}` : customerDetail.data ? `会员：${customerDetail.data.displayName}` : '散客/暂未关联会员'}</span><b>合计 {money(totalMinor)}</b></div>
        {!lines.length ? <Empty description="当前没有选择消费的任何项目或产品" /> : <div className="modern-bill-lines">{lines.map((line, index) => <article key={line.key}>
          <header><b>{index + 1}. {line.name}</b><Tag>{line.lineType === 'Service' ? '项目' : '产品'}</Tag><Button type="text" danger icon={<DeleteOutlined />} disabled={!editable} onClick={() => persistLines(lines.filter((item) => item.key !== line.key))} /></header>
          <div className="modern-line-meta"><span>编号 {line.code}</span><span>单价 {money(line.enteredPriceMinor)}</span><strong>小计 {money(classicCashierLineAmount(line))}</strong></div>
          <div className="modern-line-edit"><label>数量<InputNumber min={1} max={999} value={line.quantity} disabled={!editable} onChange={(value) => updateLine(line.key, { quantity: Number(value ?? 1) })} onBlur={persistCurrentLines} /></label>{line.lineType === 'Service' && <label>时长<InputNumber min={0} max={1440} value={line.actualMinutes} disabled={!editable} onChange={(value) => updateLine(line.key, { actualMinutes: value === null ? undefined : Number(value) })} onBlur={persistCurrentLines} /><em>分</em></label>}<label>{line.lineType === 'Service' ? '服务员工' : '添加人'}<Select allowClear value={line.employeeId} disabled={!editable} placeholder="可选择员工" options={employees.data?.map((employee) => ({ value: employee.id, label: employee.displayName }))} onChange={(value?: string) => { const employee = employees.data?.find((entry) => entry.id === value); const next = linesRef.current.map((entry) => entry.key === line.key ? { ...entry, employeeId: value, employeeName: employee?.displayName } : entry); void persistLines(next) }} /></label></div>
        </article>)}</div>}
      </section>
      <section className="modern-catalog-pane">
        {tab === 'main' && <div className="modern-main-order"><h2>主单信息</h2><div className="modern-order-facts"><span>消费单号<strong>{draft.data?.orderNo ?? '开始后自动生成'}</strong></span><span>接待编号<strong>{facility.visitNo ?? '开始后自动生成'}</strong></span><span>整单顾问<strong>{draft.data?.consultantEmployeeName ?? employees.data?.find((employee) => employee.id === consultantEmployeeId)?.displayName ?? '未选择'}</strong></span><span>草稿状态<strong>{isBeforeStart ? '待开始计时' : draft.data?.priceAuthorizationStatus === 'PendingApproval' ? '改价待审批' : draft.data?.status}</strong></span></div><label>关联顾客<Select allowClear showSearch optionFilterProp="label" value={customerId} disabled={!editable} placeholder="可暂不识别顾客" options={customers.data?.map((item) => ({ value: item.id, label: `${item.displayName} · ${item.maskedMobile}` }))} onChange={async (value) => { setCustomerId(value); if (!isBeforeStart) await saveDraft.mutateAsync({ customerId: value }) }} /></label><div className="modern-reception-grid"><label>来店渠道<Input value={sourceChannel} disabled={!editable} maxLength={80} placeholder="可自定义，例如朋友介绍、线上平台" onChange={(event) => setSourceChannel(event.target.value)} onBlur={() => { if (!isBeforeStart) void saveDraft.mutateAsync({ sourceChannel }) }} /></label><label>手工票号<Input value={manualTicketNo} disabled={!editable} maxLength={80} placeholder="可选" onChange={(event) => setManualTicketNo(event.target.value)} onBlur={() => { if (!isBeforeStart) void saveDraft.mutateAsync({ manualTicketNo }) }} /></label><label>男客人数<InputNumber min={0} max={99} value={maleGuestCount} disabled={!editable} onChange={(value) => setMaleGuestCount(Number(value ?? 0))} onBlur={() => { if (!isBeforeStart) void saveDraft.mutateAsync({ maleGuestCount }) }} /></label><label>男客年龄段<Select allowClear options={ageBands} value={maleAgeBand} disabled={!editable || maleGuestCount === 0} onChange={setMaleAgeBand} onBlur={() => { if (!isBeforeStart) void saveDraft.mutateAsync({ maleAgeBand }) }} /></label><label>女客人数<InputNumber min={0} max={99} value={femaleGuestCount} disabled={!editable} onChange={(value) => setFemaleGuestCount(Number(value ?? 0))} onBlur={() => { if (!isBeforeStart) void saveDraft.mutateAsync({ femaleGuestCount }) }} /></label><label>女客年龄段<Select allowClear options={ageBands} value={femaleAgeBand} disabled={!editable || femaleGuestCount === 0} onChange={setFemaleAgeBand} onBlur={() => { if (!isBeforeStart) void saveDraft.mutateAsync({ femaleAgeBand }) }} /></label></div><label>接待备注<Input.TextArea rows={3} value={note} disabled={!editable} maxLength={1000} showCount onChange={(event) => setNote(event.target.value)} onBlur={() => { if (!isBeforeStart) void saveDraft.mutateAsync({ note }) }} /></label><Alert type="info" showIcon title={isBeforeStart ? '当前尚未计时。选好顾客、项目、产品和员工后，请点击“开始计时”。' : '设施占用时长仅作运营记录；收费金额只由当前项目、产品和店长确认的成交价决定。'} /></div>}
        {tab === 'member' && <div className="modern-member-search"><div className="modern-catalog-search"><Input prefix={<SearchOutlined />} value={memberSearch} onChange={(event) => setMemberSearch(event.target.value)} placeholder="输入姓名、完整手机号或卡号自动查询" allowClear /></div><div className="modern-member-results">{customers.isFetching && <Spin size="small" />}{customers.data?.map((customer) => <button type="button" key={customer.id} className={customer.id === customerId ? 'active' : ''} disabled={!editable} onClick={async () => { setCustomerId(customer.id); if (!isBeforeStart) await saveDraft.mutateAsync({ customerId: customer.id }); setTab('service') }}><UserOutlined /><b>{customer.displayName}</b><span>{customer.maskedMobile}</span><small>{customer.homeStoreName} · {customer.activeCardCount} 张有效卡</small></button>)}</div></div>}
        {(tab === 'service' || tab === 'product') && <><div className="modern-catalog-search"><Select value="all" options={[{ value: 'all', label: '全部分类' }]} /><Input prefix={<SearchOutlined />} value={catalogSearch} onChange={(event) => setCatalogSearch(event.target.value)} placeholder="输入编号或名称自动查询" allowClear /></div><div className="modern-catalog-grid">{tab === 'service' ? serviceCatalog.map((item) => <button type="button" key={item.id} disabled={!editable || saveDraft.isPending} onClick={() => addService(item)}><small>No.{item.code}</small><b>{item.name}</b><span>{money(item.priceMinor)} / {item.duration ?? '-'} 分钟</span></button>) : productCatalog.map((item) => <button type="button" key={item.id} disabled={!editable || saveDraft.isPending} onClick={() => openAddProduct(item)}><small>No.{item.code}</small><b>{item.name}</b><span>{money(item.priceMinor)} / 库存 {item.stock ?? '-'} {item.unitName}</span></button>)}</div></>}
      </section>
    </div>
    <footer className="modern-cashier-actions">
      {isBeforeStart && <button type="button" className="is-start" disabled={startReception.isPending} onClick={() => startReception.mutate()}><PlayCircleOutlined /><span>开始</span><small>计时</small></button>}
      <button type="button" disabled={!editable} onClick={() => setEmployeeOpen(true)}><TeamOutlined /><span>整单</span><small>员工</small></button>
      <button type="button" disabled={!editable} onClick={() => setConsultantOpen(true)}><UserOutlined /><span>整单</span><small>顾问</small></button>
      <button type="button" disabled={!editable} onClick={() => { discountForm.setFieldsValue({ percent: 100, reason: '' }); setDiscountOpen(true) }}><span>整单</span><small>折扣</small></button>
      <button type="button" disabled={isBeforeStart || serviceEnded} onClick={() => setSwitchOpen(true)}><SwapOutlined /><span>更换</span><small>房台</small></button>
      <button type="button" disabled={isBeforeStart || !editable} onClick={() => setMergeOpen(true)}><MergeCellsOutlined /><span>合并</span><small>账单</small></button>
      <button type="button" disabled={isBeforeStart} onClick={() => prebillMutation.mutate()}><PrinterOutlined /><span>预结</span><small>小票</small></button>
      <button type="button" disabled={!editable} onClick={clearDraft}><DeleteOutlined /><span>删除</span><small>账单</small></button>
      {!serviceEnded && facility.status === 'IN_USE' && <button type="button" onClick={() => facilityOperation('pause')}><PauseCircleOutlined /><span>暂停</span><small>计时</small></button>}
      {!serviceEnded && facility.status === 'PAUSED' && <button type="button" onClick={() => facilityOperation('resume')}><PlayCircleOutlined /><span>继续</span><small>计时</small></button>}
      <button type="button" disabled={isBeforeStart || serviceEnded} onClick={() => facilityOperation('end')}><span>结束</span><small>服务</small></button>
      <button type="button" className="is-return" onClick={onExit}><span>返回</span><small>房台</small></button>
    </footer>

    <Modal title={`添加产品 · ${productCatalog.find((item) => item.id === productToAddId)?.name ?? ''}`} open={Boolean(productToAddId)} onCancel={() => { setProductToAddId(undefined); setProductAddedByEmployeeId(undefined) }} onOk={() => confirmAddProduct()} okText="添加到本单" confirmLoading={saveDraft.isPending} destroyOnHidden><Alert type="info" showIcon title="添加人用于记录是谁将该产品加入本次消费，可选；后续仍可在左侧产品明细中修改。" className="modal-alert" /><label>添加人（可选）<Select allowClear showSearch optionFilterProp="label" className="full-width" value={productAddedByEmployeeId} placeholder="请选择当前门店员工" options={employees.data?.map((employee) => ({ value: employee.id, label: `${employee.displayName} · ${employee.positionName}` }))} onChange={setProductAddedByEmployeeId} /></label></Modal>
    <Modal title="整单员工" open={employeeOpen} onCancel={() => setEmployeeOpen(false)} footer={null}><div className="modern-employee-picker">{employees.data?.map((employee) => <button type="button" key={employee.id} onClick={() => assignEmployee(employee.id)}><b>{employee.displayName}</b><span>{employee.employeeNo} · {employee.positionName}</span></button>)}</div></Modal>
    <Modal title="整单顾问" open={consultantOpen} onCancel={() => setConsultantOpen(false)} footer={null}><div className="modern-employee-picker"><button type="button" onClick={() => assignConsultant(undefined)}><b>不设置顾问</b><span>清除当前整单顾问归属</span></button>{employees.data?.map((employee) => <button type="button" key={employee.id} onClick={() => assignConsultant(employee.id)}><b>{employee.displayName}</b><span>{employee.employeeNo} · {employee.positionName}</span></button>)}</div></Modal>
    <Modal title="整单折扣" open={discountOpen} onCancel={() => setDiscountOpen(false)} onOk={() => discountForm.submit()} okText="应用折扣" confirmLoading={saveDraft.isPending}><Form form={discountForm} layout="vertical" onFinish={applyDiscount}><Form.Item name="percent" label="折后比例（%）" rules={[{ required: true }, { type: 'number', min: 0, max: 100 }]}><InputNumber min={0} max={100} precision={2} className="full-width" /></Form.Item><Form.Item name="reason" label="改价原因" rules={[{ required: true, message: '请输入改价原因' }, { min: 2, max: 500 }]}><Input.TextArea rows={3} maxLength={500} /></Form.Item></Form></Modal>
    <Modal title="更换房台" open={switchOpen} onCancel={() => setSwitchOpen(false)} onOk={() => switchForm.submit()} okText="确认更换" confirmLoading={facilityMutation.isPending}><Form form={switchForm} layout="vertical" onFinish={switchFacility}><Form.Item name="facilityId" label="目标空闲服务位" rules={[{ required: true }]}><Select options={availableFacilities.map((item) => ({ value: item.id, label: `${item.displayName} · ${item.code}` }))} /></Form.Item><Form.Item name="reason" label="更换原因（可选）" rules={[{ max: 500 }]}><Input maxLength={500} /></Form.Item></Form></Modal>
    <Modal title="合并账单" open={mergeOpen} onCancel={() => setMergeOpen(false)} onOk={() => mergeMutation.mutate()} okText="确认合并" confirmLoading={mergeMutation.isPending}><Alert type="warning" showIcon title="合并后，两次接待使用同一张主账单结算；顾客或顾问不一致时系统会阻止合并。" className="modal-alert" /><Select className="full-width" value={mergeOrderId} onChange={setMergeOrderId} placeholder="请选择另一张草稿账单" loading={mergeCandidates.isLoading} options={mergeCandidates.data?.map((order) => ({ value: order.id, label: `${order.orderNo} · ${order.lines.map((line) => line.itemName).join('、') || '空草稿'} · ${money(order.receivableMinor)}` }))} /></Modal>
    <Modal title={`预结小票${prebill ? ` · ${prebill.prebillNo}` : ''}`} open={Boolean(prebill)} onCancel={() => setPrebill(undefined)} footer={<><Button onClick={() => setPrebill(undefined)}>关闭</Button><Button type="primary" onClick={() => window.print()}>打印预览</Button></>}><div className="modern-prebill">{prebill && <><h2>{prebill.storeName}</h2><p>消费单：{prebill.orderNo}</p><p>顾客：{prebill.customerDisplayName}　顾问：{prebill.consultantEmployeeName ?? '未设置'}</p>{prebill.lines.map((line, index) => <div key={`${line.itemCode}-${index}`}><span>{line.itemName} × {line.quantity}{line.employeeName ? ` · ${line.employeeName}` : ''}</span><b>{money(line.lineAmountMinor)}</b></div>)}<footer>应收合计 <b>{money(prebill.receivableMinor)}</b></footer></>}</div></Modal>
    <Modal title="收银结算" open={settleOpen} onCancel={() => setSettleOpen(false)} onOk={() => settleForm.submit()} okText="确认收款" confirmLoading={settleMutation.isPending} width={560}><Alert type="info" showIcon title="设施计时只用于运营记录，本次应收完全来自店长选择的项目、产品和成交价。确认收款时会先结束尚未结束的设施服务。" className="modal-alert" /><Form form={settleForm} layout="vertical" onFinish={(values) => settleMutation.mutate(values)}><Form.Item name="methodId" label="支付方式" rules={[{ required: true }]}><Select options={paymentMethods.data?.map((method) => ({ value: method.id, label: `${method.name}${method.channelProvider ? '（官方渠道）' : method.category === 'ManualExternal' ? '（人工登记待核对）' : ''}` }))} onChange={() => { settleForm.setFieldValue('memberAccountId', undefined); settleForm.setFieldValue('verifiedMobile', undefined) }} /></Form.Item><Form.Item name="amountYuan" label={`收款金额（应收 ${money(totalMinor)}）`} rules={[{ required: true }, { type: 'number', min: totalMinor / 100, max: totalMinor / 100 }]}><InputNumber precision={2} className="full-width" prefix="¥" /></Form.Item>{chosenMethod?.category === 'Cash' && <Form.Item name="cashTenderedYuan" label="现金实收" rules={[{ required: true }, { type: 'number', min: totalMinor / 100 }]}><InputNumber precision={2} className="full-width" prefix="¥" /></Form.Item>}{chosenMethod?.category === 'InternalAccount' && <><Form.Item name="memberAccountId" label="会员账户" rules={[{ required: true }]}><Select loading={customerDetail.isLoading} options={memberAccounts.map((account) => ({ value: account.id, label: account.label }))} /></Form.Item><Form.Item name="verifiedMobile" label="会员完整手机号" rules={[{ required: true }, { pattern: /^1[3-9]\d{9}$/, message: '请输入有效手机号' }]}><Input maxLength={11} inputMode="numeric" /></Form.Item></>}</Form></Modal>
  </div>
}
