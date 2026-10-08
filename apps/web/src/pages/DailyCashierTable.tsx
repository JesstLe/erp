import { ReloadOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Descriptions, Input, Space, Table, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import type { DailyCashierChannel, DailyCashierReport } from '../api/types'

const money = (minor: number) => `¥${(minor / 100).toFixed(2)}`
const amount = (minor: number) => <span className={`daily-cashier-amount${minor === 0 ? ' is-zero' : ''}${minor < 0 ? ' is-negative' : ''}`}>{money(minor)}</span>
const columns: TableColumnsType<DailyCashierChannel> = [
  { title: '收款方式', dataIndex: 'name', fixed: 'left', width: 110 },
  { title: '顾客消费', children: [
    { title: '收款', dataIndex: 'consumptionMinor', align: 'right', width: 120, render: amount },
    { title: '退款', dataIndex: 'consumptionRefundMinor', align: 'right', width: 120, render: amount },
    { title: '净营业额', align: 'right', width: 130, render: (_, row) => amount(row.consumptionMinor - row.consumptionRefundMinor) },
  ] },
  { title: '会员储值（本金）', children: [
    { title: '收款', dataIndex: 'topupMinor', align: 'right', width: 120, render: amount },
    { title: '退款', dataIndex: 'topupRefundMinor', align: 'right', width: 120, render: amount },
    { title: '净储值', align: 'right', width: 130, render: (_, row) => amount(row.topupMinor - row.topupRefundMinor) },
  ] },
  { title: '收款待核对', dataIndex: 'pendingReconciliationMinor', align: 'right', width: 130,
    render: (minor: number) => <span className={minor > 0 ? 'daily-cashier-pending' : ''}>{amount(minor)}</span> },
]

export function DailyCashierTable({ storeId }: { storeId: string }) {
  const [selectedDate, setSelectedDate] = useState<string>()
  const report = useQuery({
    queryKey: ['daily-cashier-report', storeId, selectedDate ?? 'today'],
    queryFn: ({ signal }) => apiRequest<DailyCashierReport>(
      `/api/v1/reports/daily-cashier?${new URLSearchParams({ storeId, ...(selectedDate ? { date: selectedDate } : {}) })}`, { signal }),
    refetchInterval: selectedDate ? false : 60_000,
  })
  const data = report.data
  const summary = data?.summary
  return <Card variant="borderless" className="daily-cashier-card" title="营业日报" extra={<Space wrap>
    <Input type="date" aria-label="营业日报日期" value={selectedDate ?? data?.date ?? ''}
      onChange={(event) => setSelectedDate(event.target.value || undefined)} />
    <Button onClick={() => { setSelectedDate(undefined); if (!selectedDate) void report.refetch() }}>今天</Button>
    <Button icon={<ReloadOutlined />} aria-label="刷新营业日报" loading={report.isFetching} onClick={() => report.refetch()}>刷新</Button>
  </Space>}>
    {report.isError ? <Alert type="error" showIcon title="营业日报加载失败" description="请刷新重试，或检查当前门店的报表查看权限。" /> : <>
      <Typography.Paragraph type="secondary" className="daily-cashier-caption">
        {data ? `${data.storeName} · ${data.date} · 按门店时区 ${data.timeZoneId} 统计` : '正在读取当日营业数据…'}
      </Typography.Paragraph>
      {summary && <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }} className="daily-cashier-overview" items={[
        { key: 'orders', label: '消费结算', children: `${summary.consumptionOrderCount} 单` },
        { key: 'topups', label: '储值入账', children: `${summary.topupCount} 笔` },
        { key: 'bonus', label: '赠送金额', children: amount(summary.bonusMinor) },
        { key: 'revoked', label: '退款收回赠金', children: amount(summary.revokedBonusMinor) },
      ]} />}
      <Table<DailyCashierChannel> rowKey="code" bordered size="small" columns={columns} dataSource={data?.channels ?? []}
        loading={report.isPending} pagination={false} scroll={{ x: 980 }} locale={{ emptyText: '等待营业数据' }}
        summary={() => summary && <Table.Summary.Row>
          <Table.Summary.Cell index={0}><strong>合计</strong></Table.Summary.Cell>
          {[summary.consumptionMinor, summary.consumptionRefundMinor, summary.netRevenueMinor, summary.topupMinor,
            summary.topupRefundMinor, summary.netTopupMinor, summary.pendingReconciliationMinor].map((minor, index) =>
            <Table.Summary.Cell index={index + 1} key={index} align="right"><strong>{amount(minor)}</strong></Table.Summary.Cell>)}
        </Table.Summary.Row>} />
      <Typography.Paragraph type="secondary" className="daily-cashier-notes">
        消费按结算日、退款按完成日计入；净营业额＝消费收款－消费退款。储值和赠金不计营业额，会员余额扣款属于消费，不是新增现金。
        待核对金额已包含在收款中，不重复相加；人工微信、支付宝和团购登记不代表渠道已确认。
      </Typography.Paragraph>
      {data && <Typography.Text type="secondary" className="daily-cashier-updated">更新于 {new Intl.DateTimeFormat('zh-CN', {
        timeZone: data.timeZoneId, hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
      }).format(new Date(data.generatedAtUtc))}{!selectedDate ? ' · 每分钟自动刷新' : ''}</Typography.Text>}
    </>}
  </Card>
}
