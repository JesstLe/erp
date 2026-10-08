import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Empty, Modal, Space, Table, Tabs, Tag, Typography } from 'antd'
import { apiRequest } from '../api/client'
import type { MemberTopup, PageResult, ServiceOrder } from '../api/types'

export type MemberHistoryTab = 'topups' | 'orders'
interface Props {
  storeId: string
  storeName?: string
  customerId: string
  customerName: string
  initialTab: MemberHistoryTab
  canReadTopups: boolean
  canReadOrders: boolean
  onClose: () => void
}
const money = (value: number) => `¥${(value / 100).toFixed(2)}`
const time = (value: string) => new Date(value).toLocaleString('zh-CN', { hour12: false })
const orderStatus: Record<string, string> = {
  Draft: '待确认金额', PendingPayment: '待支付', PaymentProcessing: '支付处理中',
  Settled: '已结算', PartiallyRefunded: '部分退款', Refunded: '已退款', Voided: '已作废',
}
const topupStatus: Record<string, string> = { Paid: '已入账', PartiallyRefunded: '部分退款', Refunded: '已退款' }

export function MemberHistoryModal({ storeId, storeName, customerId, customerName, initialTab,
  canReadTopups, canReadOrders, onClose }: Props) {
  const [tab, setTab] = useState<MemberHistoryTab>(initialTab)
  const [topupPage, setTopupPage] = useState(1)
  const [orderPage, setOrderPage] = useState(1)
  const pageSize = 10
  const topups = useQuery({
    queryKey: ['member-topups', storeId, customerId, topupPage],
    enabled: tab === 'topups' && canReadTopups,
    queryFn: ({ signal }) => apiRequest<PageResult<MemberTopup>>(
      `/api/v1/member-topups?${new URLSearchParams({ storeId, customerId, page: String(topupPage), pageSize: String(pageSize) })}`, { signal }),
  })
  const orders = useQuery({
    queryKey: ['customer-orders', storeId, customerId, orderPage],
    enabled: tab === 'orders' && canReadOrders,
    queryFn: ({ signal }) => apiRequest<PageResult<ServiceOrder>>(
      `/api/v1/cashier/orders?${new URLSearchParams({ storeId, customerId, page: String(orderPage), pageSize: String(pageSize) })}`, { signal }),
  })
  return <Modal open title={`${customerName} · 会员记录`} width={900} onCancel={onClose}
    footer={<Button onClick={onClose}>关闭</Button>} destroyOnHidden>
    <Typography.Paragraph type="secondary">{storeName ?? '当前门店'}的储值与消费单记录；展开单据可查看明细。</Typography.Paragraph>
    <Tabs activeKey={tab} onChange={(key) => setTab(key as MemberHistoryTab)} items={[
      { key: 'topups', label: '储值记录', disabled: !canReadTopups, children: canReadTopups ? <>
        {topups.error && <Alert type="error" showIcon title="储值记录加载失败" action={<Button onClick={() => topups.refetch()}>重试</Button>} />}
        <Table<MemberTopup> size="small" rowKey="id" loading={topups.isFetching} dataSource={topups.data?.items ?? []}
          scroll={{ x: 700 }} locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="还没有储值记录" /> }}
          columns={[
            { title: '储值单号', dataIndex: 'topupNo' },
            { title: '入账时间', dataIndex: 'paidAtUtc', render: time },
            { title: '储值本金', dataIndex: 'principalMinor', align: 'right', render: money },
            { title: '赠送金额', dataIndex: 'bonusMinor', align: 'right', render: money },
            { title: '已退本金', dataIndex: 'refundedPrincipalMinor', align: 'right', render: money },
            { title: '状态', dataIndex: 'status', render: (value: string) => <Tag>{topupStatus[value] ?? value}</Tag> },
          ]}
          expandable={{ expandedRowRender: (item) => <Space orientation="vertical">
            <span>支付单：{item.paymentNo} · 剩余可退本金 {money(item.remainingPrincipalMinor)} · 已收回赠金 {money(item.revokedBonusMinor)}</span>
            <Space wrap>{item.allocations.map((line) => <Tag key={line.id}>{line.methodName} {money(line.amountMinor)}{line.reconciliationStatus === 'Pending' ? ' · 待核对' : ''}</Tag>)}</Space>
            {item.note && <span>备注：{item.note}</span>}
          </Space> }}
          pagination={{ current: topupPage, pageSize, total: topups.data?.total ?? 0, showSizeChanger: false,
            showTotal: (total) => `共 ${total} 笔`, onChange: setTopupPage }} />
      </> : null },
      { key: 'orders', label: '消费记录', disabled: !canReadOrders, children: canReadOrders ? <>
        {orders.error && <Alert type="error" showIcon title="消费记录加载失败" action={<Button onClick={() => orders.refetch()}>重试</Button>} />}
        <Table<ServiceOrder> size="small" rowKey="id" loading={orders.isFetching} dataSource={orders.data?.items ?? []}
          scroll={{ x: 650 }} locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="还没有消费记录" /> }}
          columns={[
            { title: '消费单号', dataIndex: 'orderNo' },
            { title: '录单时间', dataIndex: 'createdAtUtc', render: time },
            { title: '应收金额', dataIndex: 'receivableMinor', align: 'right', render: money },
            { title: '已退金额', dataIndex: 'refundedMinor', align: 'right', render: money },
            { title: '状态', dataIndex: 'status', render: (value: string) => <Tag>{orderStatus[value] ?? value}</Tag> },
          ]}
          expandable={{ expandedRowRender: (item) => <>
            <Table size="small" rowKey="id" pagination={false} dataSource={item.lines} scroll={{ x: 650 }} columns={[
              { title: '项目 / 产品', dataIndex: 'itemName' },
              { title: '数量', dataIndex: 'quantity' },
              { title: '成交单价', dataIndex: 'enteredPriceMinor', align: 'right', render: money },
              { title: '小计', dataIndex: 'lineAmountMinor', align: 'right', render: money },
              { title: '员工', dataIndex: 'employeeName', render: (value?: string) => value ?? '—' },
              { title: '已退数量', dataIndex: 'returnedQuantity' },
            ]} />
            {item.note && <Typography.Paragraph>备注：{item.note}</Typography.Paragraph>}
          </> }}
          pagination={{ current: orderPage, pageSize, total: orders.data?.total ?? 0, showSizeChanger: false,
            showTotal: (total) => `共 ${total} 笔`, onChange: setOrderPage }} />
      </> : null },
    ]} />
  </Modal>
}
