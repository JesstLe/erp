import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Alert, Button, Card, Col, Descriptions, Drawer, Empty, Input, Row, Select, Space, Statistic, Table, Tag, Typography } from "antd";
import { apiRequest } from "../api/client";
import type { EmployeeCommission, EmployeeCommissionLine, EmployeeCommissionReport } from "../api/types";
import { useAuth } from "../auth/useAuth";
import { useDebouncedValue } from "../hooks/useDebouncedValue";

const money = (value: number) => `¥${(value / 100).toFixed(2)}`;
const sources: Record<string, string> = { PositionService: "岗位项目单独比例", PositionDefault: "岗位默认比例", ServiceItem: "服务项目规则" };
const rule = (item: EmployeeCommissionLine) => item.commissionMode === "Percentage" ? `${(item.rateBasisPoints ?? 0) / 100}%`
  : item.commissionMode === "FixedAmount" ? `${money(item.fixedMinor ?? 0)}/次` : "不计提";

export function EmployeeCommissionsPage({ classic = false }: { classic?: boolean }) {
  const auth = useAuth();
  const [storeId, setStoreId] = useState("all");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [queryText, setQueryText] = useState("");
  const query = useDebouncedValue(queryText.trim());
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selected, setSelected] = useState<EmployeeCommissionLine>();
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (storeId !== "all") params.set("storeId", storeId);
  if (fromDate) params.set("fromDate", fromDate);
  if (toDate) params.set("toDate", toDate);
  if (query) params.set("query", query);
  const report = useQuery({ queryKey: ["employee-commission-report", auth.user?.tenantId, storeId, fromDate, toDate, query, page, pageSize],
    enabled: Boolean(auth.user), queryFn: ({ signal }) => apiRequest<EmployeeCommissionReport>(`/api/v1/reports/employee-commissions?${params}`, { signal }) });
  const totals = report.data?.totals;
  const showEmployee = (employee: EmployeeCommission) => { setQueryText(employee.employeeNo); setPage(1); };
  return <div className={classic ? "classic-feature-panel" : "page-stack"}>
    <Space orientation="vertical" size={16} style={{ width: "100%" }}>
      <div className="page-heading"><div><Typography.Title level={2}>员工提成明细</Typography.Title>
        <Typography.Text type="secondary">每笔服务提成、退款冲减与员工汇总集中查看；不是工资发放记录。</Typography.Text></div></div>
      <Card>
        <Space wrap>
          <Select aria-label="提成门店范围" value={storeId} style={{ minWidth: 190 }}
            options={[{ value: "all", label: "全部授权门店" }, ...(auth.user?.stores ?? []).map(x => ({ value: x.id, label: `${x.name} · ${x.code}` }))]}
            onChange={value => { setStoreId(value); setPage(1); }} />
          <Input aria-label="提成开始日期" type="date" value={fromDate} style={{ width: 155 }} onChange={event => { setFromDate(event.target.value); setPage(1); }} />
          <span>至</span>
          <Input aria-label="提成结束日期" type="date" value={toDate} style={{ width: 155 }} onChange={event => { setToDate(event.target.value); setPage(1); }} />
          <Input aria-label="实时查询员工提成" placeholder="员工姓名/工号、单号或服务项目" value={queryText} allowClear maxLength={100}
            style={{ width: 300 }} onChange={event => { setQueryText(event.target.value); setPage(1); }} />
          <Button onClick={() => void report.refetch()} loading={report.isFetching}>刷新</Button>
          <Button onClick={() => { setStoreId("all"); setFromDate(""); setToDate(""); setQueryText(""); setPage(1); }}>重置筛选</Button>
        </Space>
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0, marginTop: 12 }}>
          日期留空为全部历史；按结算日期查询，统计时区：{report.data?.timeZoneId ?? "加载中"}。汇总覆盖筛选后的全部明细，不只是当前页。
        </Typography.Paragraph>
      </Card>
      {report.isError && <Alert type="error" showIcon title={report.error.message || "提成数据加载失败"} />}
      <Row gutter={[16, 16]}>
        {[
          ["服务成交金额", totals?.serviceRevenueMinor ?? 0], ["提成毛额", totals?.grossCommissionMinor ?? 0],
          ["退款冲减", totals?.refundDeductionMinor ?? 0], ["净提成合计", totals?.netCommissionMinor ?? 0],
        ].map(([label, value]) => <Col xs={24} sm={12} xl={6} key={label}><Card loading={report.isLoading}>
          <Statistic title={label} value={Number(value) / 100} prefix="¥" precision={2} />
        </Card></Col>)}
      </Row>
      <Alert type="info" showIcon title={`共 ${totals?.orderCount ?? 0} 张结算单、${totals?.lineCount ?? 0} 条服务明细、${totals?.serviceQuantity ?? 0} 次服务。退款冲减为这些订单截至查询时的累计已完成退款，按整单比例分摊到每条提成；未结算单和产品不纳入。`} />
      <Card title="按员工汇总">
        <Table<EmployeeCommission> rowKey="employeeId" size="small" loading={report.isFetching}
          dataSource={report.data?.employees} pagination={{ pageSize: 10, showSizeChanger: false }} scroll={{ x: 1060 }}
          columns={[
            { title: "工号", dataIndex: "employeeNo", width: 140 }, { title: "员工当前姓名", dataIndex: "employeeName" },
            { title: "结算单数", dataIndex: "orderCount" }, { title: "服务次数", dataIndex: "serviceQuantity" },
            { title: "服务成交", dataIndex: "grossServiceRevenueMinor", render: money },
            { title: "提成毛额", dataIndex: "grossCommissionMinor", render: money },
            { title: "退款冲减", dataIndex: "refundDeductionMinor", render: money },
            { title: "净提成", dataIndex: "netCommissionMinor", render: value => <strong>{money(value)}</strong> },
            { title: "操作", render: (_, employee) => <Button size="small" onClick={() => showEmployee(employee)}>查看该员工明细</Button> },
          ]} />
      </Card>
      <Card title="每笔提成记录" extra={<Typography.Text type="secondary">点击“详情”查看完整金额与规则</Typography.Text>}>
        <Table<EmployeeCommissionLine> rowKey="lineId" size="small" loading={report.isFetching} dataSource={report.data?.items}
          scroll={{ x: 1900 }} locale={{ emptyText: <Empty description="暂无符合条件的已结算服务提成" /> }}
          pagination={{ current: page, pageSize, total: report.data?.total ?? 0, showSizeChanger: true, pageSizeOptions: [20, 50, 100],
            showTotal: total => `共 ${total} 条`, onChange: (next, size) => { setPage(size !== pageSize ? 1 : next); setPageSize(size); } }}
          columns={[
            { title: "结算时间", width: 180, render: (_, item) => new Date(item.paidAtUtc).toLocaleString("zh-CN", { hour12: false, timeZone: report.data?.timeZoneId }) },
            { title: "门店", width: 160, render: (_, item) => `${item.storeName} · ${item.storeCode}` },
            { title: "消费单号", dataIndex: "orderNo", width: 205 },
            { title: "服务员工", width: 150, render: (_, item) => <>{item.employeeName}<br /><Typography.Text type="secondary">{item.employeeNo}</Typography.Text></> },
            { title: "当时岗位", width: 130, render: (_, item) => item.positionName ?? "历史记录未保存" },
            { title: "服务项目", dataIndex: "itemName", width: 160 }, { title: "数量", dataIndex: "quantity", width: 65 },
            { title: "成交金额", dataIndex: "lineAmountMinor", render: money, width: 110 },
            { title: "提成规则", render: (_, item) => rule(item), width: 110 },
            { title: "规则来源", render: (_, item) => sources[item.ruleSource ?? ""] ?? "历史记录未保存", width: 150 },
            { title: "提成毛额", dataIndex: "grossCommissionMinor", render: money, width: 110 },
            { title: "退款冲减", dataIndex: "refundDeductionMinor", render: money, width: 110 },
            { title: "净提成", dataIndex: "netCommissionMinor", render: value => <Tag color="green">{money(value)}</Tag>, width: 110 },
            { title: "操作", fixed: "right", width: 75, render: (_, item) => <Button size="small" onClick={() => setSelected(item)}>详情</Button> },
          ]} />
      </Card>
    </Space>
    <Drawer open={Boolean(selected)} title="提成记录详情" size={650} onClose={() => setSelected(undefined)}>
      {selected && <Descriptions bordered column={1} items={[
        { key: "order", label: "消费单号", children: selected.orderNo },
        { key: "store", label: "门店", children: `${selected.storeName} · ${selected.storeCode}` },
        { key: "time", label: "结算时间", children: new Date(selected.paidAtUtc).toLocaleString("zh-CN", { hour12: false, timeZone: report.data?.timeZoneId }) },
        { key: "employee", label: "当时服务员工", children: `${selected.employeeName} · ${selected.employeeNo}` },
        { key: "position", label: "当时岗位", children: selected.positionName ? `${selected.positionName} · ${selected.positionCode}` : "历史记录未保存岗位快照" },
        { key: "service", label: "服务项目", children: `${selected.itemName} · ${selected.itemCode}` },
        { key: "quantity", label: "数量", children: selected.quantity },
        { key: "unit", label: "本次成交单价", children: money(selected.unitPriceMinor) },
        { key: "amount", label: "服务成交金额 / 提成基数", children: money(selected.lineAmountMinor) },
        { key: "rule", label: "提成规则", children: rule(selected) },
        { key: "source", label: "规则来源", children: sources[selected.ruleSource ?? ""] ?? "历史记录未保存来源快照" },
        { key: "gross", label: "提成毛额", children: money(selected.grossCommissionMinor) },
        { key: "order-refund", label: "该消费单累计退款", children: money(selected.orderRefundedMinor) },
        { key: "refund", label: "该条提成退款冲减", children: money(selected.refundDeductionMinor) },
        { key: "net", label: "净提成", children: money(selected.netCommissionMinor) },
      ]} />}
    </Drawer>
  </div>;
}
