import { SearchOutlined } from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Drawer,
  Empty,
  Form,
  Input,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from "antd";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { apiRequest } from "../api/client";
import type { AuditEvent, AuditEventPage } from "../api/types";
import { useAuth } from "../auth/useAuth";

interface AuditFilters {
  action?: string;
  entityType?: string;
  fromDate?: string;
  toDate?: string;
}
const actionLabels: Record<string, string> = {
  "facility.session.start": "开始设施使用",
  "facility.session.pause": "暂停设施计时",
  "facility.session.resume": "继续设施计时",
  "facility.session.switch": "更换设施",
  "facility.session.end": "结束设施使用",
  "facility.cleaning.complete": "完成设施清洁",
  "customer.create": "新建顾客",
  "membership.open": "开通会员",
  "membership.card.open": "开通会员",
  "member_card_type.create": "发布会员卡类",
  "service_order.create": "创建消费单",
  "service_order.confirm": "确认消费单金额",
  "service_order.settle": "结算消费单",
  "payment.complete": "完成支付记录",
  "cashier_shift.open": "开始收银班次",
  "cashier_shift.submit": "提交交班",
  "cashier_shift.review": "复核交班",
  "visit.customer.link": "关联接待顾客",
  "employee.create": "新建员工",
  "employee.account.enable": "启用员工账号",
  "employee.account.disable": "停用员工账号",
  "catalog.service_item.create": "新建服务项目",
  "catalog.product_item.create": "新建产品",
  "catalog.price_book.create": "新建价格版本",
  "catalog.price_book.publish": "发布价格版本",
  "customer.mobile.reveal": "查看完整手机号",
  "customer.export": "导出顾客名单",
};
const stateColor: Record<string, string> = {
  Draft: "gold",
  PendingPayment: "blue",
  Settled: "green",
  Open: "green",
  ReviewPending: "gold",
  Closed: "default",
};

export function AuditPage() {
  const auth = useAuth();
  const storeId = auth.store?.id;
  const [form] = Form.useForm<AuditFilters>();
  const [filters, setFilters] = useState<AuditFilters>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selected, setSelected] = useState<AuditEvent>();
  const params = new URLSearchParams({
    storeId: storeId ?? "",
    page: String(page),
    pageSize: String(pageSize),
  });
  if (filters.action) params.set("action", filters.action);
  if (filters.entityType) params.set("entityType", filters.entityType);
  if (filters.fromDate) params.set("fromDate", filters.fromDate);
  if (filters.toDate) params.set("toDate", filters.toDate);
  const events = useQuery({
    queryKey: ["audit-events", storeId, filters, page, pageSize],
    enabled: Boolean(storeId),
    queryFn: () => apiRequest<AuditEventPage>(`/api/v1/audit/events?${params}`),
  });
  const columns = [
    {
      title: "时间",
      dataIndex: "occurredAtUtc",
      width: 180,
      render: (value: string) =>
        new Date(value).toLocaleString("zh-CN", { hour12: false }),
    },
    {
      title: "业务动作",
      dataIndex: "action",
      render: (value: string) => (
        <div className="audit-action">
          <strong>{actionLabels[value] ?? value}</strong>
          <Typography.Text type="secondary">{value}</Typography.Text>
        </div>
      ),
    },
    { title: "业务对象", dataIndex: "entityType", width: 160 },
    {
      title: "状态变化",
      key: "state",
      width: 220,
      render: (_: unknown, record: AuditEvent) => (
        <Space>
          {record.previousState ? (
            <Tag color={stateColor[record.previousState]}>
              {record.previousState}
            </Tag>
          ) : (
            <Tag>新建</Tag>
          )}
          <span>→</span>
          <Tag color={stateColor[record.currentState ?? ""]}>
            {record.currentState ?? "—"}
          </Tag>
        </Space>
      ),
    },
    { title: "操作者", dataIndex: "operatorDisplayName", width: 130 },
    {
      title: "操作",
      key: "action",
      width: 90,
      render: (_: unknown, record: AuditEvent) => (
        <Button
          size="small"
          onClick={(event) => {
            event.stopPropagation();
            setSelected(record);
          }}
        >
          查看
        </Button>
      ),
    },
  ];
  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <Typography.Title level={2}>审计记录</Typography.Title>
          <Typography.Paragraph>
            按门店只读查看关键业务状态变化；审计记录不提供编辑和删除。
          </Typography.Paragraph>
        </div>
      </div>
      <Alert
        type="info"
        showIcon
        title="审计用于追溯谁在何时执行了什么动作，不代表渠道对账或财务审核已经完成。"
      />
      <Card variant="borderless">
        <Form<AuditFilters>
          form={form}
          layout="inline"
          onFinish={(values) => {
            setFilters(values);
            setPage(1);
          }}
        >
          <Form.Item name="action" label="动作">
            <Input
              allowClear
              placeholder="例如 payment 或 customer.export"
              maxLength={128}
            />
          </Form.Item>
          <Form.Item name="entityType" label="对象">
            <Select
              allowClear
              placeholder="全部对象"
              className="audit-entity-select"
              options={[
                "FacilitySession",
                "Customer",
                "CustomerExport",
                "MemberCard",
                "ServiceOrder",
                "Payment",
                "CashierShift",
                "Employee",
                "ServiceItem",
                "ProductItem",
                "PriceBook",
              ].map((value) => ({ value, label: value }))}
            />
          </Form.Item>
          <Form.Item name="fromDate" label="开始日期">
            <Input type="date" />
          </Form.Item>
          <Form.Item name="toDate" label="结束日期">
            <Input type="date" />
          </Form.Item>
          <Button type="primary" htmlType="submit" icon={<SearchOutlined />}>
            查询
          </Button>
        </Form>
      </Card>
      <Card variant="borderless" className="table-card">
        <Table<AuditEvent>
          rowKey="id"
          columns={columns}
          dataSource={events.data?.items}
          loading={events.isLoading}
          pagination={{
            current: page,
            pageSize,
            total: events.data?.total,
            showSizeChanger: true,
            showTotal: (total) => `共 ${total} 条`,
            onChange: (next, size) => {
              setPage(next);
              setPageSize(size);
            },
          }}
          locale={{ emptyText: <Empty description="没有匹配的审计记录" /> }}
          onRow={(record) => ({
            onClick: () => setSelected(record),
            className: "clickable-row",
          })}
        />
      </Card>
      <Drawer
        title="审计详情"
      size={620}
        open={Boolean(selected)}
        onClose={() => setSelected(undefined)}
      >
        {selected && (
          <Space orientation="vertical" size={18} className="full-width">
            <Descriptions
              column={1}
              bordered
              size="small"
              items={[
                {
                  key: "time",
                  label: "服务器时间",
                  children: new Date(selected.occurredAtUtc).toLocaleString(
                    "zh-CN",
                    { hour12: false },
                  ),
                },
                {
                  key: "action",
                  label: "动作",
                  children: (
                    <>
                      <strong>
                        {actionLabels[selected.action] ?? selected.action}
                      </strong>
                      <br />
                      <Typography.Text type="secondary">
                        {selected.action}
                      </Typography.Text>
                    </>
                  ),
                },
                {
                  key: "operator",
                  label: "操作者",
                  children: selected.operatorDisplayName,
                },
                {
                  key: "entity",
                  label: "业务对象",
                  children: `${selected.entityType}${selected.entityId ? ` · ${selected.entityId}` : ""}`,
                },
                {
                  key: "state",
                  label: "状态变化",
                  children: `${selected.previousState ?? "新建"} → ${selected.currentState ?? "—"}`,
                },
                {
                  key: "reason",
                  label: "原因/备注",
                  children: selected.reason ?? "未填写",
                },
                {
                  key: "request",
                  label: "请求号",
                  children: selected.requestId ?? "无",
                },
                { key: "trace", label: "追踪号", children: selected.traceId },
              ]}
            />
            <Alert
              type="warning"
              showIcon
              title="请求号用于识别幂等业务命令，追踪号用于服务日志定位；两者不能作为支付到账证明。"
            />
          </Space>
        )}
      </Drawer>
    </div>
  );
}
