import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Alert, Button, Card, Col, Descriptions, Drawer, Empty, Input, Row, Select, Space, Statistic, Table, Tag, Typography } from "antd";
import { apiRequest } from "../api/client";
import type { Employee, EmployeeCommission, EmployeeCommissionLine, EmployeeCommissionReport } from "../api/types";
import { useAuth } from "../auth/useAuth";
import { useDebouncedValue } from "../hooks/useDebouncedValue";
import { commissionDateRange } from "../components/employeeCommissionDates";

const money = (value: number) => `¥${(value / 100).toFixed(2)}`;
const sources: Record<string, string> = { PositionService: "岗位项目单独比例", PositionDefault: "岗位默认比例", ServiceItem: "服务项目规则" };
const pricingSources: Record<string, string> = { ListPrice: "目录价", MemberDiscount: "会员优惠价", ManualOverride: "人工改价" };
const rule = (item: EmployeeCommissionLine) => item.commissionMode === "Percentage" ? `${(item.rateBasisPoints ?? 0) / 100}%`
  : item.commissionMode === "FixedAmount" ? `${money(item.fixedMinor ?? 0)}/次` : "不计提";

export function EmployeeCommissionsPage({ classic = false, employee, initialDates }: {
  classic?: boolean; employee?: Pick<Employee, "id" | "employeeNo" | "displayName">;
  initialDates?: { fromDate: string; toDate: string };
}) {
  const auth = useAuth();
  const [storeId, setStoreId] = useState("all");
  const [fromDate, setFromDate] = useState(initialDates?.fromDate ?? "");
  const [toDate, setToDate] = useState(initialDates?.toDate ?? "");
  const [focusedEmployee, setFocusedEmployee] = useState<Pick<Employee, "id" | "employeeNo" | "displayName">>();
  const employeeFilter = employee ?? focusedEmployee;
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
  if (employeeFilter) params.set("employeeId", employeeFilter.id);
  const invalidDates = Boolean(fromDate && toDate && fromDate > toDate);
  const report = useQuery({ queryKey: ["employee-commission-report", auth.user?.tenantId, employeeFilter?.id, storeId, fromDate, toDate, query, page, pageSize],
    enabled: Boolean(auth.user) && !invalidDates, throwOnError: false,
    queryFn: ({ signal }) => apiRequest<EmployeeCommissionReport>(`/api/v1/reports/employee-commissions?${params}`, { signal }) });
  const data = report.isError || invalidDates ? undefined : report.data;
  const totals = data?.totals;
  const showEmployee = (item: EmployeeCommission) => { setFocusedEmployee({ id: item.employeeId, employeeNo: item.employeeNo, displayName: item.employeeName }); setQueryText(""); setPage(1); };
  const selectRange = (days?: number) => {
    const range = days ? commissionDateRange(days, data?.timeZoneId) : { fromDate: "", toDate: "" };
    setFromDate(range.fromDate); setToDate(range.toDate); setPage(1);
  };
  return <div className={classic ? "classic-feature-panel" : "page-stack"}>
    <Space orientation="vertical" size={16} style={{ width: "100%" }}>
      <div className="page-heading"><div><Typography.Title level={employee ? 4 : 2}>{employee ? "已结算服务记录与提成" : "员工提成明细"}</Typography.Title>
        <Typography.Text type="secondary">查看服务记录、提成明细与退款调整。</Typography.Text>
        {employeeFilter && <div><Tag closable={!employee} onClose={() => { setFocusedEmployee(undefined); setPage(1); }}>{employeeFilter.displayName} · {employeeFilter.employeeNo}</Tag></div>}
      </div></div>
      <Card>
        <Space wrap>
          <Select aria-label="提成门店范围" value={storeId} style={{ minWidth: 190 }}
            options={[{ value: "all", label: "全部授权门店" }, ...(auth.user?.stores ?? []).map(x => ({ value: x.id, label: `${x.name} · ${x.code}` }))]}
            onChange={value => { setStoreId(value); setPage(1); }} />
          <Input aria-label="提成开始日期" type="date" value={fromDate} style={{ width: 155 }} onChange={event => { setFromDate(event.target.value); setPage(1); }} />
          <span>至</span>
          <Input aria-label="提成结束日期" type="date" value={toDate} style={{ width: 155 }} onChange={event => { setToDate(event.target.value); setPage(1); }} />
          <Input aria-label="实时查询员工提成" placeholder={employeeFilter ? "单号或服务项目" : "员工姓名/工号、单号或服务项目"} value={queryText} allowClear maxLength={100}
            style={{ width: 300 }} onChange={event => { setQueryText(event.target.value); setPage(1); }} />
          <Button onClick={() => void report.refetch()} loading={report.isFetching} disabled={invalidDates}>刷新</Button>
          <Button onClick={() => { setStoreId("all"); selectRange(employee ? 1 : undefined); setQueryText(""); setFocusedEmployee(undefined); }}>重置筛选</Button>
        </Space>
        <Space wrap style={{ marginTop: 12 }}>
          <Button size="small" onClick={() => selectRange(1)}>当天</Button>
          <Button size="small" onClick={() => selectRange(7)}>近7天</Button>
          <Button size="small" onClick={() => selectRange(30)}>近30天</Button>
          <Button size="small" onClick={() => selectRange()}>全部历史</Button>
        </Space>
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0, marginTop: 12 }}>
          日期留空为全部历史；按结算日期查询，统计时区：{data?.timeZoneId ?? "加载中"}。汇总覆盖筛选后的全部明细，不只是当前页。
        </Typography.Paragraph>
      </Card>
      {report.isError && <Alert type="error" showIcon title={report.error.message || "提成数据加载失败"} />}
      {invalidDates && <Alert type="warning" showIcon title="开始日期不得晚于结束日期，请调整后查询。" />}
      <Row gutter={[16, 16]}>
        {[
          { label: "服务成交金额", value: totals?.serviceRevenueMinor }, { label: "提成毛额", value: totals?.grossCommissionMinor },
          { label: "退款冲减", value: totals?.refundDeductionMinor }, { label: "净提成合计", value: totals?.netCommissionMinor },
        ].map(({ label, value }) => <Col xs={24} sm={12} xl={6} key={label}><Card loading={report.isLoading}>
          <Statistic title={label} value={value == null ? "—" : value / 100} prefix={value == null ? undefined : "¥"} precision={2} />
        </Card></Col>)}
      </Row>
      <Alert type="info" showIcon title={`${totals ? `共 ${totals.orderCount} 张结算单、${totals.lineCount} 条服务明细、${totals.serviceQuantity} 次服务。` : "统计结果尚未获取。"}退款冲减为这些订单截至查询时的累计已完成退款，按整单比例分摊到每条提成；未结算单和产品不纳入。`} />
      {!employee && <Card title="按员工汇总">
        <Table<EmployeeCommission> rowKey="employeeId" size="small" loading={report.isFetching}
          dataSource={data?.employees} pagination={{ pageSize: 10, showSizeChanger: false }} scroll={{ x: 1060 }}
          columns={[
            { title: "工号", dataIndex: "employeeNo", width: 140 }, { title: "员工当前姓名", dataIndex: "employeeName" },
            { title: "结算单数", dataIndex: "orderCount" }, { title: "服务次数", dataIndex: "serviceQuantity" },
            { title: "服务成交", dataIndex: "grossServiceRevenueMinor", render: money },
            { title: "提成毛额", dataIndex: "grossCommissionMinor", render: money },
            { title: "退款冲减", dataIndex: "refundDeductionMinor", render: money },
            { title: "净提成", dataIndex: "netCommissionMinor", render: value => <strong>{money(value)}</strong> },
            { title: "操作", render: (_, employee) => <Button size="small" onClick={() => showEmployee(employee)}>查看该员工明细</Button> },
          ]} />
      </Card>}
      <Card title={employee ? "每笔服务与提成记录" : "每笔提成记录"} extra={<Typography.Text type="secondary">点击“详情”查看完整金额与规则</Typography.Text>}>
        <Table<EmployeeCommissionLine> rowKey="lineId" size="small" loading={report.isFetching} dataSource={data?.items}
          scroll={{ x: 2110 }} locale={{ emptyText: <Empty description={report.isError || invalidDates ? "数据未加载成功，请检查筛选或重试" : "暂无符合条件的已结算服务提成"} /> }}
          pagination={{ current: page, pageSize, total: data?.total ?? 0, showSizeChanger: true, pageSizeOptions: [20, 50, 100],
            showTotal: total => `共 ${total} 条`, onChange: (next, size) => { setPage(size !== pageSize ? 1 : next); setPageSize(size); } }}
          columns={[
            { title: "结算时间", width: 180, render: (_, item) => new Date(item.paidAtUtc).toLocaleString("zh-CN", { hour12: false, timeZone: data?.timeZoneId }) },
            { title: "门店", width: 160, render: (_, item) => `${item.storeName} · ${item.storeCode}` },
            { title: "消费单号", dataIndex: "orderNo", width: 205 },
            { title: "服务员工", width: 150, render: (_, item) => <>{item.employeeName}<br /><Typography.Text type="secondary">{item.employeeNo}</Typography.Text></> },
            { title: "当时岗位", width: 130, render: (_, item) => item.positionName ?? "历史记录未保存" },
            { title: "服务项目", dataIndex: "itemName", width: 160 }, { title: "数量", dataIndex: "quantity", width: 65 },
            { title: "录入时长", width: 100, render: (_, item) => item.actualSeconds == null ? "未记录" : `${item.actualSeconds / 60} 分钟` },
            { title: "成交单价", dataIndex: "unitPriceMinor", render: money, width: 110 },
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
        { key: "duration", label: "录入服务时长", children: selected.actualSeconds == null ? "未记录（不是设施计时）" : `${selected.actualSeconds / 60} 分钟（不是设施计时）` },
        { key: "reference", label: "当时目录单价", children: money(selected.referencePriceMinor) },
        { key: "unit", label: "本次成交单价", children: money(selected.unitPriceMinor) },
        { key: "pricing", label: "价格来源", children: pricingSources[selected.pricingSource] ?? selected.pricingSource },
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
