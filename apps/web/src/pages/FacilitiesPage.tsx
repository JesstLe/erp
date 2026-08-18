import { ClockCircleOutlined, PauseCircleOutlined, PlayCircleOutlined, SettingOutlined, SwapOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Form, Input, InputNumber, Modal, Select, Space, Statistic, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiRequest, ApiError } from '../api/client'
import type { FacilityBoard, FacilityBoardItem } from '../api/types'
import { useAuth } from '../auth/useAuth'

const statusMeta: Record<string, { label: string; color: string }> = {
  AVAILABLE: { label: '可用', color: 'green' }, IN_USE: { label: '使用中', color: 'blue' }, PAUSED: { label: '已暂停', color: 'orange' },
  CLEANING_REQUIRED: { label: '待清洁', color: 'purple' }, MAINTENANCE: { label: '维护中', color: 'red' }, DISABLED: { label: '已停用', color: 'default' },
}

interface StartValues { expectedDurationMinutes?: number; note?: string }
interface SwitchValues { targetFacilityId: string; reason?: string }

function commandId() { return crypto.randomUUID() }
function formatDuration(seconds: number) { const hours = Math.floor(seconds / 3600); const minutes = Math.floor((seconds % 3600) / 60); const rest = seconds % 60; return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}` : `${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}` }

export function FacilitiesPage() {
  const auth = useAuth(); const storeId = auth.store?.id; const queryClient = useQueryClient(); const navigate = useNavigate()
  const [tick, setTick] = useState(Date.now()); const [startTarget, setStartTarget] = useState<FacilityBoardItem>(); const [switchTarget, setSwitchTarget] = useState<FacilityBoardItem>()
  const [startForm] = Form.useForm<StartValues>(); const [switchForm] = Form.useForm<SwitchValues>()
  useEffect(() => { const timer = window.setInterval(() => setTick(Date.now()), 1000); return () => window.clearInterval(timer) }, [])
  const board = useQuery({ queryKey: ['facility-board', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<FacilityBoard>(`/api/v1/facilities/board?storeId=${storeId}`), refetchInterval: 30_000 })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['facility-board', storeId] })
  const mutate = useMutation({ mutationFn: ({ path, body }: { path: string; body: object }) => apiRequest<FacilityBoardItem>(path, { method: 'POST', body: JSON.stringify(body) }), onSuccess: async () => { await refresh() }, onError: (error) => message.error(error instanceof ApiError ? error.message : '操作失败') })
  const available = board.data?.groups.flatMap((group) => group.facilities).filter((item) => item.status === 'AVAILABLE') ?? []
  const start = async (values: StartValues) => { if (!startTarget || !storeId) return; await mutate.mutateAsync({ path: '/api/v1/facilities/sessions/start', body: { storeId, facilityId: startTarget.id, ...values, commandId: commandId() } }); message.success('设施已开始使用'); setStartTarget(undefined); startForm.resetFields() }
  const operation = async (item: FacilityBoardItem, action: 'pause' | 'resume' | 'end') => { if (!storeId || !item.sessionId) return; await mutate.mutateAsync({ path: `/api/v1/facilities/sessions/${item.sessionId}/${action}`, body: { storeId, commandId: commandId() } }); message.success(action === 'pause' ? '计时已暂停' : action === 'resume' ? '计时已继续' : '服务已结束，未产生收费') }
  const switchFacility = async (values: SwitchValues) => { if (!storeId || !switchTarget?.sessionId) return; await mutate.mutateAsync({ path: `/api/v1/facilities/sessions/${switchTarget.sessionId}/switch`, body: { storeId, ...values, commandId: commandId() } }); message.success('设施已更换，原计时记录已保留'); setSwitchTarget(undefined); switchForm.resetFields() }
  const completeCleaning = async (item: FacilityBoardItem) => { if (!storeId) return; await mutate.mutateAsync({ path: `/api/v1/facilities/${item.id}/cleaning/complete`, body: { storeId, commandId: commandId() } }); message.success('清洁已完成，设施恢复可用') }
  const canConfigure = auth.user?.roles.some((role) => role === 'OWNER' || role === 'STORE_MANAGER')

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>设施接待</Typography.Title><Typography.Paragraph>点击可用设施后再明确开始；计时只记录占用，不参与自动收费。</Typography.Paragraph></div><Space>{canConfigure && <Button icon={<SettingOutlined />} onClick={() => navigate('/settings/facilities')}>门店设施配置</Button>}<Button onClick={() => refresh()} loading={board.isFetching}>刷新状态</Button></Space></div>
    {board.error && <Alert type="error" showIcon title={board.error instanceof Error ? board.error.message : '设施看板加载失败'} />}
    {!board.isLoading && !board.data?.groups.length && <Card variant="borderless"><Empty description="当前门店还没有设施，请先打开门店设施配置" /></Card>}
    {board.data?.groups.map((group) => <section key={group.id} className="facility-group"><Typography.Title level={4}>{group.displayName}<Typography.Text type="secondary"> · {group.facilities.length} 个设施</Typography.Text></Typography.Title><div className="facility-grid">{group.facilities.map((item) => {
      const meta = statusMeta[item.status] ?? { label: item.status, color: 'default' }; const liveSeconds = item.startedAtUtc && item.status === 'IN_USE' ? item.activeSeconds + Math.max(0, Math.floor((tick - board.dataUpdatedAt) / 1000)) : item.activeSeconds
      return <Card key={item.id} className={`facility-card status-${item.status.toLowerCase()}`} hoverable={item.status === 'AVAILABLE'} onClick={() => item.status === 'AVAILABLE' && setStartTarget(item)}>
        <div className="facility-card-head"><div><Typography.Text type="secondary">{item.code} · {item.typeName}</Typography.Text><Typography.Title level={4}>{item.displayName}</Typography.Title></div><Tag color={meta.color}>{meta.label}</Tag></div>
        {(item.serviceName || item.equipmentName || (item.referencePriceMinor !== undefined && item.referencePriceMinor !== null)) && <div className="facility-session-copy">{item.serviceName && <span>服务：{item.serviceName}</span>}{item.equipmentName && <span>设施：{item.equipmentName}</span>}{item.referencePriceMinor !== undefined && item.referencePriceMinor !== null && <span>参考 ¥{(item.referencePriceMinor / 100).toFixed(2)}（不自动收费）</span>}</div>}
        {item.sessionId ? <><Statistic title="实际占用时长" value={formatDuration(liveSeconds)} prefix={<ClockCircleOutlined />} /><div className="facility-session-copy"><span>接待单 {item.visitNo}</span>{item.expectedDurationMinutes && <span>预计 {item.expectedDurationMinutes} 分钟</span>}{item.note && <span>{item.note}</span>}</div><Space wrap onClick={(event) => event.stopPropagation()}>{item.status === 'IN_USE' && <Button size="small" icon={<PauseCircleOutlined />} onClick={() => operation(item, 'pause')}>暂停</Button>}{item.status === 'PAUSED' && <Button size="small" icon={<PlayCircleOutlined />} onClick={() => operation(item, 'resume')}>继续</Button>}<Button size="small" icon={<SwapOutlined />} onClick={() => setSwitchTarget(item)}>换设施</Button><Button size="small" danger onClick={() => operation(item, 'end')}>结束服务</Button></Space></> : item.status === 'CLEANING_REQUIRED' ? <Button type="primary" onClick={(event) => { event.stopPropagation(); completeCleaning(item) }}>完成清洁</Button> : <Typography.Text type="secondary">{item.status === 'AVAILABLE' ? '点击后填写接待信息并开始使用' : '当前不可接待'}</Typography.Text>}
      </Card>})}</div></section>)}
    <Modal title={`开始使用 · ${startTarget?.displayName ?? ''}`} open={Boolean(startTarget)} onCancel={() => setStartTarget(undefined)} onOk={() => startForm.submit()} confirmLoading={mutate.isPending} okText="确认开始" cancelText="取消" destroyOnHidden><Alert type="info" showIcon title="开始时间由服务器记录；设施时长不会自动形成收费。" className="modal-alert" /><Form form={startForm} layout="vertical" onFinish={start}><Form.Item name="expectedDurationMinutes" label="预计时长（分钟，可选）" rules={[{ type: 'number', min: 1, max: 1440 }]}><InputNumber min={1} max={1440} precision={0} className="full-width" /></Form.Item><Form.Item name="note" label="接待备注（可选）" rules={[{ max: 500 }]}><Input.TextArea rows={3} maxLength={500} showCount placeholder="不要填写不必要的敏感信息" /></Form.Item></Form></Modal>
    <Modal title={`更换设施 · ${switchTarget?.displayName ?? ''}`} open={Boolean(switchTarget)} onCancel={() => setSwitchTarget(undefined)} onOk={() => switchForm.submit()} confirmLoading={mutate.isPending} okText="确认更换" destroyOnHidden><Form form={switchForm} layout="vertical" onFinish={switchFacility}><Form.Item name="targetFacilityId" label="目标设施" rules={[{ required: true, message: '请选择可用设施' }]}><Select options={available.filter((item) => item.id !== switchTarget?.id).map((item) => ({ value: item.id, label: `${item.displayName} · ${item.code}` }))} /></Form.Item><Form.Item name="reason" label="更换原因（可选）" rules={[{ max: 500 }]}><Input maxLength={500} /></Form.Item></Form></Modal>
  </div>
}
