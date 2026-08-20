import {
  BarChartOutlined,
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  ShopOutlined,
  WalletOutlined,
} from '@ant-design/icons'
import { Alert, Button, Card, Col, Empty, Input, Row, Space, Statistic, Table, Tag, Typography } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import type {
  BrandStoreFinancialOverview,
  EmployeeCommission,
  OperationsReport,
  ServicePerformance,
  StoreFinancialOverview,
} from '../api/types'
import { useAuth } from '../auth/useAuth'

function dateString(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function money(minor: number) { return `¥${(minor / 100).toFixed(2)}` }
function duration(seconds: number) {
  if (seconds < 60) return `${seconds} 秒`
  if (seconds < 3600) return `${(seconds / 60).toFixed(1)} 分钟`
  return `${(seconds / 3600).toFixed(1)} 小时`
}

export function ReportsPage() {
  const auth = useAuth()
  const storeId = auth.store?.id
  const isOwner = auth.user?.roles.includes('OWNER') ?? false
  const today = dateString(new Date())
  const weekAgo = new Date()
  weekAgo.setDate(weekAgo.getDate() - 6)
  const [fromDate, setFromDate] = useState(dateString(weekAgo))
  const [toDate, setToDate] = useState(today)

  const report = useQuery({
    queryKey: ['operations-report', storeId, fromDate, toDate],
    enabled: Boolean(storeId && fromDate && toDate),
    queryFn: () => apiRequest<OperationsReport>(
      `/api/v1/reports/operations?storeId=${storeId}&fromDate=${fromDate}&toDate=${toDate}`,
    ),
  })
  const storeOverview = useQuery({
    queryKey: ['store-financial-overview', fromDate, toDate],
    enabled: Boolean(isOwner && fromDate && toDate),
    queryFn: () => apiRequest<BrandStoreFinancialOverview>(
      `/api/v1/reports/store-overview?fromDate=${fromDate}&toDate=${toDate}`,
    ),
  })

  const maxDaily = Math.max(1, ...(report.data?.daily.map((row) => row.settledRevenueMinor) ?? []))
  const maxMix = Math.max(1, ...(report.data?.paymentMix.map((row) => row.amountMinor) ?? []))
  const serviceColumns = [
    { title: '服务项目', dataIndex: 'itemName', render: (value: string, record: ServicePerformance) => <div className="audit-action"><strong>{value}</strong><Typography.Text type="secondary">{record.itemCode}</Typography.Text></div> },
    { title: '结算单数', dataIndex: 'orderCount', align: 'right' as const },
    { title: '数量', dataIndex: 'quantity', align: 'right' as const },
    { title: '成交金额', dataIndex: 'revenueMinor', align: 'right' as const, render: (value: number) => <strong>{money(value)}</strong> },
  ]
  const commissionColumns = [
    { title: '服务员工', dataIndex: 'employeeName', render: (value: string, record: EmployeeCommission) => <div className="audit-action"><strong>{value}</strong><Typography.Text type="secondary">{record.employeeNo}</Typography.Text></div> },
    { title: '服务数量', dataIndex: 'serviceQuantity', align: 'right' as const },
    { title: '结算单数', dataIndex: 'orderCount', align: 'right' as const },
    { title: '服务成交额', dataIndex: 'grossServiceRevenueMinor', align: 'right' as const, render: (value: number) => money(value) },
    { title: '提成毛额', dataIndex: 'grossCommissionMinor', align: 'right' as const, render: (value: number) => money(value) },
    { title: '退款冲减', dataIndex: 'refundDeductionMinor', align: 'right' as const, render: (value: number) => <Typography.Text type={value > 0 ? 'danger' : undefined}>{money(value)}</Typography.Text> },
    { title: '净提成', dataIndex: 'netCommissionMinor', align: 'right' as const, render: (value: number) => <strong>{money(value)}</strong> },
  ]
  const storeColumns = [
    { title: '门店', fixed: 'left' as const, width: 190, render: (_: unknown, row: StoreFinancialOverview) => <div className="audit-action"><strong>{row.storeName}</strong><Typography.Text type="secondary">{row.storeCode} · {row.timeZoneId}</Typography.Text></div> },
    { title: '今日收入', dataIndex: 'todayRevenueMinor', align: 'right' as const, width: 130, render: (value: number) => <strong>{money(value)}</strong> },
    { title: '期间收入', dataIndex: 'periodNetRevenueMinor', align: 'right' as const, width: 130, render: (value: number) => money(value) },
    { title: '累计储值净额', dataIndex: 'storedValueNetMinor', align: 'right' as const, width: 150, render: (value: number, row: StoreFinancialOverview) => <div className="audit-action"><strong>{money(value)}</strong><Typography.Text type="secondary">本金 {money(row.storedValuePrincipalMinor)} · 赠送 {money(row.storedValueBonusMinor)}</Typography.Text></div> },
    { title: '资金待核对', dataIndex: 'pendingReconciliationMinor', align: 'right' as const, width: 150, render: (value: number, row: StoreFinancialOverview) => value > 0 ? <Tag color="orange">{money(value)} · {row.pendingReconciliationCount} 笔</Tag> : <Tag color="green">已清</Tag> },
    { title: '渠道差异', dataIndex: 'channelDifferenceCount', align: 'center' as const, width: 110, render: (value: number) => value > 0 ? <Tag color="red">{value} 条</Tag> : <Tag color="green">0 条</Tag> },
    { title: '交班状态', align: 'right' as const, width: 170, render: (_: unknown, row: StoreFinancialOverview) => <div className="audit-action"><strong>营业中 {row.openShiftCount} 班</strong><Typography.Text type="secondary">待复核 {row.reviewPendingShiftCount} 班 · {money(row.reviewPendingShiftAmountMinor)}</Typography.Text></div> },
    { title: '操作', fixed: 'right' as const, width: 120, render: (_: unknown, row: StoreFinancialOverview) => <Button type="link" onClick={() => { const store = auth.user?.stores.find((item) => item.id === row.storeId); if (store) auth.setStore(store) }}>查看门店明细</Button> },
  ]

  return <div className="page-stack">
    <div className="page-heading">
      <div><Typography.Title level={2}>经营报表</Typography.Title><Typography.Paragraph>门店收入、储值资金和待对账分别核算；充值不计入营业收入。</Typography.Paragraph></div>
      <Space><Input aria-label="开始日期" type="date" value={fromDate} max={toDate} onChange={(event) => setFromDate(event.target.value)} /><span>至</span><Input aria-label="结束日期" type="date" value={toDate} min={fromDate} max={today} onChange={(event) => setToDate(event.target.value)} /></Space>
    </div>

    {isOwner && <Card variant="borderless" title="品牌多门店经营与对账总览" extra={<Tag color="purple">总账号可见</Tag>}>
      <Alert type="info" showIcon title="收入按消费发生门店统计；储值按充值门店归属，展示未退款本金与未收回赠送金；品牌内跨店消费不会重复计算充值。" className="report-inline-alert" />
      <Row gutter={[16, 16]} className="report-overview-metrics">
        <Col xs={24} sm={12} xl={6}><Statistic title="各店今日收入合计" value={(storeOverview.data?.todayRevenueMinor ?? 0) / 100} precision={2} prefix="¥" /></Col>
        <Col xs={24} sm={12} xl={6}><Statistic title="所选期间收入合计" value={(storeOverview.data?.periodNetRevenueMinor ?? 0) / 100} precision={2} prefix="¥" /></Col>
        <Col xs={24} sm={12} xl={6}><Statistic title="品牌累计储值净额" value={(storeOverview.data?.storedValueNetMinor ?? 0) / 100} precision={2} prefix={<WalletOutlined />} /></Col>
        <Col xs={24} sm={12} xl={6}><Statistic title="全部门店待核对" value={(storeOverview.data?.pendingReconciliationMinor ?? 0) / 100} precision={2} prefix={<ExclamationCircleOutlined />} suffix={storeOverview.data?.channelDifferenceCount ? ` · ${storeOverview.data.channelDifferenceCount} 条差异` : undefined} /></Col>
      </Row>
      <Table<StoreFinancialOverview> rowKey="storeId" columns={storeColumns} dataSource={storeOverview.data?.stores} loading={storeOverview.isLoading} pagination={false} scroll={{ x: 1_120 }} locale={{ emptyText: <Empty description="暂无门店经营数据" /> }} />
    </Card>}

    <Alert type="info" showIcon title={`当前门店口径按时区 ${report.data?.timeZoneId ?? '加载中'} 计算；单次最多查询 92 天。`} />
    <Row gutter={[16, 16]}>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric"><Statistic title="当前门店今日收入" value={(report.data?.summary.todayRevenueMinor ?? 0) / 100} precision={2} prefix="¥" /><span>今日结算减今日已完成退款</span></Card></Col>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric"><Statistic title="当前门店期间收入" value={(report.data?.summary.netRevenueMinor ?? 0) / 100} precision={2} prefix="¥" /><span>所选期间结算净额</span></Card></Col>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric"><Statistic title="累计储值净额" value={(report.data?.summary.storedValueNetMinor ?? 0) / 100} precision={2} prefix={<WalletOutlined />} /><span>本金 {money(report.data?.summary.storedValuePrincipalMinor ?? 0)} · 赠送 {money(report.data?.summary.storedValueBonusMinor ?? 0)}</span></Card></Col>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric warning"><Statistic title="外部待核对" value={(report.data?.summary.pendingReconciliationMinor ?? 0) / 100} precision={2} prefix={<ExclamationCircleOutlined />} /><span>不能视为渠道已确认</span></Card></Col>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric"><Statistic title="业务已结算" value={(report.data?.summary.settledRevenueMinor ?? 0) / 100} precision={2} prefix="¥" /><span>退款前结算总额</span></Card></Col>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric warning"><Statistic title="已完成退款" value={(report.data?.summary.refundMinor ?? 0) / 100} precision={2} prefix="¥" /><span>按退款完成日冲减</span></Card></Col>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric"><Statistic title="已记录资金净额" value={(report.data?.summary.recordedFundsMinor ?? 0) / 100} precision={2} prefix={<WalletOutlined />} /><span>允许出现退款净流出</span></Card></Col>
      <Col xs={24} sm={12} xl={6}><Card className="report-metric"><Statistic title="结算客单价" value={(report.data?.summary.averageTicketMinor ?? 0) / 100} precision={2} prefix="¥" /><span>{report.data?.summary.settledOrderCount ?? 0} 张结算单</span></Card></Col>
    </Row>

    <Row gutter={[16, 16]}>
      <Col xs={24} xl={15}><Card variant="borderless" title="每日收入（结算净额）走势" extra={<Tag>{report.data?.fromDate} 至 {report.data?.toDate}</Tag>}><div className="daily-chart">{report.data?.daily.map((row) => <div key={row.date} className="daily-column"><div className="daily-value">{money(row.netRevenueMinor)}</div><div className="daily-bars"><span className="bar-pending" style={{ height: `${row.pendingReconciliationMinor <= 0 ? 0 : Math.max(3, row.pendingReconciliationMinor / maxDaily * 150)}px` }} title={`待核对 ${money(row.pendingReconciliationMinor)}`} /><span className="bar-recorded" style={{ height: `${row.recordedFundsMinor <= 0 ? 0 : Math.max(3, row.recordedFundsMinor / maxDaily * 150)}px` }} title={`已记录净额 ${money(row.recordedFundsMinor)}`} /></div><strong>{row.date.slice(5)}</strong><small>退款 {money(row.refundMinor)}</small></div>)}</div><div className="chart-legend"><span><i className="recorded-dot" />已记录资金净额</span><span><i className="pending-dot" />外部待核对</span></div></Card></Col>
      <Col xs={24} xl={9}><Card variant="borderless" title="支付构成"><div className="mix-list">{report.data?.paymentMix.length ? report.data.paymentMix.map((row) => <div key={row.methodCode}><div><strong>{row.methodName}</strong><span>净额 {money(row.netAmountMinor)} · {row.allocationCount} 笔收款</span></div><div className="mix-track"><span style={{ width: `${Math.max(0, row.netAmountMinor) / maxMix * 100}%` }} /></div>{row.refundMinor > 0 && <Tag color="red">退款 {money(row.refundMinor)}</Tag>}{row.pendingReconciliationMinor > 0 && <Tag color="orange">待核对 {money(row.pendingReconciliationMinor)}</Tag>}</div>) : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无支付数据" />}</div></Card></Col>
    </Row>

    <Row gutter={[16, 16]}>
      <Col xs={24} xl={15}><Card variant="borderless" title="服务项目成交排行"><Table<ServicePerformance> rowKey="serviceItemId" columns={serviceColumns} dataSource={report.data?.services} pagination={false} locale={{ emptyText: <Empty description="暂无已结算项目" /> }} /></Card></Col>
      <Col xs={24} xl={9}><Card variant="borderless" title="设施记录占用"><Alert type="warning" showIcon title="仅展示记录时长及其占比，不等于营业时间利用率。" className="report-inline-alert" /><div className="facility-usage-list">{report.data?.facilities.length ? report.data.facilities.map((row) => <div key={row.facilityId}><div><strong><ShopOutlined /> {row.facilityName}</strong><span>{duration(row.activeSeconds)}</span></div><div className="mix-track facility"><span style={{ width: `${row.usageShare * 100}%` }} /></div><small>占全部设施记录时长 {(row.usageShare * 100).toFixed(1)}%</small></div>) : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无设施占用记录" />}</div></Card></Col>
    </Row>
    <Card variant="borderless" title="员工服务收益与提成"><Alert type="info" showIcon title="提成来自结算时锁定的服务员工和规则快照；退款按退款金额占整单应收的比例冲减，当前为收益核算数据，不代表工资已经发放。" className="report-inline-alert" /><Table<EmployeeCommission> rowKey="employeeId" columns={commissionColumns} dataSource={report.data?.employeeCommissions} pagination={false} locale={{ emptyText: <Empty description="暂无已结算员工服务数据" /> }} scroll={{ x: 900 }} /></Card>
    <Row gutter={[16, 16]}><Col xs={24} md={8}><Card variant="borderless"><Statistic title="接待次数" value={report.data?.summary.visitCount ?? 0} prefix={<ShopOutlined />} suffix="次" /></Card></Col><Col xs={24} md={8}><Card variant="borderless"><Statistic title="设施记录占用" value={(report.data?.summary.facilityActiveSeconds ?? 0) / 3600} precision={1} prefix={<ClockCircleOutlined />} suffix="小时" /></Card></Col><Col xs={24} md={8}><Card variant="borderless"><Statistic title="数据更新" value={report.isFetching ? '刷新中' : '已读取'} prefix={<BarChartOutlined />} /></Card></Col></Row>
  </div>
}
