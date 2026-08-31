import {
  AlertOutlined,
  ArrowRightOutlined,
  BankOutlined,
  BarChartOutlined,
  ClockCircleOutlined,
  ShopOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import { Alert, Button, Card, Col, Empty, Row, Space, Statistic, Tag, Typography } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { apiRequest } from '../api/client'
import type { DashboardOverview, DashboardStoreSnapshot } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { Permission } from '../security/permissions'
import { useAuthorization } from '../security/useAuthorization'

function money(minor: number) {
  return `¥${(minor / 100).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function compactMoney(minor: number) {
  const amount = minor / 100
  if (Math.abs(amount) >= 10_000) return `¥${(amount / 10_000).toFixed(1)}万`
  return `¥${amount.toLocaleString('zh-CN', { maximumFractionDigits: 0 })}`
}

export function DashboardPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const { can } = useAuthorization()
  const canViewReports = can(Permission.ReportRead)
  const canOperateFacilities = can(Permission.FacilityOperate)
  const isOwner = auth.user?.roles.includes('OWNER') ?? false
  const storeId = auth.store?.id
  const overview = useQuery({
    queryKey: ['dashboard-overview', isOwner ? 'brand' : storeId],
    enabled: canViewReports && Boolean(isOwner || storeId),
    queryFn: () => apiRequest<DashboardOverview>(
      `/api/v1/reports/dashboard-overview${isOwner ? '' : `?storeId=${storeId}`}`,
    ),
  })
  const data = overview.data
  const maxTrend = Math.max(1, ...(data?.trend.map((row) => Math.abs(row.netRevenueMinor)) ?? []))
  const maxMix = Math.max(1, ...(data?.paymentMix.map((row) => row.amountMinor) ?? []))
  const maxStoreRevenue = Math.max(1, ...(data?.stores.map((row) => Math.max(0, row.monthRevenueMinor)) ?? []))
  const storedTotal = data?.storedValueBalanceMinor ?? 0
  const principalShare = storedTotal > 0
    ? Math.round((data?.storedValuePrincipalBalanceMinor ?? 0) / storedTotal * 100)
    : 0

  const openStore = (snapshot: DashboardStoreSnapshot) => {
    const store = auth.user?.stores.find((item) => item.id === snapshot.storeId)
    if (store) auth.setStore(store)
    navigate('/reports')
  }

  return <div className="page-stack dashboard-page">
    <section className="dashboard-hero">
      <div>
        <Typography.Text className="dashboard-eyebrow">经营全局</Typography.Text>
        <Typography.Title level={2}>{isOwner ? '品牌经营驾驶舱' : '门店经营驾驶舱'}</Typography.Title>
        <Typography.Paragraph>
          首页展示当前资金余额、累计经营结果与近 30 天趋势；经营报表继续用于按日期深查。
        </Typography.Paragraph>
      </div>
      <Space wrap>
        <Tag color="cyan" icon={<ShopOutlined />}>{data?.scopeName ?? (isOwner ? '全部门店' : auth.store?.name)}</Tag>
        {canViewReports && <Button onClick={() => navigate('/reports')}>查看详细报表 <ArrowRightOutlined /></Button>}
      </Space>
    </section>

    {!canViewReports ? <Card>
      <Typography.Title level={4}>当前岗位不展示经营资金</Typography.Title>
      <Typography.Paragraph type="secondary">经营总览仅向获授权岗位开放。你仍可进入自己的业务工作区。</Typography.Paragraph>
      {canOperateFacilities && <Button type="primary" onClick={() => navigate('/facilities')}>进入设施接待</Button>}
    </Card> : overview.isError ? <Alert type="error" showIcon title="经营总览读取失败" description="页面没有用零值代替失败数据，请稍后刷新；如持续失败请按追踪号检查服务日志。" /> : <>
      <Row gutter={[16, 16]} className="dashboard-kpis">
        <Col xs={24} md={12} xl={6}>
          <Card className="dashboard-kpi primary" loading={overview.isLoading}>
            <div className="dashboard-kpi-label"><BankOutlined /> 当前总储值余额</div>
            <strong>{money(storedTotal)}</strong>
            <span>本金 {money(data?.storedValuePrincipalBalanceMinor ?? 0)} · 赠送 {money(data?.storedValueBonusBalanceMinor ?? 0)}</span>
          </Card>
        </Col>
        <Col xs={24} md={12} xl={6}>
          <Card className="dashboard-kpi" loading={overview.isLoading}>
            <Statistic title="累计营业净收入" value={(data?.lifetimeRevenueMinor ?? 0) / 100} precision={2} prefix="¥" />
            <span>本月 {money(data?.monthRevenueMinor ?? 0)}</span>
          </Card>
        </Col>
        <Col xs={24} md={12} xl={6}>
          <Card className="dashboard-kpi" loading={overview.isLoading}>
            <Statistic title="今日营业净收入" value={(data?.todayRevenueMinor ?? 0) / 100} precision={2} prefix="¥" />
            <span>{data?.todaySettledOrderCount ?? 0} 单结算 · {data?.todayVisitCount ?? 0} 人次接待</span>
          </Card>
        </Col>
        <Col xs={24} md={12} xl={6}>
          <Card className={`dashboard-kpi attention ${(data?.pendingReconciliationMinor ?? 0) === 0 ? 'is-clear' : ''}`} loading={overview.isLoading}>
            <Statistic title="当前待核对资金" value={(data?.pendingReconciliationMinor ?? 0) / 100} precision={2} prefix={<AlertOutlined />} />
            <span>{data?.pendingReconciliationCount ?? 0} 笔待核对 · {data?.reviewPendingShiftCount ?? 0} 班待复核</span>
          </Card>
        </Col>
      </Row>

      <div className="dashboard-status-strip">
        <span><TeamOutlined /><b>{data?.activeMemberCount ?? 0}</b> 名有效会员</span>
        <span><TeamOutlined /><b>{data?.activeCustomerCount ?? 0}</b> 名有效顾客</span>
        <span><ClockCircleOutlined /><b>{data?.activeFacilityCount ?? 0}</b> 个设施使用中</span>
        <span><ShopOutlined /><b>{data?.openShiftCount ?? 0}</b> 个营业班次</span>
      </div>

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={16}>
          <Card className="dashboard-panel" title="近 30 天营业净收入" extra={<Tag>{data?.trendFromDate} 至 {data?.trendToDate}</Tag>} loading={overview.isLoading}>
            {data?.trend.length ? <div className="dashboard-trend" aria-label="近30天营业净收入柱状图">
              {data.trend.map((row) => <div key={row.date} className="dashboard-trend-day" title={`${row.date} ${money(row.netRevenueMinor)} · ${row.orderCount} 单`}>
                <span>{compactMoney(row.netRevenueMinor)}</span>
                <div><i className={row.netRevenueMinor < 0 ? 'negative' : ''} style={{ height: `${Math.max(row.netRevenueMinor === 0 ? 2 : 8, Math.abs(row.netRevenueMinor) / maxTrend * 150)}px` }} /></div>
                <small>{row.date.slice(5)}</small>
              </div>)}
            </div> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="近 30 天暂无营业数据" />}
          </Card>
        </Col>
        <Col xs={24} xl={8}>
          <Card className="dashboard-panel" title="会员储值资金构成" loading={overview.isLoading}>
            <div className="dashboard-fund">
              <div className="dashboard-fund-ring" style={{ background: storedTotal > 0 ? `conic-gradient(#0f8f83 0 ${principalShare}%, #7c6cf2 ${principalShare}% 100%)` : '#e8edf2' }}>
                <div><strong>{principalShare}%</strong><span>本金占比</span></div>
              </div>
              <div className="dashboard-fund-copy">
                <p><i className="principal" /><span>可退本金余额</span><strong>{money(data?.storedValuePrincipalBalanceMinor ?? 0)}</strong></p>
                <p><i className="bonus" /><span>赠送余额</span><strong>{money(data?.storedValueBonusBalanceMinor ?? 0)}</strong></p>
              </div>
            </div>
            <Typography.Paragraph type="secondary" className="dashboard-footnote">这里展示会员账户当前尚可使用的余额，不是历史充值流水累计值。</Typography.Paragraph>
          </Card>
        </Col>
      </Row>

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={9}>
          <Card className="dashboard-panel" title="近 30 天收款方式构成" extra={<BarChartOutlined />} loading={overview.isLoading}>
            <div className="dashboard-mix-list">
              {data?.paymentMix.length ? data.paymentMix.map((row) => <div key={row.methodCode}>
                <div><strong>{row.methodName}</strong><span>{money(row.amountMinor)} · {row.allocationCount} 笔</span></div>
                <div className="dashboard-track"><i style={{ width: `${row.amountMinor / maxMix * 100}%` }} /></div>
              </div>) : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无收款数据" />}
            </div>
            <Typography.Paragraph type="secondary" className="dashboard-footnote">按结算收款分摊统计，退款请在详细经营报表中查看。</Typography.Paragraph>
          </Card>
        </Col>
        <Col xs={24} xl={15}>
          <Card className="dashboard-panel" title={isOwner ? '各门店经营全景' : '当前门店经营全景'} loading={overview.isLoading}>
            <div className="dashboard-store-list">
              {data?.stores.length ? [...data.stores].sort((a, b) => b.monthRevenueMinor - a.monthRevenueMinor).map((store) => <div key={store.storeId}>
                <div className="dashboard-store-head">
                  <div><strong>{store.storeName}</strong><span>{store.storeCode} · {store.activeMemberCount} 名会员 · {store.activeFacilityCount} 个设施使用中</span></div>
                  <Button type="link" onClick={() => openStore(store)}>明细 <ArrowRightOutlined /></Button>
                </div>
                <div className="dashboard-store-values">
                  <span>今日 <b>{money(store.todayRevenueMinor)}</b></span>
                  <span>本月 <b>{money(store.monthRevenueMinor)}</b></span>
                  <span>累计 <b>{money(store.lifetimeRevenueMinor)}</b></span>
                  <span>储值余额 <b>{money(store.storedValueBalanceMinor)}</b></span>
                </div>
                <div className="dashboard-track"><i style={{ width: `${Math.max(2, Math.max(0, store.monthRevenueMinor) / maxStoreRevenue * 100)}%` }} /></div>
                {(store.pendingReconciliationCount > 0 || store.reviewPendingShiftCount > 0) && <Tag color="orange">待核对 {store.pendingReconciliationCount} 笔 · 待复核 {store.reviewPendingShiftCount} 班</Tag>}
              </div>) : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无门店经营数据" />}
            </div>
          </Card>
        </Col>
      </Row>
    </>}
  </div>
}
