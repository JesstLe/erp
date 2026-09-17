import {
  CheckCircleOutlined,
  CreditCardOutlined,
  DollarOutlined,
  DownloadOutlined,
  EditOutlined,
  EyeOutlined,
  LoadingOutlined,
  PlusOutlined,
  SearchOutlined,
  SettingOutlined,
  StopOutlined,
  TeamOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Descriptions,
  Drawer,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Pagination,
  Select,
  Space,
  Table,
  Tabs,
  Tag,
  Typography,
  message,
} from "antd";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { apiDownload, apiRequest, ApiError } from "../api/client";
import type {
  CashierShift,
  CustomerDetail,
  CustomerMergePreview,
  CustomerSummary,
  MemberCard,
  MemberCardType,
  MemberTopup,
  PageResult,
  PaymentMethod,
  ProductItem,
  Refund,
  ServiceItem,
} from "../api/types";
import { useAuth } from "../auth/useAuth";
import { ServiceRecordsSection } from "./ServiceRecordsSection";
import { MembershipBenefitsSection } from "./MembershipBenefitsSection";
import { MemberTopupModal } from "./MemberTopupModal";
import { buildRemainingRefundLines } from "./membershipRules";
import { useDebouncedValue } from "../hooks/useDebouncedValue";
import { Permission } from "../security/permissions";
import { useAuthorization } from "../security/useAuthorization";

function commandId() {
  return crypto.randomUUID();
}
function formatAccount(type: string, units: number) {
  return type === "Points" ? `${units} 积分` : `¥${(units / 100).toFixed(2)}`;
}
const accountLabels: Record<string, string> = {
  Principal: "储值本金",
  Bonus: "奖励金",
  Points: "积分",
};
const genderLabels: Record<string, string> = {
  Unknown: "未填写",
  Female: "女",
  Male: "男",
  Other: "其他",
};
interface TopupRefundValues {
  amountYuan: number;
  reason: string;
}
interface SensitivePurposeValues {
  purpose: string;
  includeFullMobile?: boolean;
}
interface EditCustomerValues {
  name: string;
  mobile: string;
  gender: string;
  birthDate?: string;
  residence?: string;
  sourceCode?: string;
  serviceNotificationConsent: boolean;
  marketingConsent: boolean;
}
interface CustomerStatusValues {
  reason: string;
}
interface MergeCustomerValues {
  targetCustomerId: string;
  reason: string;
}
interface CustomerMobileReveal {
  customerId: string;
  mobile: string;
  revealedAtUtc: string;
}

export function CustomersPage() {
  const auth = useAuth();
  const { can } = useAuthorization();
  const storeId = auth.store?.id;
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedCustomerId = searchParams.get("customerId") ?? undefined;
  const requestedSection = searchParams.get("section");
  const [query, setQuery] = useState("");
  const submittedQuery = useDebouncedValue(query.trim());
  const [selectedId, setSelectedId] = useState<string>();
  const [detailSection, setDetailSection] = useState<"topups" | "care">("topups");
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const [topupPage, setTopupPage] = useState(1);
  const topupPageSize = 5;
  const [createOpen, setCreateOpen] = useState(false);
  const [membershipOpen, setMembershipOpen] = useState(false);
  const [cardTypeOpen, setCardTypeOpen] = useState(false);
  const [editingCardType, setEditingCardType] = useState<MemberCardType>();
  const [serviceItemDiscounts, setServiceItemDiscounts] = useState<Record<string, number | undefined>>({});
  const [productItemDiscounts, setProductItemDiscounts] = useState<Record<string, number | undefined>>({});
  const [serviceBulkDiscount, setServiceBulkDiscount] = useState<number>(10);
  const [productBulkDiscount, setProductBulkDiscount] = useState<number>(10);
  const [topupCard, setTopupCard] = useState<MemberCard>();
  const [refundTopup, setRefundTopup] = useState<MemberTopup>();
  const [revealOpen, setRevealOpen] = useState(false);
  const [exportOpen, setExportOpen] = useState(false);
  const [revealedMobile, setRevealedMobile] = useState<CustomerMobileReveal>();
  const [editOpen, setEditOpen] = useState(false);
  const [statusAction, setStatusAction] = useState<"disable" | "restore">();
  const [mergeOpen, setMergeOpen] = useState(false);
  const [mergeTargetQuery, setMergeTargetQuery] = useState("");
  const mergeTargetTerm = useDebouncedValue(mergeTargetQuery.trim());
  const [mergePreview, setMergePreview] = useState<CustomerMergePreview>();
  const [createForm] = Form.useForm();
  const [editForm] = Form.useForm<EditCustomerValues>();
  const [statusForm] = Form.useForm<CustomerStatusValues>();
  const [mergeForm] = Form.useForm<MergeCustomerValues>();
  const [membershipForm] = Form.useForm();
  const [cardTypeForm] = Form.useForm();
  const [topupRefundForm] = Form.useForm<TopupRefundValues>();
  const [revealForm] = Form.useForm<SensitivePurposeValues>();
  const [exportForm] = Form.useForm<SensitivePurposeValues>();
  const canOpenMembership = can(Permission.MembershipOpen);
  const canTopup = can(Permission.MembershipTopup);
  const canGrantBonus = can(Permission.MembershipGrantBonus);
  const canRequestTopupRefund = can(Permission.RefundRequest);
  const canExportCustomers = can(Permission.CustomerExport);
  const canExportFullMobile = can(Permission.CustomerExportFullMobile);
  const canViewFinancialDetails = can(Permission.MembershipManage);
  const canViewServiceRecords = can(Permission.ServiceRecordManage);
  const canManageCustomers = can(Permission.CustomerManage);
  const canMergeCustomers = can(Permission.CustomerMerge);
  const canManageCardTypes = can(Permission.MembershipCardTypeManage);
  const canCreateCustomer = can(Permission.CustomerWrite);
  useEffect(() => setPage(1), [storeId, submittedQuery]);
  useEffect(() => setTopupPage(1), [storeId, selectedId]);
  useEffect(() => {
    if (!requestedCustomerId) return;
    setSelectedId(requestedCustomerId);
    setDetailSection(requestedSection === "care" ? "care" : "topups");
  }, [requestedCustomerId, requestedSection]);
  const customers = useQuery({
    queryKey: ["customers", storeId, submittedQuery, page],
    enabled: Boolean(storeId),
    queryFn: ({ signal }) =>
      apiRequest<PageResult<CustomerSummary>>("/api/v1/customers/search", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          query: submittedQuery,
          page,
          pageSize,
        }),
        signal,
      }),
  });
  const detail = useQuery({
    queryKey: ["customer", storeId, selectedId],
    enabled: Boolean(storeId && selectedId),
    queryFn: () =>
      apiRequest<CustomerDetail>(
        `/api/v1/customers/${selectedId}?storeId=${storeId}`,
      ),
  });
  const cardTypes = useQuery({
    queryKey: ["member-card-types"],
    queryFn: () =>
      apiRequest<MemberCardType[]>("/api/v1/customers/membership/card-types"),
  });
  const discountServiceItems = useQuery({
    queryKey: ["service-items", "card-discounts"],
    enabled: cardTypeOpen,
    queryFn: () => apiRequest<ServiceItem[]>("/api/v1/catalog/service-items"),
  });
  const discountProductItems = useQuery({
    queryKey: ["product-items", "card-discounts"],
    enabled: cardTypeOpen,
    queryFn: () => apiRequest<ProductItem[]>("/api/v1/catalog/products"),
  });
  const paymentMethods = useQuery({
    queryKey: ["payment-methods", storeId],
    enabled: Boolean(storeId && canTopup),
    queryFn: () =>
      apiRequest<PaymentMethod[]>(
        `/api/v1/payments/methods?storeId=${storeId}`,
      ),
  });
  const currentShift = useQuery({
    queryKey: ["cashier-shift", storeId],
    enabled: Boolean(storeId && canTopup),
    queryFn: () =>
      apiRequest<CashierShift | undefined>(
        `/api/v1/payments/shifts/current?storeId=${storeId}`,
      ),
  });
  const topups = useQuery({
    queryKey: ["member-topups", storeId, selectedId, topupPage],
    enabled: Boolean(storeId && selectedId && canViewFinancialDetails),
    queryFn: () =>
      apiRequest<PageResult<MemberTopup>>(
        `/api/v1/member-topups?storeId=${storeId}&customerId=${selectedId}&page=${topupPage}&pageSize=${topupPageSize}`,
      ),
  });
  const mergeTargets = useQuery({
    queryKey: ["customer-merge-targets", storeId, mergeTargetTerm],
    enabled: Boolean(
      storeId && mergeOpen && canMergeCustomers,
    ),
    queryFn: ({ signal }) =>
      apiRequest<PageResult<CustomerSummary>>("/api/v1/customers/search", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          query: mergeTargetTerm,
          page: 1,
          pageSize: 20,
        }),
        signal,
      }),
  });
  const onError = (error: unknown) =>
    message.error(error instanceof ApiError ? error.message : "操作失败");
  const createCustomer = useMutation({
    mutationFn: (values: Record<string, unknown>) =>
      apiRequest<CustomerDetail>("/api/v1/customers", {
        method: "POST",
        body: JSON.stringify({ ...values, storeId, commandId: commandId() }),
      }),
    onSuccess: async (result) => {
      message.success("顾客档案已创建");
      setCreateOpen(false);
      createForm.resetFields();
      setSelectedId(result.id);
      await queryClient.invalidateQueries({ queryKey: ["customers", storeId] });
    },
    onError,
  });
  const openMembership = useMutation({
    mutationFn: (values: Record<string, unknown>) =>
      apiRequest<CustomerDetail>(`/api/v1/customers/${selectedId}/membership`, {
        method: "POST",
        body: JSON.stringify({ ...values, storeId, commandId: commandId() }),
      }),
    onSuccess: async () => {
      message.success("会员已开通，账户初始余额为 0");
      setMembershipOpen(false);
      membershipForm.resetFields();
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ["customer", storeId, selectedId],
        }),
        queryClient.invalidateQueries({ queryKey: ["customers", storeId] }),
      ]);
    },
    onError,
  });
  const saveCardType = useMutation({
    mutationFn: (values: { name: string; validityDays?: number; serviceDiscount: number; productDiscount: number }) =>
      apiRequest<MemberCardType>(editingCardType
        ? `/api/v1/customers/membership/card-types/${editingCardType.id}`
        : "/api/v1/customers/membership/card-types", {
        method: editingCardType ? "PUT" : "POST",
        body: JSON.stringify({ name: values.name, validityDays: values.validityDays,
          serviceDiscountBasisPoints: Math.round(values.serviceDiscount * 1000),
          productDiscountBasisPoints: Math.round(values.productDiscount * 1000),
          serviceItemDiscounts: Object.entries(serviceItemDiscounts).filter((entry): entry is [string, number] => entry[1] !== undefined).map(([catalogItemId, discount]) => ({ catalogItemId, discountBasisPoints: Math.round(discount * 1000) })),
          productItemDiscounts: Object.entries(productItemDiscounts).filter((entry): entry is [string, number] => entry[1] !== undefined).map(([catalogItemId, discount]) => ({ catalogItemId, discountBasisPoints: Math.round(discount * 1000) })),
          expectedVersion: editingCardType?.version, commandId: commandId() }),
      }),
    onSuccess: async (cardType) => {
      message.success(editingCardType ? "会员折扣已更新" : `卡类已发布，系统编号 ${cardType.code}`);
      setCardTypeOpen(false);
      setEditingCardType(undefined);
      cardTypeForm.resetFields();
      await queryClient.invalidateQueries({ queryKey: ["member-card-types"] });
      await queryClient.invalidateQueries({ queryKey: ["customer-detail"] });
      await queryClient.invalidateQueries({ queryKey: ["customer"] });
    },
    onError,
  });
  const requestTopupRefund = useMutation({
    mutationFn: ({
      topup,
      values,
    }: {
      topup: MemberTopup;
      values: TopupRefundValues;
    }) => {
      const lines = buildRemainingRefundLines(
        topup.allocations,
        topup.paymentRefundedMinor,
        Math.round(values.amountYuan * 100),
      );
      return apiRequest<Refund>("/api/v1/refunds", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          paymentId: topup.paymentId,
          expectedPaymentVersion: topup.paymentVersion,
          reason: values.reason,
          commandId: commandId(),
          lines,
        }),
      });
    },
    onSuccess: async () => {
      message.success("储值退款申请已提交，等待最高权限审批");
      setRefundTopup(undefined);
      topupRefundForm.resetFields();
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ["member-topups", storeId, selectedId],
        }),
        queryClient.invalidateQueries({ queryKey: ["refunds", storeId] }),
      ]);
    },
    onError,
  });
  const revealMobile = useMutation({
    mutationFn: (values: SensitivePurposeValues) =>
      apiRequest<CustomerMobileReveal>(
        `/api/v1/customers/${selectedId}/mobile/reveal`,
        {
          method: "POST",
          body: JSON.stringify({
            storeId,
            purpose: values.purpose,
            commandId: commandId(),
          }),
        },
      ),
    onSuccess: (result) => {
      setRevealedMobile(result);
      setRevealOpen(false);
      revealForm.resetFields();
      message.success("完整手机号已临时显示，本次查看已记录审计");
    },
    onError,
  });
  const exportCustomers = useMutation({
    mutationFn: (values: SensitivePurposeValues) =>
      apiDownload("/api/v1/customers/export", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          query: submittedQuery,
          includeFullMobile: Boolean(values.includeFullMobile),
          purpose: values.purpose,
          commandId: commandId(),
        }),
      }),
    onSuccess: ({ blob, filename }) => {
      const href = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = href;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.setTimeout(() => URL.revokeObjectURL(href), 1_000);
      setExportOpen(false);
      exportForm.resetFields();
      message.success("顾客名单已导出，本次操作已记录审计");
    },
    onError,
  });
  const updateCustomer = useMutation({
    mutationFn: (values: EditCustomerValues) =>
      apiRequest<CustomerDetail>(`/api/v1/customers/${selectedId}`, {
        method: "PUT",
        body: JSON.stringify({
          ...values,
          storeId,
          expectedVersion: detail.data?.version,
          commandId: commandId(),
        }),
      }),
    onSuccess: async (result) => {
      message.success("顾客资料已更新并记录审计");
      setEditOpen(false);
      setRevealedMobile({
        customerId: result.id,
        mobile: editForm.getFieldValue("mobile"),
        revealedAtUtc: new Date().toISOString(),
      });
      await Promise.all([
        queryClient.setQueryData(["customer", storeId, selectedId], result),
        queryClient.invalidateQueries({ queryKey: ["customers", storeId] }),
      ]);
    },
    onError,
  });
  const changeCustomerStatus = useMutation({
    mutationFn: (values: CustomerStatusValues) =>
      apiRequest<CustomerDetail>(`/api/v1/customers/${selectedId}/status`, {
        method: "POST",
        body: JSON.stringify({
          storeId,
          restore: statusAction === "restore",
          reason: values.reason,
          expectedVersion: detail.data?.version,
          commandId: commandId(),
        }),
      }),
    onSuccess: async (result) => {
      message.success(
        result.status === "Active"
          ? "顾客档案已恢复"
          : "顾客档案已停用，历史和余额均保留",
      );
      setStatusAction(undefined);
      statusForm.resetFields();
      await Promise.all([
        queryClient.setQueryData(["customer", storeId, selectedId], result),
        queryClient.invalidateQueries({ queryKey: ["customers", storeId] }),
      ]);
    },
    onError,
  });
  const previewMerge = useMutation({
    mutationFn: (targetCustomerId: string) =>
      apiRequest<CustomerMergePreview>(
        `/api/v1/customers/${selectedId}/merge-preview`,
        { method: "POST", body: JSON.stringify({ storeId, targetCustomerId }) },
      ),
    onSuccess: setMergePreview,
    onError,
  });
  const mergeCustomer = useMutation({
    mutationFn: (values: MergeCustomerValues) =>
      apiRequest<CustomerDetail>(`/api/v1/customers/${selectedId}/merge`, {
        method: "POST",
        body: JSON.stringify({
          storeId,
          targetCustomerId: values.targetCustomerId,
          expectedSourceVersion: mergePreview?.sourceVersion,
          expectedTargetVersion: mergePreview?.targetVersion,
          reason: values.reason,
          commandId: commandId(),
        }),
      }),
    onSuccess: async (result) => {
      message.success(
        "重复顾客已逻辑合并；历史外键和不可变流水保持原样，统一从保留档案查看",
      );
      setMergeOpen(false);
      setMergePreview(undefined);
      setRevealedMobile(undefined);
      setSelectedId(result.id);
      mergeForm.resetFields();
      await Promise.all([
        queryClient.setQueryData(["customer", storeId, result.id], result),
        queryClient.invalidateQueries({ queryKey: ["customers", storeId] }),
        queryClient.invalidateQueries({ queryKey: ["member-topups", storeId] }),
      ]);
    },
    onError,
  });
  const beginTopup = (card: MemberCard) => {
    if (!(paymentMethods.data ?? []).some((method) =>
      method.category !== "InternalAccount" && method.category !== "ChannelExternal"))
      return message.error("没有可用的储值收款方式");
    setTopupCard(card);
  };
  const closeDetail = () => {
    setSelectedId(undefined);
    setRevealedMobile(undefined);
    const next = new URLSearchParams(searchParams);
    next.delete("customerId");
    next.delete("section");
    setSearchParams(next, { replace: true });
  };
  const beginEdit = () => {
    if (!detail.data) return;
    if (revealedMobile?.customerId !== detail.data.id) {
      message.info("编辑手机号前请先按需查看完整号码并留痕");
      setRevealOpen(true);
      return;
    }
    editForm.setFieldsValue({
      name: detail.data.displayName,
      mobile: revealedMobile.mobile,
      gender: detail.data.gender,
      birthDate: detail.data.birthDate,
      residence: detail.data.residence,
      sourceCode: detail.data.sourceCode,
      serviceNotificationConsent: detail.data.serviceNotificationConsent,
      marketingConsent: detail.data.marketingConsent,
    });
    setEditOpen(true);
  };

  const columns = [
    {
      title: "顾客",
      dataIndex: "displayName",
      render: (value: string) => (
        <Space>
          <span className="customer-avatar">
            <TeamOutlined />
          </span>
          <strong>{value}</strong>
        </Space>
      ),
    },
    { title: "手机号", dataIndex: "maskedMobile" },
    { title: "建档门店", dataIndex: "homeStoreName" },
    {
      title: "有效会员卡",
      dataIndex: "activeCardCount",
      render: (value: number) =>
        value ? <Tag color="blue">{value} 张</Tag> : <Tag>普通顾客</Tag>,
    },
    {
      title: "状态",
      dataIndex: "status",
      render: (value: string) => (
        <Tag color={value === "Active" ? "green" : "default"}>
          {value === "Active" ? "正常" : value}
        </Tag>
      ),
    },
    {
      title: "建档时间",
      dataIndex: "createdAtUtc",
      render: (value: string) =>
        new Date(value).toLocaleString("zh-CN", { hour12: false }),
    },
    {
      title: "操作",
      key: "action",
      width: 90,
      render: (_: unknown, record: CustomerSummary) => (
        <Button
          size="small"
          onClick={(event) => {
            event.stopPropagation();
            setRevealedMobile(undefined);
            setSelectedId(record.id);
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
          <Typography.Title level={2}>顾客与会员</Typography.Title>
          <Typography.Paragraph>
            顾客姓名按原名展示；手机号仅隐藏中间四位，完整号码仍可精确查询。
          </Typography.Paragraph>
        </div>
        <Space>
          {canExportCustomers && (
            <Button
              icon={<DownloadOutlined />}
              onClick={() => {
                exportForm.setFieldsValue({ includeFullMobile: false });
                setExportOpen(true);
              }}
            >
              导出名单
            </Button>
          )}
          {canManageCardTypes && (
            <Button
              icon={<SettingOutlined />}
              onClick={() => { setEditingCardType(undefined); setServiceItemDiscounts({}); setProductItemDiscounts({}); cardTypeForm.setFieldsValue({ serviceDiscount: 10, productDiscount: 10 }); setCardTypeOpen(true); }}
            >
              卡类配置
            </Button>
          )}
          {canCreateCustomer && <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => setCreateOpen(true)}
          >
            新建顾客
          </Button>}
        </Space>
      </div>
      <Alert
        type="info"
        showIcon
        title="会员开通只初始化本金、奖励金和积分账户，不会自动储值，也不能直接修改余额。"
      />
      <Card variant="borderless">
        <Space wrap>
          <Input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            allowClear
            maxLength={100}
            placeholder="输入姓名、完整手机号、手机尾号或会员卡号，自动匹配"
            prefix={<SearchOutlined />}
            suffix={
              query.trim() !== submittedQuery || customers.isFetching ? (
                <LoadingOutlined spin />
              ) : null
            }
            aria-label="实时查询顾客"
            style={{ width: 440 }}
          />
          <Typography.Text type="secondary">
            输入后自动加载，无需点击查询
          </Typography.Text>
        </Space>
      </Card>
      <Card variant="borderless" className="table-card">
        <Table<CustomerSummary>
          rowKey="id"
          columns={columns}
          dataSource={customers.data?.items}
          loading={customers.isFetching}
          pagination={{
            current: page,
            pageSize,
            total: customers.data?.total ?? 0,
            showSizeChanger: false,
            showTotal: (total) => `共 ${total} 位顾客`,
            onChange: setPage,
          }}
          locale={{ emptyText: <Empty description="没有匹配的顾客档案" /> }}
          onRow={(record) => ({
            onClick: () => {
              setRevealedMobile(undefined);
              setDetailSection("topups");
              setSelectedId(record.id);
            },
            className: "clickable-row",
          })}
        />
      </Card>

      <Drawer
        title="顾客与会员详情"
      size={560}
        open={Boolean(selectedId)}
        onClose={closeDetail}
        extra={
          <Space>
            {canManageCustomers && detail.data && (
              <>
                <Button icon={<EditOutlined />} onClick={beginEdit}>
                  编辑资料
                </Button>
                <Button
                  danger={detail.data.status === "Active"}
                  icon={
                    detail.data.status === "Active" ? (
                      <StopOutlined />
                    ) : (
                      <CheckCircleOutlined />
                    )
                  }
                  onClick={() => {
                    statusForm.resetFields();
                    setStatusAction(
                      detail.data!.status === "Active" ? "disable" : "restore",
                    );
                  }}
                >
                  {detail.data.status === "Active" ? "停用" : "恢复"}
                </Button>
              </>
            )}
            {canMergeCustomers && detail.data && (
              <Button
                onClick={() => {
                  mergeForm.resetFields();
                  setMergeTargetQuery("");
                  setMergePreview(undefined);
                  setMergeOpen(true);
                }}
              >
                合并重复档案
              </Button>
            )}
            {canOpenMembership && detail.data?.status === "Active" && (
              <Button
                type="primary"
                icon={<CreditCardOutlined />}
                onClick={() => setMembershipOpen(true)}
              >
                开通会员
              </Button>
            )}
          </Space>
        }
      >
        {detail.error && (
          <Alert
            type="error"
            showIcon
            title={
              detail.error instanceof Error
                ? detail.error.message
                : "详情加载失败"
            }
          />
        )}
        {detail.data && (
          <Space orientation="vertical" size={20} className="full-width">
            <Descriptions
              column={2}
              bordered
              size="small"
              items={[
                {
                  key: "name",
                  label: "顾客",
                  children: detail.data.displayName,
                },
                {
                  key: "mobile",
                  label: "手机号",
                  children: (
                    <Space>
                      {revealedMobile?.customerId === detail.data.id ? (
                        <Typography.Text copyable>
                          {revealedMobile.mobile}
                        </Typography.Text>
                      ) : (
                        detail.data.maskedMobile
                      )}
                      <Button
                        type="link"
                        size="small"
                        icon={<EyeOutlined />}
                        onClick={() => setRevealOpen(true)}
                      >
                        {revealedMobile?.customerId === detail.data.id
                          ? "重新查看"
                          : "按需查看"}
                      </Button>
                    </Space>
                  ),
                },
                {
                  key: "gender",
                  label: "性别",
                  children:
                    genderLabels[detail.data.gender] ?? detail.data.gender,
                },
                {
                  key: "birthDate",
                  label: "生日",
                  children: detail.data.birthDate ?? "未填写",
                },
                {
                  key: "residence",
                  label: "住宅",
                  children: detail.data.residence ?? "未填写",
                },
                {
                  key: "source",
                  label: "来源",
                  children: detail.data.sourceCode ?? "未填写",
                },
                {
                  key: "homeStore",
                  label: "建档门店",
                  children: detail.data.homeStoreName,
                },
                {
                  key: "status",
                  label: "状态",
                  children:
                    detail.data.status === "Active" ? (
                      <Tag color="green">正常</Tag>
                    ) : (
                      <Tag>已停用</Tag>
                    ),
                },
                {
                  key: "service",
                  label: "服务通知",
                  children: detail.data.serviceNotificationConsent
                    ? "已授权"
                    : "未授权",
                },
                {
                  key: "marketing",
                  label: "营销通知",
                  children: detail.data.marketingConsent ? "已授权" : "未授权",
                },
              ]}
            />
            {detail.data.mergedAliases?.length > 0 && (
              <Alert
                type="info"
                showIcon
                title={`已合并 ${detail.data.mergedAliases.length} 份历史档案`}
                description={detail.data.mergedAliases
                  .map(
                    (alias) => `${alias.displayName} · ${alias.maskedMobile}`,
                  )
                  .join("；")}
              />
            )}
            <Tabs
              activeKey={detailSection}
              onChange={(key) => setDetailSection(key as "topups" | "care")}
              items={[
                {
                  key: "topups",
                  label: "会员储值",
                  children: canViewFinancialDetails ? (
                    <TopupHistorySection
                      data={topups.data}
                      page={topupPage}
                      pageSize={topupPageSize}
                      canRequestRefund={canRequestTopupRefund}
                      onPageChange={setTopupPage}
                      onRefund={(item) => {
                        topupRefundForm.resetFields();
                        topupRefundForm.setFieldValue("amountYuan", item.remainingPrincipalMinor / 100);
                        setRefundTopup(item);
                      }}
                    />
                  ) : (
                    <Alert type="info" showIcon title="储值账户与流水仅店长和结算角色可查看。" />
                  ),
                },
                {
                  key: "care",
                  label: "服务档案",
                  children: canViewServiceRecords ? (
                    <ServiceRecordsSection customerId={detail.data.id} storeId={storeId!} />
                  ) : (
                    <Alert type="info" showIcon title="服务档案仅最高权限和门店店长可查看。" />
                  ),
                },
              ]}
            />
            <div>
              <Typography.Title level={4}>会员卡与账户</Typography.Title>
              {!detail.data.cards.length ? (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="尚未开通会员"
                />
              ) : (
                detail.data.cards.map((card) => (
                  <Card key={card.id} size="small" className="member-card">
                    <div className="member-card-title">
                      <div>
                        <Typography.Text type="secondary">
                          {card.cardTypeName}
                        </Typography.Text>
                        <Typography.Title level={5}>
                          {card.maskedCardNo}
                        </Typography.Title>
                      </div>
                      <Space>
                        <Tag color="green">有效</Tag>
                        {canTopup && (
                          <Button
                            size="small"
                            type="primary"
                            icon={<DollarOutlined />}
                            disabled={currentShift.data?.status !== "Open"}
                            onClick={() => beginTopup(card)}
                          >
                            储值
                          </Button>
                        )}
                      </Space>
                    </div>
                    <Typography.Text type="secondary">
                      有效期：{card.validFrom} 至 {card.validTo ?? "长期"}
                    </Typography.Text>
                    {canViewFinancialDetails ? (
                      <div className="account-grid">
                        {card.accounts.map((account) => (
                          <div key={account.id}>
                            <span>
                              {accountLabels[account.accountType] ??
                                account.accountType}
                            </span>
                            <strong>
                              {formatAccount(
                                account.accountType,
                                account.balanceUnits,
                              )}
                            </strong>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <Typography.Text type="secondary">
                        账户余额仅向店长和结算角色显示。
                      </Typography.Text>
                    )}
                  </Card>
                ))
              )}
            </div>
            {canViewFinancialDetails && detail.data.cards.length > 0 && (
              <MembershipBenefitsSection
                storeId={storeId!}
                customerId={detail.data.id}
                cards={detail.data.cards}
              />
            )}
          </Space>
        )}
      </Drawer>

      <Modal
        title="编辑顾客资料"
        open={editOpen}
        onCancel={() => setEditOpen(false)}
        onOk={() => editForm.submit()}
        confirmLoading={updateCustomer.isPending}
        okText="保存修改"
        destroyOnHidden
      >
        <Alert
          type="info"
          showIcon
          title="修改前读取完整手机号已留痕；保存使用版本校验，覆盖冲突会被拒绝。"
          className="modal-alert"
        />
        <Form<EditCustomerValues>
          form={editForm}
          layout="vertical"
          onFinish={(values) => updateCustomer.mutate(values)}
        >
          <Form.Item
            name="name"
            label="姓名"
            rules={[{ required: true }, { max: 100 }]}
          >
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item
            name="mobile"
            label="手机号"
            rules={[
              { required: true },
              {
                pattern: /^1[3-9]\d{9}$/,
                message: "请输入有效的中国大陆手机号",
              },
            ]}
          >
            <Input maxLength={11} inputMode="numeric" />
          </Form.Item>
          <Space align="start" className="full-width">
            <Form.Item name="gender" label="性别" className="grow">
              <Select
                options={[
                  { value: "Unknown", label: "未填写" },
                  { value: "Female", label: "女" },
                  { value: "Male", label: "男" },
                  { value: "Other", label: "其他" },
                ]}
              />
            </Form.Item>
            <Form.Item name="birthDate" label="生日（可选）" className="grow">
              <Input type="date" max={new Date().toISOString().slice(0, 10)} />
            </Form.Item>
          </Space>
          <Form.Item
            name="residence"
            label="住宅（可选）"
            rules={[{ max: 300 }]}
          >
            <Input maxLength={300} placeholder="例如小区、街道或常住区域" />
          </Form.Item>
          <Form.Item
            name="sourceCode"
            label="来源渠道（可选）"
            rules={[{ max: 40 }]}
          >
            <Input maxLength={40} />
          </Form.Item>
          <Form.Item name="serviceNotificationConsent" valuePropName="checked">
            <Checkbox>顾客已授权接收服务通知</Checkbox>
          </Form.Item>
          <Form.Item name="marketingConsent" valuePropName="checked">
            <Checkbox>顾客已单独授权接收营销信息</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
      <Modal
        title={statusAction === "restore" ? "恢复顾客档案" : "停用顾客档案"}
        open={Boolean(statusAction)}
        onCancel={() => setStatusAction(undefined)}
        onOk={() => statusForm.submit()}
        okText={statusAction === "restore" ? "确认恢复" : "确认停用"}
        okButtonProps={{ danger: statusAction === "disable" }}
        confirmLoading={changeCustomerStatus.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title={
            statusAction === "restore"
              ? "恢复后可继续开卡、储值和关联消费。"
              : "停用不会删除历史订单、服务档案、会员卡、账户余额或流水；需要时可再恢复。"
          }
          className="modal-alert"
        />
        <Form<CustomerStatusValues>
          form={statusForm}
          layout="vertical"
          onFinish={(values) => changeCustomerStatus.mutate(values)}
        >
          <Form.Item
            name="reason"
            label="操作原因"
            rules={[
              { required: true, whitespace: true },
              { min: 2 },
              { max: 200 },
            ]}
          >
            <Input.TextArea rows={3} maxLength={200} showCount />
          </Form.Item>
        </Form>
      </Modal>
      <Modal
        title="合并重复顾客档案"
        width={680}
        open={mergeOpen}
        onCancel={() => setMergeOpen(false)}
        onOk={() => mergeForm.submit()}
        okText="确认永久合并"
        okButtonProps={{ danger: true, disabled: !mergePreview?.canMerge }}
        confirmLoading={mergeCustomer.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="合并不可直接撤销。当前档案作为源档案停用并指向保留档案；历史订单、服务档案和账务流水不改写，统一通过保留档案聚合查看。"
          className="modal-alert"
        />
        <Form<MergeCustomerValues>
          form={mergeForm}
          layout="vertical"
          onValuesChange={(changed) => {
            if ("targetCustomerId" in changed) setMergePreview(undefined);
          }}
          onFinish={(values) =>
            mergePreview?.canMerge && mergeCustomer.mutate(values)
          }
        >
          <Form.Item
            name="targetCustomerId"
            label="保留的顾客档案"
            rules={[{ required: true, message: "请选择保留档案" }]}
          >
            <Select
              showSearch
              filterOption={false}
              onSearch={setMergeTargetQuery}
              loading={mergeTargets.isFetching}
              placeholder="输入姓名、完整手机号、尾号或卡号搜索"
              options={mergeTargets.data?.items
                .filter(
                  (item) => item.id !== selectedId && item.status === "Active",
                )
                .map((item) => ({
                  value: item.id,
                  label: `${item.displayName} · ${item.maskedMobile} · ${item.activeCardCount}张卡`,
                }))}
            />
          </Form.Item>
          <Button
            block
            loading={previewMerge.isPending}
            onClick={() => {
              const targetId = mergeForm.getFieldValue("targetCustomerId");
              if (!targetId) return message.warning("请先选择保留档案");
              previewMerge.mutate(targetId);
            }}
          >
            生成合并预览
          </Button>
          {mergePreview && (
            <Card size="small" style={{ marginTop: 12 }}>
              <Descriptions
                size="small"
                column={2}
                items={[
                  {
                    key: "source",
                    label: "将被合并",
                    children: `${mergePreview.sourceDisplayName} · ${mergePreview.sourceMaskedMobile}`,
                  },
                  {
                    key: "target",
                    label: "最终保留",
                    children: `${mergePreview.targetDisplayName} · ${mergePreview.targetMaskedMobile}`,
                  },
                  {
                    key: "cards",
                    label: "迁入会员卡",
                    children: `${mergePreview.sourceCardCount} 张`,
                  },
                  {
                    key: "balance",
                    label: "源档案余额",
                    children: `本金 ${formatAccount("Principal", mergePreview.sourcePrincipalBalanceMinor)}；奖励 ${formatAccount("Bonus", mergePreview.sourceBonusBalanceMinor)}；积分 ${mergePreview.sourcePointsBalance}`,
                  },
                  {
                    key: "orders",
                    label: "历史消费单",
                    children: `${mergePreview.sourceOrderCount} 单`,
                  },
                  {
                    key: "records",
                    label: "服务档案",
                    children: `${mergePreview.sourceServiceRecordCount} 条`,
                  },
                ]}
              />
              {mergePreview.blockers.length ? (
                <Alert
                  type="error"
                  showIcon
                  title="当前不能合并"
                  description={mergePreview.blockers.join("；")}
                  style={{ marginTop: 12 }}
                />
              ) : (
                <Alert
                  type="success"
                  showIcon
                  title="校验通过；保留档案的姓名和手机号作为主资料"
                  style={{ marginTop: 12 }}
                />
              )}
            </Card>
          )}
          <Form.Item
            name="reason"
            label="合并依据与原因"
            rules={[
              { required: true, whitespace: true },
              { min: 2 },
              { max: 200 },
            ]}
            style={{ marginTop: 12 }}
          >
            <Input.TextArea
              rows={3}
              maxLength={200}
              showCount
              placeholder="例如：本人核验为同一顾客，保留最新手机号档案"
            />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="按需查看完整手机号"
        open={revealOpen}
        onCancel={() => setRevealOpen(false)}
        onOk={() => revealForm.submit()}
        confirmLoading={revealMobile.isPending}
        okText="确认查看并留痕"
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="完整号码只在当前详情中临时显示；查看人、顾客、业务目的和时间都会写入审计。"
          className="modal-alert"
        />
        <Form<SensitivePurposeValues>
          form={revealForm}
          layout="vertical"
          onFinish={(values) => revealMobile.mutate(values)}
        >
          <Form.Item
            name="purpose"
            label="查看目的"
            rules={[
              { required: true, whitespace: true },
              { min: 2 },
              { max: 200 },
            ]}
          >
            <Input.TextArea
              rows={3}
              maxLength={200}
              showCount
              placeholder="例如：核对会员本人身份"
            />
          </Form.Item>
        </Form>
      </Modal>
      <Modal
        title="导出顾客名单"
        open={exportOpen}
        onCancel={() => setExportOpen(false)}
        onOk={() => exportForm.submit()}
        confirmLoading={exportCustomers.isPending}
        okText="确认导出并留痕"
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title={`姓名始终按原名导出；${canExportFullMobile ? "可选择手机号脱敏或完整导出。" : "店长只能导出中间四位脱敏的手机号。"} 单次最多5000条。`}
          className="modal-alert"
        />
        <Form<SensitivePurposeValues>
          form={exportForm}
          layout="vertical"
          initialValues={{ includeFullMobile: false }}
          onFinish={(values) => exportCustomers.mutate(values)}
        >
          <Form.Item
            name="purpose"
            label="导出目的"
            rules={[
              { required: true, whitespace: true },
              { min: 2 },
              { max: 200 },
            ]}
          >
            <Input.TextArea
              rows={3}
              maxLength={200}
              showCount
              placeholder="例如：本月会员回访名单"
            />
          </Form.Item>
          <Form.Item name="includeFullMobile" valuePropName="checked">
            <Checkbox disabled={!canExportFullMobile}>
              包含完整手机号（仅最高权限）
            </Checkbox>
          </Form.Item>
          <Typography.Text type="secondary">
            导出范围使用当前自动匹配条件；输入仍在加载时请等待结果稳定后导出。
          </Typography.Text>
        </Form>
      </Modal>
      <Modal
        title="新建顾客档案"
        open={createOpen}
        onCancel={() => setCreateOpen(false)}
        onOk={() => createForm.submit()}
        confirmLoading={createCustomer.isPending}
        okText="确认建档"
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="姓名按原名展示；完整手机号会加密保存，页面仅隐藏中间四位。"
          className="modal-alert"
        />
        <Form
          form={createForm}
          layout="vertical"
          initialValues={{
            gender: "Unknown",
            serviceNotificationConsent: false,
            marketingConsent: false,
          }}
          onFinish={(values) => createCustomer.mutate(values)}
        >
          <Form.Item
            name="name"
            label="姓名"
            rules={[{ required: true, message: "请输入姓名" }, { max: 100 }]}
          >
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item
            name="mobile"
            label="手机号"
            rules={[
              { required: true, message: "请输入手机号" },
              {
                pattern: /^1[3-9]\d{9}$/,
                message: "请输入有效的中国大陆手机号",
              },
            ]}
          >
            <Input maxLength={11} inputMode="numeric" />
          </Form.Item>
          <Space align="start" className="full-width">
            <Form.Item name="gender" label="性别" className="grow">
              <Select
                options={[
                  { value: "Unknown", label: "未填写" },
                  { value: "Female", label: "女" },
                  { value: "Male", label: "男" },
                  { value: "Other", label: "其他" },
                ]}
              />
            </Form.Item>
            <Form.Item name="birthDate" label="生日（可选）" className="grow">
              <Input type="date" max={new Date().toISOString().slice(0, 10)} />
            </Form.Item>
          </Space>
          <Form.Item
            name="residence"
            label="住宅（可选）"
            rules={[{ max: 300 }]}
          >
            <Input maxLength={300} placeholder="例如小区、街道或常住区域" />
          </Form.Item>
          <Form.Item
            name="sourceCode"
            label="来源渠道（可选）"
            rules={[{ max: 40 }]}
          >
            <Input placeholder="例如 WALK_IN" maxLength={40} />
          </Form.Item>
          <Form.Item name="serviceNotificationConsent" valuePropName="checked">
            <Checkbox>顾客已授权接收服务通知</Checkbox>
          </Form.Item>
          <Form.Item name="marketingConsent" valuePropName="checked">
            <Checkbox>顾客已单独授权接收营销信息</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
      <Modal
        title="开通会员"
        open={membershipOpen}
        onCancel={() => setMembershipOpen(false)}
        onOk={() => membershipForm.submit()}
        confirmLoading={openMembership.isPending}
        okText="确认开通"
        destroyOnHidden
      >
        <Alert
          type="info"
          showIcon
          title="开卡和储值是两个独立动作。本次只创建会员卡和三个零余额账户。"
          className="modal-alert"
        />
        <Form
          form={membershipForm}
          layout="vertical"
          onFinish={(values) => openMembership.mutate(values)}
        >
          <Form.Item
            name="cardTypeId"
            label="卡类"
            rules={[{ required: true, message: "请选择卡类" }]}
          >
            <Select
              options={cardTypes.data?.map((item) => ({
                value: item.id,
                label: `${item.name} · ${item.validityDays ? `${item.validityDays}天` : "长期"}`,
              }))}
            />
          </Form.Item>
          <Form.Item
            name="cardNo"
            label="会员卡号（可选）"
            rules={[{ min: 5 }, { max: 40 }]}
          >
            <Input placeholder="留空由系统生成" maxLength={40} />
          </Form.Item>
          <Form.Item name="note" label="备注（可选）" rules={[{ max: 500 }]}>
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
        </Form>
      </Modal>
      <Modal
        title={editingCardType ? `编辑卡类 · ${editingCardType.code}` : "新建并发布卡类"}
        width={1060}
        open={cardTypeOpen}
        onCancel={() => { setCardTypeOpen(false); setEditingCardType(undefined); setServiceItemDiscounts({}); setProductItemDiscounts({}); cardTypeForm.resetFields(); }}
        onOk={() => cardTypeForm.submit()}
        confirmLoading={saveCardType.isPending}
        okText={editingCardType ? "保存折扣配置" : "发布卡类"}
        destroyOnHidden
      >
        <Space wrap className="card-type-config-list">
          {cardTypes.data?.map((cardType) => <Button key={cardType.id} onClick={() => {
            setEditingCardType(cardType);
            cardTypeForm.setFieldsValue({ name: cardType.name, validityDays: cardType.validityDays,
              serviceDiscount: cardType.serviceDiscountBasisPoints / 1000,
              productDiscount: cardType.productDiscountBasisPoints / 1000 });
            setServiceItemDiscounts(Object.fromEntries((cardType.serviceItemDiscounts ?? []).map((item) => [item.catalogItemId, item.discountBasisPoints / 1000])));
            setProductItemDiscounts(Object.fromEntries((cardType.productItemDiscounts ?? []).map((item) => [item.catalogItemId, item.discountBasisPoints / 1000])));
          }}>{cardType.name} · 服务 {(cardType.serviceDiscountBasisPoints / 1000).toFixed(3).replace(/0+$/, '').replace(/\.$/, '')} 折 · 产品 {(cardType.productDiscountBasisPoints / 1000).toFixed(3).replace(/0+$/, '').replace(/\.$/, '')} 折</Button>)}
          {editingCardType && <Button type="link" onClick={() => { setEditingCardType(undefined); setServiceItemDiscounts({}); setProductItemDiscounts({}); cardTypeForm.resetFields(); cardTypeForm.setFieldsValue({ serviceDiscount: 10, productDiscount: 10 }); }}>新建其他卡类</Button>}
        </Space>
        <Form
          form={cardTypeForm}
          layout="vertical"
          onFinish={(values) => saveCardType.mutate(values)}
        >
          <Alert
            type="info"
            showIcon
            title="折扣由后端权威计算。10 折表示原价；会员价不与团购核销叠加，历史订单不随以后调整而变化。"
            className="modal-alert"
          />
          <Form.Item
            name="name"
            label="卡类名称"
            rules={[{ required: true }, { max: 80 }]}
          >
            <Input placeholder="例如 长期会员" maxLength={80} />
          </Form.Item>
          <div className="two-column-form">
            <Form.Item name="serviceDiscount" label="服务项目折扣" initialValue={10}
              extra="按行业习惯填写：9.5 表示原价的 95%，10 表示不打折"
              rules={[{ required: true }, { type: "number", min: 1, max: 10 }]}>
              <InputNumber min={1} max={10} step={0.001} precision={3} suffix="折" className="full-width" />
            </Form.Item>
            <Form.Item name="productDiscount" label="产品折扣" initialValue={10}
              extra="可与服务折扣不同；9 表示原价的 90%"
              rules={[{ required: true }, { type: "number", min: 1, max: 10 }]}>
              <InputNumber min={1} max={10} step={0.001} precision={3} suffix="折" className="full-width" />
            </Form.Item>
          </div>
          <Form.Item
            name="validityDays"
            label="有效期天数（可选）"
            rules={[{ type: "number", min: 1, max: 3650 }]}
          >
            <InputNumber
              min={1}
              max={3650}
              precision={0}
              className="full-width"
              placeholder="留空表示长期有效"
            />
          </Form.Item>
          <Tabs items={[
            { key: "services", label: `服务项目单独折扣（${Object.values(serviceItemDiscounts).filter((value) => value !== undefined).length}）`, children: <>
              <Alert type="info" showIcon title="单独设置的项目优先；留空则沿用上面的服务默认折扣。" className="modal-alert" />
              <Space wrap style={{ marginBottom: 12 }}><InputNumber min={1} max={10} step={0.001} precision={3} suffix="折" value={serviceBulkDiscount} onChange={(value) => setServiceBulkDiscount(Number(value ?? 10))} /><Button onClick={() => setServiceItemDiscounts(Object.fromEntries((discountServiceItems.data ?? []).map((item) => [item.id, serviceBulkDiscount])))}>应用到全部服务</Button><Button onClick={() => setServiceItemDiscounts({})}>全部改为沿用默认</Button></Space>
              <Table<ServiceItem> rowKey="id" size="small" loading={discountServiceItems.isLoading} dataSource={discountServiceItems.data ?? []} pagination={{ pageSize: 8, showSizeChanger: false }} columns={[{ title: "项目编码", dataIndex: "code", width: 130 }, { title: "服务项目", dataIndex: "name" }, { title: "状态", dataIndex: "status", width: 80, render: (value: string) => value === "ENABLED" ? "启用" : "停用" }, { title: "本卡折扣", width: 190, render: (_: unknown, item) => <InputNumber min={1} max={10} step={0.001} precision={3} suffix="折" placeholder="沿用默认" value={serviceItemDiscounts[item.id]} onChange={(value) => setServiceItemDiscounts((current) => ({ ...current, [item.id]: value === null ? undefined : Number(value) }))} /> }]} />
            </> },
            { key: "products", label: `产品单独折扣（${Object.values(productItemDiscounts).filter((value) => value !== undefined).length}）`, children: <>
              <Alert type="info" showIcon title="单独设置的产品优先；留空则沿用上面的产品默认折扣。" className="modal-alert" />
              <Space wrap style={{ marginBottom: 12 }}><InputNumber min={1} max={10} step={0.001} precision={3} suffix="折" value={productBulkDiscount} onChange={(value) => setProductBulkDiscount(Number(value ?? 10))} /><Button onClick={() => setProductItemDiscounts(Object.fromEntries((discountProductItems.data ?? []).map((item) => [item.id, productBulkDiscount])))}>应用到全部产品</Button><Button onClick={() => setProductItemDiscounts({})}>全部改为沿用默认</Button></Space>
              <Table<ProductItem> rowKey="id" size="small" loading={discountProductItems.isLoading} dataSource={discountProductItems.data ?? []} pagination={{ pageSize: 8, showSizeChanger: false }} columns={[{ title: "产品编码", dataIndex: "code", width: 130 }, { title: "产品", dataIndex: "name" }, { title: "单位", dataIndex: "unitName", width: 80 }, { title: "状态", dataIndex: "status", width: 80, render: (value: string) => value === "ENABLED" ? "启用" : "停用" }, { title: "本卡折扣", width: 190, render: (_: unknown, item) => <InputNumber min={1} max={10} step={0.001} precision={3} suffix="折" placeholder="沿用默认" value={productItemDiscounts[item.id]} onChange={(value) => setProductItemDiscounts((current) => ({ ...current, [item.id]: value === null ? undefined : Number(value) }))} /> }]} />
            </> },
          ]} />
        </Form>
      </Modal>
      {storeId && selectedId && detail.data && <MemberTopupModal
        open={Boolean(topupCard)}
        storeId={storeId}
        storeName={auth.store?.name}
        customerId={selectedId}
        customerName={detail.data.displayName}
        cards={topupCard ? [topupCard] : []}
        methods={paymentMethods.data ?? []}
        shiftOpen={currentShift.data?.status === "Open"}
        shiftLoading={currentShift.isLoading}
        canGrantBonus={canGrantBonus}
        onClose={() => setTopupCard(undefined)}
        onSuccess={async () => {
          await Promise.all([
            queryClient.invalidateQueries({ queryKey: ["customer", storeId, selectedId] }),
            queryClient.invalidateQueries({ queryKey: ["member-topups", storeId, selectedId] }),
            queryClient.invalidateQueries({ queryKey: ["payments", storeId] }),
            queryClient.invalidateQueries({ queryKey: ["cashier-shift", storeId] }),
          ]);
        }}
      />}
      <Modal
        title={`储值退款 · ${refundTopup?.topupNo ?? ""}`}
        open={Boolean(refundTopup)}
        onCancel={() => setRefundTopup(undefined)}
        onOk={() => topupRefundForm.submit()}
        okText="提交退款审批"
        okButtonProps={{ danger: true }}
        confirmLoading={requestTopupRefund.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title={`最多可申请退回本金 ${formatAccount("Principal", refundTopup?.remainingPrincipalMinor ?? 0)}。系统按累计退款比例向上取整收回对应赠送奖励；可退本金或奖励已被使用时会拒绝。`}
          className="modal-alert"
        />
        <Form<TopupRefundValues>
          form={topupRefundForm}
          layout="vertical"
          onFinish={(values) =>
            refundTopup &&
            requestTopupRefund.mutate({ topup: refundTopup, values })
          }
        >
          <Form.Item
            name="amountYuan"
            label="退回本金（元）"
            rules={[{ required: true }, { type: "number", min: 0.01, max: (refundTopup?.remainingPrincipalMinor ?? 0) / 100 }]}
          >
            <InputNumber min={0.01} max={(refundTopup?.remainingPrincipalMinor ?? 0) / 100} precision={2} prefix="¥" className="full-width" />
          </Form.Item>
          <Form.Item
            name="reason"
            label="冲正原因"
            rules={[
              { required: true, whitespace: true },
              { min: 2 },
              { max: 500 },
            ]}
          >
            <Input.TextArea rows={4} maxLength={500} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}

function TopupHistorySection({ data, page, pageSize, canRequestRefund, onPageChange, onRefund }: {
  data?: PageResult<MemberTopup>;
  page: number;
  pageSize: number;
  canRequestRefund: boolean;
  onPageChange: (page: number) => void;
  onRefund: (topup: MemberTopup) => void;
}) {
  if (!data?.items.length)
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="还没有储值记录" />;
  return <>
    {data.items.map((item) => {
      const originalRouteAvailable = item.allocations.every((line) => line.category === "Cash");
      return <Card key={item.id} size="small" className="topup-history">
        <div>
          <strong>{item.topupNo}</strong>
          <Space>
            <Tag color={item.status === "Refunded" ? "default" : item.status === "PartiallyRefunded" ? "gold" : "green"}>
              {item.status === "Refunded" ? "已全部退款" : item.status === "PartiallyRefunded" ? "部分已退" : "已入账"}
            </Tag>
            {canRequestRefund && (item.status === "Paid" || item.status === "PartiallyRefunded") &&
              item.remainingPrincipalMinor > 0 && <Button size="small" danger
                disabled={!originalRouteAvailable}
                title={originalRouteAvailable ? "提交后由最高权限审批" : "人工外部登记暂不支持原路冲正"}
                onClick={() => onRefund(item)}>申请退款</Button>}
          </Space>
        </div>
        <div>
          <span>实收本金 {formatAccount("Principal", item.principalMinor)}</span>
          <span>赠送奖励 {formatAccount("Bonus", item.bonusMinor)}</span>
          {item.refundedPrincipalMinor > 0 && <span>已退本金 {formatAccount("Principal", item.refundedPrincipalMinor)} · 已收回奖励 {formatAccount("Bonus", item.revokedBonusMinor)}</span>}
          <span>{new Date(item.paidAtUtc).toLocaleString("zh-CN", { hour12: false })}</span>
        </div>
        {item.allocations.map((line) => <Space key={line.id} wrap>
          <Tag>{line.methodName} {formatAccount("Principal", line.amountMinor)}</Tag>
          {line.reconciliationStatus === "Pending" && <Tag color="gold">待核对</Tag>}
        </Space>)}
      </Card>;
    })}
    <Pagination current={page} pageSize={pageSize} total={data.total} showSizeChanger={false}
      showTotal={(total) => `共 ${total} 笔`} onChange={onPageChange}
      style={{ marginTop: 12, textAlign: "right" }} />
  </>;
}
