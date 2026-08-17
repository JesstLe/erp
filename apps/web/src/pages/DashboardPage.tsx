import { ArrowRightOutlined, ClockCircleOutlined, ExclamationCircleOutlined, SafetyCertificateOutlined, ShopOutlined } from '@ant-design/icons'
import { Button, Card, Col, Row, Space, Statistic, Tag, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { useQuery } from '@tanstack/react-query'
import { apiRequest } from '../api/client'
import type { FacilityBoard, OperationsReport } from '../api/types'

export function DashboardPage() {
  const auth = useAuth(); const navigate = useNavigate()
  const storeId = auth.store?.id; const today = new Date(); const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  const report = useQuery({ queryKey: ['operations-report-today', storeId, date], enabled: Boolean(storeId), queryFn: () => apiRequest<OperationsReport>(`/api/v1/reports/operations?storeId=${storeId}&fromDate=${date}&toDate=${date}`) })
  const board = useQuery({ queryKey: ['facility-board', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<FacilityBoard>(`/api/v1/facilities/board?storeId=${storeId}`) })
  const inUse = board.data?.groups.flatMap((group) => group.facilities).filter((item) => item.status === 'IN_USE' || item.status === 'PAUSED').length ?? 0
  return <div className="page-stack">
    <section className="welcome-strip"><div><Typography.Text>今日经营工作台</Typography.Text><Typography.Title level={2}>你好，{auth.user?.displayName}</Typography.Title><Typography.Paragraph>指标来自当前门店真实业务记录；外部待核对金额不会并入“已记录资金”。</Typography.Paragraph></div><Tag color="green" icon={<SafetyCertificateOutlined />}>权限与审计已启用</Tag></section>
    <Row gutter={[16, 16]}>
      <Col xs={24} md={12} xl={6}><Card className="metric-card"><Statistic title="今日接待" value={report.data?.summary.visitCount ?? 0} suffix="人次" prefix={<ShopOutlined />} /><span>按接待到店时间统计</span></Card></Col>
      <Col xs={24} md={12} xl={6}><Card className="metric-card"><Statistic title="使用中设施" value={inUse} suffix="个" prefix={<ClockCircleOutlined />} /><span>包含暂停中的设施</span></Card></Col>
      <Col xs={24} md={12} xl={6}><Card className="metric-card"><Statistic title="今日已记录资金" value={(report.data?.summary.recordedFundsMinor ?? 0) / 100} precision={2} prefix="¥" /><span>不含人工外部待核对</span></Card></Col>
      <Col xs={24} md={12} xl={6}><Card className="metric-card warning"><Statistic title="外部待核对" value={(report.data?.summary.pendingReconciliationMinor ?? 0) / 100} precision={2} prefix={<ExclamationCircleOutlined />} /><span>需在交班和财务持续处置</span></Card></Col>
    </Row>
    <Row gutter={[16, 16]}><Col xs={24} xl={15}><Card title="门店服务闭环" extra={<Tag color="green">已贯通</Tag>} className="flow-card"><div className="flow-steps">{['选择设施', '开始计时', '结束服务', '录入项目', '支付分摊', '交班审计'].map((label, index) => <div key={label}><span>{String(index + 1).padStart(2, '0')}</span><strong>{label}</strong></div>)}</div></Card></Col><Col xs={24} xl={9}><Card title="快速开始" className="quick-card"><Space orientation="vertical" size={12} className="full-width"><Button block size="large" onClick={() => navigate('/facilities')}>开始设施接待 <ArrowRightOutlined /></Button><Button block size="large" onClick={() => navigate('/cashier')}>服务录单与收银 <ArrowRightOutlined /></Button><Button block size="large" onClick={() => navigate('/reports')}>查看经营报表 <ArrowRightOutlined /></Button></Space></Card></Col></Row>
  </div>
}
