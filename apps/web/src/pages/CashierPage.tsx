import {
  CheckCircleOutlined,
  DeleteOutlined,
  FileDoneOutlined,
  PictureOutlined,
  PlusOutlined,
  PrinterOutlined,
  SafetyCertificateOutlined,
  WalletOutlined,
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
  Image,
  Input,
  InputNumber,
  Modal,
  QRCode,
  Segmented,
  Select,
  Space,
  Statistic,
  Table,
  Tag,
  Typography,
  message,
} from "antd";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { apiRequest, ApiError } from "../api/client";
import type {
  CashierShift,
  CashierShiftReview,
  CashierVisit,
  CustomerDetail,
  CustomerSummary,
  MemberAccount,
  MemberVerification,
  PageResult,
  Payment,
  PaymentChannelOrder,
  PaymentMethod,
  PaymentReceipt,
  PriceBook,
  PriceOverrideApproval,
  PriceOverridePolicy,
  ProductItem,
  ProductReturn,
  Refund,
  ServiceEmployee,
  ServiceOrder,
  ServiceOrderLine,
} from "../api/types";
import { useAuth } from "../auth/useAuth";
import { useDebouncedValue } from "../hooks/useDebouncedValue";
import { Permission } from "../security/permissions";
import { useAuthorization } from "../security/useAuthorization";
import {
  canActivateShiftReview,
  cashAmountMinor as calculateCashAmountMinor,
  cashTenderedMinorForSubmission,
  hasAllocationCategory,
} from "./cashierRules";

interface OrderLineValues {
  lineType: "Service" | "Product";
  serviceItemId?: string;
  productItemId?: string;
  serviceEmployeeId?: string;
  quantity: number;
  actualMinutes?: number;
  enteredPriceYuan: number;
  priceOverrideReason?: string;
}
interface OrderValues {
  source: "standalone" | "visit";
  visitId?: string;
  customerId?: string;
  note?: string;
  lines: OrderLineValues[];
}
interface SettleAllocationValues {
  methodId: string;
  amountYuan: number;
  externalReference?: string;
  memberAccountId?: string;
}
interface SettleValues {
  allocations: SettleAllocationValues[];
  cashTenderedYuan?: number;
  verifiedMobile?: string;
  verificationCode?: string;
}
type SettlementMemberAccount = MemberAccount & { cardLabel: string };
interface ShiftValues {
  amountYuan: number;
  note?: string;
}
interface ReviewValues {
  reason?: string;
}
interface RefundValues {
  reason: string;
  lines: { originalAllocationId: string; amountYuan: number }[];
}
interface RejectRefundValues {
  reason: string;
}
interface VoidOrderValues {
  reason: string;
}
interface ProductReturnValues {
  quantity: number;
  reason: string;
}
interface PricePolicyValues {
  managerLineDiscountPercent: number;
  managerOrderDiscountYuan: number;
  allowManagerPriceIncrease: boolean;
}
interface PriceApprovalValues {
  note?: string;
}
interface OrderFilters {
  customerId?: string;
  catalogItemId?: string;
  employeeId?: string;
  status?: string;
  fromDate?: string;
  toDate?: string;
}

const statusMeta: Record<string, { label: string; color: string }> = {
  Draft: { label: "待确认金额", color: "gold" },
  PendingPayment: { label: "待支付", color: "blue" },
  PaymentProcessing: { label: "支付处理中", color: "processing" },
  Settled: { label: "已结算", color: "green" },
  PartiallyRefunded: { label: "部分退款", color: "orange" },
  Refunded: { label: "已退款", color: "default" },
  Voided: { label: "已作废", color: "default" },
};
const priceAuthorizationMeta: Record<string, { label: string; color: string }> =
  {
    NotRequired: { label: "标准价", color: "default" },
    DirectAuthorized: { label: "权限内改价", color: "green" },
    PendingApproval: { label: "待改价审批", color: "gold" },
    Approved: { label: "改价已批准", color: "green" },
    Rejected: { label: "改价已驳回", color: "red" },
    Cancelled: { label: "审批已取消", color: "default" },
  };
function money(minor: number) {
  return `¥${(minor / 100).toFixed(2)}`;
}
function duration(seconds?: number) {
  if (seconds === undefined || seconds === null) return "未填写";
  const whole = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const rest = whole % 60;
  return `${hours ? `${hours}小时` : ""}${minutes ? `${minutes}分` : ""}${rest || (!hours && !minutes) ? `${rest}秒` : ""}`;
}
function commandId() {
  return crypto.randomUUID();
}
function openReceiptPrint(receipt: PaymentReceipt, popup: Window) {
  const document = popup.document;
  document.title = `${receipt.printLabel}-${receipt.orderNo}`;
  const style = document.createElement("style");
  style.textContent =
    "body{font:14px/1.5 system-ui,sans-serif;width:320px;margin:20px auto;color:#111}h1{text-align:center;font-size:20px}.meta,.row{display:flex;justify-content:space-between;gap:12px}.muted{color:#666}.divider{border-top:1px dashed #777;margin:10px 0}.item{margin:8px 0}.center{text-align:center}";
  document.head.append(style);
  const add = (tag: string, value: string, className?: string) => {
    const element = document.createElement(tag);
    element.textContent = value;
    if (className) element.className = className;
    document.body.append(element);
    return element;
  };
  const row = (label: string, value: string) => {
    const element = document.createElement("div");
    element.className = "row";
    const left = document.createElement("span");
    left.textContent = label;
    const right = document.createElement("strong");
    right.textContent = value;
    element.append(left, right);
    document.body.append(element);
  };
  add("h1", receipt.storeName);
  add("div", receipt.printLabel, "center");
  add("div", `消费单 ${receipt.orderNo}`, "muted");
  add("div", `支付单 ${receipt.paymentNo}`, "muted");
  add(
    "div",
    `顾客 ${receipt.customerName} · 收银 ${receipt.operatorName}`,
    "muted",
  );
  add(
    "div",
    `收款 ${new Date(receipt.paidAtUtc).toLocaleString("zh-CN")}`,
    "muted",
  );
  add("div", "", "divider");
  receipt.lines.forEach((line) => {
    add(
      "div",
      `${line.itemName}${line.employeeName ? ` · ${line.employeeName}` : ""}`,
      "item",
    );
    row(
      `${line.quantity}${line.unitName ?? ""} × ${money(line.unitPriceMinor)}`,
      money(line.amountMinor),
    );
  });
  add("div", "", "divider");
  row("应收合计", money(receipt.receivableMinor));
  receipt.allocations.forEach((line) =>
    row(line.methodName, money(line.amountMinor)),
  );
  if (receipt.memberPrincipalBalanceAfterMinor != null &&
      receipt.memberBonusBalanceAfterMinor != null) {
    row("结算后储值余额", money(receipt.memberPrincipalBalanceAfterMinor +
      receipt.memberBonusBalanceAfterMinor));
  }
  if (receipt.cashTenderedMinor !== undefined)
    row("现金实收", money(receipt.cashTenderedMinor));
  if (receipt.cashChangeMinor !== undefined)
    row("找零", money(receipt.cashChangeMinor));
  add(
    "div",
    `打印时间 ${new Date(receipt.printedAtUtc).toLocaleString("zh-CN")} · 第${receipt.printSequence}次`,
    "muted",
  );
  add("p", "谢谢惠顾", "center");
  popup.focus();
  popup.print();
}

export function CashierPage() {
  const auth = useAuth();
  const { can } = useAuthorization();
  const storeId = auth.store?.id;
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<string>();
  const [settleOrder, setSettleOrder] = useState<ServiceOrder>();
  const [settleAfterShiftOpen, setSettleAfterShiftOpen] = useState<ServiceOrder>();
  const [shiftAction, setShiftAction] = useState<"open" | "submit">();
  const [reviewShift, setReviewShift] = useState<CashierShiftReview>();
  const [refundPayment, setRefundPayment] = useState<Payment>();
  const [rejectRefund, setRejectRefund] = useState<Refund>();
  const [voidOrder, setVoidOrder] = useState<ServiceOrder>();
  const [returnLine, setReturnLine] = useState<ServiceOrderLine>();
  const [pricePolicyOpen, setPricePolicyOpen] = useState(false);
  const [rejectPriceApproval, setRejectPriceApproval] =
    useState<PriceOverrideApproval>();
  const [memberVerification, setMemberVerification] =
    useState<MemberVerification>();
  const [channelOrder, setChannelOrder] = useState<PaymentChannelOrder>();
  const [orderPage, setOrderPage] = useState(1);
  const [orderKeyword, setOrderKeyword] = useState("");
  const appliedOrderKeyword = useDebouncedValue(orderKeyword.trim());
  const [orderFilters, setOrderFilters] = useState<OrderFilters>({});
  const [workbenchView, setWorkbenchView] = useState<
    "today" | "pending" | "review"
  >("today");
  const orderPageSize = 10;
  const [form] = Form.useForm<OrderValues>();
  const [settleForm] = Form.useForm<SettleValues>();
  const [shiftForm] = Form.useForm<ShiftValues>();
  const [reviewForm] = Form.useForm<ReviewValues>();
  const [refundForm] = Form.useForm<RefundValues>();
  const [rejectRefundForm] = Form.useForm<RejectRefundValues>();
  const [voidForm] = Form.useForm<VoidOrderValues>();
  const [returnForm] = Form.useForm<ProductReturnValues>();
  const [pricePolicyForm] = Form.useForm<PricePolicyValues>();
  const [priceApprovalForm] = Form.useForm<PriceApprovalValues>();
  const orderParams = new URLSearchParams({
    storeId: storeId ?? "",
    page: String(orderPage),
    pageSize: String(orderPageSize),
  });
  if (appliedOrderKeyword) orderParams.set("query", appliedOrderKeyword);
  Object.entries(orderFilters).forEach(([key, value]) => {
    if (value) orderParams.set(key, value);
  });
  const orders = useQuery({
    queryKey: [
      "cashier-orders",
      storeId,
      orderPage,
      appliedOrderKeyword,
      orderFilters,
    ],
    enabled: Boolean(storeId),
    queryFn: () =>
      apiRequest<PageResult<ServiceOrder>>(
        `/api/v1/cashier/orders?${orderParams}`,
      ),
  });
  const pendingVisits = useQuery({
    queryKey: ["cashier-visits", storeId],
    enabled: Boolean(storeId),
    queryFn: () =>
      apiRequest<PageResult<CashierVisit>>(
        `/api/v1/cashier/pending-visits?storeId=${storeId}&page=1&pageSize=100`,
      ),
    select: (result) => result.items,
  });
  const priceBooks = useQuery({
    queryKey: ["price-books"],
    queryFn: () => apiRequest<PriceBook[]>("/api/v1/catalog/price-books"),
  });
  const products = useQuery({
    queryKey: ["product-items"],
    queryFn: () => apiRequest<ProductItem[]>("/api/v1/catalog/products"),
  });
  const serviceEmployees = useQuery({
    queryKey: ["service-employees", storeId],
    enabled: Boolean(storeId),
    queryFn: () =>
      apiRequest<ServiceEmployee[]>(
        `/api/v1/cashier/service-employees?storeId=${storeId}`,
      ),
  });
  const customers = useQuery({
    queryKey: ["customers", storeId, "cashier"],
    enabled: Boolean(storeId),
    queryFn: () =>
      apiRequest<PageResult<CustomerSummary>>("/api/v1/customers/search", {
        method: "POST",
        body: JSON.stringify({ storeId, query: "", page: 1, pageSize: 100 }),
      }),
    select: (result) => result.items,
  });
  const selected = useQuery({
    queryKey: ["cashier-order", storeId, selectedId],
    enabled: Boolean(storeId && selectedId),
    queryFn: () =>
      apiRequest<ServiceOrder>(
        `/api/v1/cashier/orders/${selectedId}?storeId=${storeId}`,
      ),
  });
  const paymentMethods = useQuery({
    queryKey: ["payment-methods", storeId],
    enabled: Boolean(storeId),
    queryFn: () =>
      apiRequest<PaymentMethod[]>(
        `/api/v1/payments/methods?storeId=${storeId}`,
      ),
  });
  const payments = useQuery({
    queryKey: ["payments", storeId],
    enabled: Boolean(storeId),
    queryFn: () =>
      apiRequest<PageResult<Payment>>(
        `/api/v1/payments?storeId=${storeId}&page=1&pageSize=100`,
      ),
    select: (result) => result.items,
  });
  const refunds = useQuery({
    queryKey: ["refunds", storeId],
    enabled: Boolean(storeId && can(Permission.RefundRequest)),
    queryFn: () =>
      apiRequest<PageResult<Refund>>(
        `/api/v1/refunds?storeId=${storeId}&page=1&pageSize=100`,
      ),
    select: (result) => result.items,
  });
  const currentShift = useQuery({
    queryKey: ["cashier-shift", storeId],
    enabled: Boolean(storeId),
    queryFn: () =>
      apiRequest<CashierShift | undefined>(
        `/api/v1/payments/shifts/current?storeId=${storeId}`,
      ),
  });
  const activeChannelOrder = useQuery({
    queryKey: ["payment-channel-order", storeId, selected.data?.id],
    enabled: Boolean(
      storeId &&
        selected.data?.id &&
        selected.data.status === "PaymentProcessing",
    ),
    queryFn: () =>
      apiRequest<PaymentChannelOrder>(
        `/api/v1/payment-channels/orders/by-service-order/${selected.data?.id}?storeId=${storeId}`,
      ),
    retry: false,
  });
  const settlementCustomer = useQuery({
    queryKey: ["settlement-customer", storeId, settleOrder?.customerId],
    enabled: Boolean(storeId && settleOrder?.customerId),
    queryFn: () =>
      apiRequest<CustomerDetail>(
        `/api/v1/customers/${settleOrder?.customerId}?storeId=${storeId}`,
      ),
  });
  const canReviewShifts = can(Permission.ShiftReview);
  const canApproveRefunds = can(Permission.RefundApprove);
  const canRequestRefunds = can(Permission.RefundRequest);
  const isOwner = can(Permission.CashierApprovePrice);
  const pricePolicy = useQuery({
    queryKey: ["price-override-policy"],
    enabled: Boolean(auth.user),
    queryFn: () =>
      apiRequest<PriceOverridePolicy>("/api/v1/cashier/price-policy"),
  });
  const priceApprovals = useQuery({
    queryKey: ["price-override-approvals", storeId],
    enabled: Boolean(storeId && isOwner),
    queryFn: () =>
      apiRequest<PageResult<PriceOverrideApproval>>(
        `/api/v1/cashier/price-approvals?storeId=${storeId}&status=Pending&page=1&pageSize=100`,
      ),
    select: (result) => result.items,
  });
  const shiftReviews = useQuery({
    queryKey: ["cashier-shift-reviews", storeId],
    enabled: Boolean(storeId && canReviewShifts),
    queryFn: () =>
      apiRequest<PageResult<CashierShiftReview>>(
        `/api/v1/payments/shifts?storeId=${storeId}&page=1&pageSize=100`,
      ),
    select: (result) => result.items,
  });
  const publishedBook = useMemo(
    () =>
      priceBooks.data
        ?.filter((book) => book.status === "PUBLISHED")
        .sort(
          (a, b) =>
            b.effectiveFrom.localeCompare(a.effectiveFrom) ||
            (b.publishedAtUtc ?? "").localeCompare(a.publishedAtUtc ?? ""),
        )[0],
    [priceBooks.data],
  );
  useEffect(() => setOrderPage(1), [appliedOrderKeyword, orderFilters]);
  const refresh = async () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["cashier-orders", storeId] }),
      queryClient.invalidateQueries({ queryKey: ["cashier-visits", storeId] }),
      queryClient.invalidateQueries({ queryKey: ["payments", storeId] }),
      queryClient.invalidateQueries({ queryKey: ["refunds", storeId] }),
      queryClient.invalidateQueries({ queryKey: ["cashier-shift", storeId] }),
      queryClient.invalidateQueries({
        queryKey: ["cashier-shift-reviews", storeId],
      }),
      queryClient.invalidateQueries({
        queryKey: ["price-override-approvals", storeId],
      }),
      queryClient.invalidateQueries({ queryKey: ["notifications", storeId] }),
    ]);
  const onError = (error: unknown) =>
    message.error(error instanceof ApiError ? error.message : "操作失败");
  const create = useMutation({
    mutationFn: (values: OrderValues) =>
      apiRequest<ServiceOrder>("/api/v1/cashier/orders", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          visitId: values.source === "visit" ? values.visitId : null,
          customerId: values.customerId || null,
          note: values.note,
          commandId: commandId(),
          lines: values.lines.map((line) => ({
            lineType: line.lineType,
            serviceItemId:
              line.lineType === "Service" ? line.serviceItemId : null,
            productItemId:
              line.lineType === "Product" ? line.productItemId : null,
            serviceEmployeeId:
              line.lineType === "Service" ? line.serviceEmployeeId : null,
            quantity: line.quantity,
            actualSeconds:
              line.lineType === "Service" && line.actualMinutes !== undefined
                ? Math.round(line.actualMinutes * 60)
                : null,
            enteredPriceMinor: Math.round(line.enteredPriceYuan * 100),
            priceOverrideReason: line.priceOverrideReason,
          })),
        }),
      }),
    onSuccess: async (result) => {
      message.success(
        result.priceAuthorizationStatus === "PendingApproval"
          ? "消费单已创建，改价已提交最高权限审批"
          : "消费单草稿已创建，尚未收款",
      );
      setCreateOpen(false);
      form.resetFields();
      setSelectedId(result.id);
      await refresh();
    },
    onError,
  });
  const confirm = useMutation({
    mutationFn: (order: ServiceOrder) =>
      apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${order.id}/confirm`, {
        method: "POST",
        body: JSON.stringify({
          storeId,
          expectedVersion: order.version,
          commandId: commandId(),
        }),
      }),
    onSuccess: async (result) => {
      message.success("金额已确认，消费单进入待支付");
      await Promise.all([
        refresh(),
        queryClient.setQueryData(["cashier-order", storeId, result.id], result),
      ]);
    },
    onError,
  });
  const voidMutation = useMutation({
    mutationFn: ({
      order,
      values,
    }: {
      order: ServiceOrder;
      values: VoidOrderValues;
    }) =>
      apiRequest<ServiceOrder>(`/api/v1/cashier/orders/${order.id}/void`, {
        method: "POST",
        body: JSON.stringify({
          storeId,
          expectedVersion: order.version,
          reason: values.reason,
          commandId: commandId(),
        }),
      }),
    onSuccess: async (result) => {
      message.success("消费单已作废；如有库存预占已全部释放");
      setVoidOrder(undefined);
      voidForm.resetFields();
      await Promise.all([
        refresh(),
        queryClient.setQueryData(["cashier-order", storeId, result.id], result),
        queryClient.invalidateQueries({
          queryKey: ["inventory-balances", storeId],
        }),
      ]);
    },
    onError,
  });
  const productReturn = useMutation({
    mutationFn: ({
      order,
      line,
      values,
    }: {
      order: ServiceOrder;
      line: ServiceOrderLine;
      values: ProductReturnValues;
    }) =>
      apiRequest<ProductReturn>("/api/v1/inventory/product-returns", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          orderId: order.id,
          orderLineId: line.id,
          quantity: values.quantity,
          reason: values.reason,
          expectedOrderVersion: order.version,
          commandId: commandId(),
        }),
      }),
    onSuccess: async () => {
      message.success("产品退货已登记并回库；资金退款需另走原支付退款流程");
      setReturnLine(undefined);
      returnForm.resetFields();
      await Promise.all([
        refresh(),
        queryClient.invalidateQueries({
          queryKey: ["cashier-order", storeId, selectedId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["inventory-balances", storeId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["inventory-movements", storeId],
        }),
      ]);
    },
    onError,
  });
  const settle = useMutation({
    mutationFn: ({
      order,
      values,
    }: {
      order: ServiceOrder;
      values: SettleValues;
    }) =>
      apiRequest<Payment>(`/api/v1/payments/orders/${order.id}/settle`, {
        method: "POST",
        body: JSON.stringify({
          storeId,
          expectedVersion: order.version,
          commandId: commandId(),
          cashTenderedMinor: cashTenderedMinorForSubmission(
            values.allocations,
            paymentMethods.data ?? [],
            values.cashTenderedYuan,
          ),
          verifiedMobile: values.verifiedMobile,
          verificationChallengeId:
            memberVerification?.status === "Verified"
              ? memberVerification.id
              : null,
          allocations: values.allocations.map((line) => ({
            methodId: line.methodId,
            amountMinor: Math.round(line.amountYuan * 100),
            externalReference: line.externalReference,
            memberAccountId: line.memberAccountId,
          })),
        }),
      }),
    onSuccess: async (result, variables) => {
      const includesManualExternal = hasAllocationCategory(
        variables.values.allocations,
        paymentMethods.data ?? [],
        "ManualExternal",
      );
      const includesMemberAccount = hasAllocationCategory(
        variables.values.allocations,
        paymentMethods.data ?? [],
        "InternalAccount",
      );
      message.success(
        result.cashChangeMinor
          ? `结算完成，应找零 ${money(result.cashChangeMinor)}`
          : includesManualExternal
            ? "人工收款已记录，消费单已结算；金额已计入当前班次的人工外部收款待核对"
            : includesMemberAccount
              ? "结算完成；会员余额已写入不可变扣款流水"
              : "收款已记录，消费单已结算",
      );
      setSettleOrder(undefined);
      setMemberVerification(undefined);
      settleForm.resetFields();
      await refresh();
      await queryClient.invalidateQueries({
        queryKey: ["cashier-order", storeId, result.businessId],
      });
    },
    onError,
  });
  const printReceipt = useMutation({
    mutationFn: ({ payment }: { payment: Payment; popup: Window }) =>
      apiRequest<PaymentReceipt>(`/api/v1/payments/${payment.id}/receipt`, {
        method: "POST",
        body: JSON.stringify({ storeId, commandId: commandId() }),
      }),
    onSuccess: (receipt, variables) =>
      openReceiptPrint(receipt, variables.popup),
    onError: (error, variables) => {
      variables.popup.close();
      onError(error);
    },
  });
  const initiateChannel = useMutation({
    mutationFn: ({
      order,
      methodId,
    }: {
      order: ServiceOrder;
      methodId: string;
    }) =>
      apiRequest<PaymentChannelOrder>(
        `/api/v1/payment-channels/orders/${order.id}/initiate`,
        {
          method: "POST",
          body: JSON.stringify({
            storeId,
            expectedOrderVersion: order.version,
            methodId,
            commandId: commandId(),
          }),
        },
      ),
    onSuccess: async (result) => {
      setChannelOrder(result);
      setSettleOrder(undefined);
      settleForm.resetFields();
      message.success(
        result.qrPayload
          ? "付款码已生成，等待顾客支付"
          : "渠道订单已创建，请查询支付状态",
      );
      await refresh();
    },
    onError,
  });
  const queryChannel = useMutation({
    mutationFn: (item: PaymentChannelOrder) =>
      apiRequest<PaymentChannelOrder>(
        `/api/v1/payment-channels/orders/${item.id}/query`,
        { method: "POST", body: JSON.stringify({ storeId }) },
      ),
    onSuccess: async (result) => {
      setChannelOrder((previous) => {
        if (result.status === "Paid" && previous?.status !== "Paid")
          message.success("渠道已确认收款，消费单结算完成");
        return result;
      });
      if (result.status === "Paid") await refresh();
    },
    onError,
  });
  const closeChannel = useMutation({
    mutationFn: (item: PaymentChannelOrder) =>
      apiRequest<PaymentChannelOrder>(
        `/api/v1/payment-channels/orders/${item.id}/close`,
        { method: "POST", body: JSON.stringify({ storeId }) },
      ),
    onSuccess: async (result) => {
      setChannelOrder(result);
      message.success(
        result.status === "Paid"
          ? "查单发现已支付，消费单已结算"
          : "渠道已确认关单，可重新选择收款方式",
      );
      await refresh();
    },
    onError,
  });
  const issueVerification = useMutation({
    mutationFn: ({
      orderId,
      memberAmountMinor,
      fullMobile,
    }: {
      orderId: string;
      memberAmountMinor: number;
      fullMobile: string;
    }) =>
      apiRequest<MemberVerification>("/api/v1/member-verifications", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          orderId,
          memberAmountMinor,
          fullMobile,
        }),
      }),
    onSuccess: (result) => {
      setMemberVerification(result);
      message.success(
        result.developmentCode
          ? `本地测试验证码：${result.developmentCode}`
          : `验证码已发送至 ${result.maskedMobile}`,
      );
    },
    onError,
  });
  const verifyMemberCode = useMutation({
    mutationFn: ({
      challengeId,
      code,
    }: {
      challengeId: string;
      code: string;
    }) =>
      apiRequest<MemberVerification>(
        `/api/v1/member-verifications/${challengeId}/verify`,
        { method: "POST", body: JSON.stringify({ storeId, code }) },
      ),
    onSuccess: (result) => {
      setMemberVerification(result);
      message.success("会员验证码核验通过");
    },
    onError,
  });
  const openShift = useMutation({
    mutationFn: (values: ShiftValues) =>
      apiRequest<CashierShift>("/api/v1/payments/shifts/open", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          openingCashMinor: Math.round(values.amountYuan * 100),
          commandId: commandId(),
        }),
      }),
    onSuccess: async () => {
      message.success("班次已开始");
      setShiftAction(undefined);
      shiftForm.resetFields();
      await refresh();
      if (settleAfterShiftOpen) {
        beginSettlement(settleAfterShiftOpen);
        setSettleAfterShiftOpen(undefined);
      }
    },
    onError,
  });
  const submitShift = useMutation({
    mutationFn: (values: ShiftValues) =>
      apiRequest<CashierShift>(
        `/api/v1/payments/shifts/${currentShift.data?.id}/submit`,
        {
          method: "POST",
          body: JSON.stringify({
            storeId,
            expectedVersion: currentShift.data?.version,
            submittedCashMinor: Math.round(values.amountYuan * 100),
            note: values.note,
            commandId: commandId(),
          }),
        },
      ),
    onSuccess: async (shift) => {
      message.success(
        shift.status === "Closed"
          ? "账实一致且没有外部待核对，班次已自动关闭"
          : "交班已提交，存在差额或外部待核对，等待独立复核",
      );
      setShiftAction(undefined);
      shiftForm.resetFields();
      await refresh();
    },
    onError,
  });
  const review = useMutation({
    mutationFn: ({
      item,
      values,
    }: {
      item: CashierShiftReview;
      values: ReviewValues;
    }) =>
      apiRequest<CashierShift>(
        `/api/v1/payments/shifts/${item.shift.id}/review`,
        {
          method: "POST",
          body: JSON.stringify({
            storeId,
            expectedVersion: item.shift.version,
            reason: values.reason,
            commandId: commandId(),
          }),
        },
      ),
    onSuccess: async () => {
      message.success("交班已独立复核并关闭");
      setReviewShift(undefined);
      reviewForm.resetFields();
      await refresh();
    },
    onError,
  });
  const requestRefund = useMutation({
    mutationFn: ({
      payment,
      values,
    }: {
      payment: Payment;
      values: RefundValues;
    }) =>
      apiRequest<Refund>("/api/v1/refunds", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          paymentId: payment.id,
          expectedPaymentVersion: payment.version,
          reason: values.reason,
          commandId: commandId(),
          lines: values.lines
            .filter((line) => line.amountYuan > 0)
            .map((line) => ({
              originalAllocationId: line.originalAllocationId,
              amountMinor: Math.round(line.amountYuan * 100),
            })),
        }),
      }),
    onSuccess: async () => {
      message.success("退款申请已提交，等待最高权限审批");
      setRefundPayment(undefined);
      refundForm.resetFields();
      await refresh();
    },
    onError,
  });
  const approveRefund = useMutation({
    mutationFn: (refund: Refund) =>
      apiRequest<Refund>(`/api/v1/refunds/${refund.id}/approve`, {
        method: "POST",
        body: JSON.stringify({
          storeId,
          expectedVersion: refund.version,
          commandId: commandId(),
        }),
      }),
    onSuccess: async (result) => {
      message.success(
        result.status === "Processing"
          ? "退款已安全提交原支付渠道，等待渠道最终确认"
          : "退款或冲正已批准，并完成反向流水",
      );
      await Promise.all([
        refresh(),
        queryClient.invalidateQueries({ queryKey: ["customer"] }),
        queryClient.invalidateQueries({ queryKey: ["member-topups"] }),
      ]);
      if (selectedId)
        await queryClient.invalidateQueries({
          queryKey: ["cashier-order", storeId, selectedId],
        });
    },
    onError,
  });
  const rejectRefundMutation = useMutation({
    mutationFn: ({
      refund,
      values,
    }: {
      refund: Refund;
      values: RejectRefundValues;
    }) =>
      apiRequest<Refund>(`/api/v1/refunds/${refund.id}/reject`, {
        method: "POST",
        body: JSON.stringify({
          storeId,
          expectedVersion: refund.version,
          reason: values.reason,
          commandId: commandId(),
        }),
      }),
    onSuccess: async () => {
      message.success("退款申请已拒绝，可退额度已释放");
      setRejectRefund(undefined);
      rejectRefundForm.resetFields();
      await refresh();
    },
    onError,
  });
  const queryChannelRefund = useMutation({
    mutationFn: (refund: Refund) =>
      apiRequest<Refund>(`/api/v1/refunds/${refund.id}/channel/query`, {
        method: "POST",
        body: JSON.stringify({ storeId }),
      }),
    onSuccess: async (result) => {
      message.success(
        result.status === "Completed"
          ? "渠道已确认退款成功，本地反向流水已完成"
          : result.channelRefund?.status === "Failed"
            ? "渠道返回退款失败，可核对原因后使用同一退款单安全重试"
            : "渠道退款仍在处理中，本地账务未提前变更",
      );
      await refresh();
      if (selectedId)
        await queryClient.invalidateQueries({
          queryKey: ["cashier-order", storeId, selectedId],
        });
    },
    onError,
  });
  const retryChannelRefund = useMutation({
    mutationFn: (refund: Refund) =>
      apiRequest<Refund>(`/api/v1/refunds/${refund.id}/channel/retry`, {
        method: "POST",
        body: JSON.stringify({ storeId }),
      }),
    onSuccess: async (result) => {
      message.success(
        result.status === "Completed"
          ? "渠道已确认退款成功，本地反向流水已完成"
          : "已使用原商户退款单号安全重试，等待渠道最终确认",
      );
      await refresh();
      if (selectedId)
        await queryClient.invalidateQueries({
          queryKey: ["cashier-order", storeId, selectedId],
        });
    },
    onError,
  });
  const updatePricePolicy = useMutation({
    mutationFn: (values: PricePolicyValues) =>
      apiRequest<PriceOverridePolicy>("/api/v1/cashier/price-policy", {
        method: "PUT",
        body: JSON.stringify({
          storeId,
          managerLineDiscountBasisPoints: Math.round(
            values.managerLineDiscountPercent * 100,
          ),
          managerOrderDiscountMinor: Math.round(
            values.managerOrderDiscountYuan * 100,
          ),
          allowManagerPriceIncrease: values.allowManagerPriceIncrease,
          expectedVersion: pricePolicy.data?.version,
          commandId: commandId(),
        }),
      }),
    onSuccess: async () => {
      message.success("新改价策略已发布；历史订单继续使用原策略快照");
      setPricePolicyOpen(false);
      await queryClient.invalidateQueries({
        queryKey: ["price-override-policy"],
      });
    },
    onError,
  });
  const approvePrice = useMutation({
    mutationFn: (approval: PriceOverrideApproval) =>
      apiRequest<PriceOverrideApproval>(
        `/api/v1/cashier/price-approvals/${approval.id}/approve`,
        {
          method: "POST",
          body: JSON.stringify({
            storeId,
            expectedVersion: approval.version,
            note: "最高权限已核对成交金额与改价原因",
            commandId: commandId(),
          }),
        },
      ),
    onSuccess: async (result) => {
      message.success("改价已批准，消费单现在可以确认金额");
      await refresh();
      await queryClient.invalidateQueries({
        queryKey: ["cashier-order", storeId, result.serviceOrderId],
      });
    },
    onError,
  });
  const rejectPrice = useMutation({
    mutationFn: ({
      approval,
      values,
    }: {
      approval: PriceOverrideApproval;
      values: PriceApprovalValues;
    }) =>
      apiRequest<PriceOverrideApproval>(
        `/api/v1/cashier/price-approvals/${approval.id}/reject`,
        {
          method: "POST",
          body: JSON.stringify({
            storeId,
            expectedVersion: approval.version,
            note: values.note,
            commandId: commandId(),
          }),
        },
      ),
    onSuccess: async (result) => {
      message.success(
        "改价已驳回；原消费单不能确认，可作废后按正确金额重新录入",
      );
      setRejectPriceApproval(undefined);
      priceApprovalForm.resetFields();
      await refresh();
      await queryClient.invalidateQueries({
        queryKey: ["cashier-order", storeId, result.serviceOrderId],
      });
    },
    onError,
  });
  const openPricePolicy = () => {
    if (!pricePolicy.data) return;
    pricePolicyForm.setFieldsValue({
      managerLineDiscountPercent:
        pricePolicy.data.managerLineDiscountBasisPoints / 100,
      managerOrderDiscountYuan:
        pricePolicy.data.managerOrderDiscountMinor / 100,
      allowManagerPriceIncrease: pricePolicy.data.allowManagerPriceIncrease,
    });
    setPricePolicyOpen(true);
  };
  const openCreate = () => {
    const first = publishedBook?.lines[0];
    const firstProduct = publishedBook?.productLines[0];
    form.resetFields();
    form.setFieldsValue({
      source: "standalone",
      lines: first
        ? [
            {
              lineType: "Service",
              serviceItemId: first.serviceItemId,
              quantity: 1,
              enteredPriceYuan: first.unitPriceMinor / 100,
            },
          ]
        : firstProduct
          ? [
              {
                lineType: "Product",
                productItemId: firstProduct.productItemId,
                quantity: 1,
                enteredPriceYuan: firstProduct.unitPriceMinor / 100,
              },
            ]
          : [],
    });
    setCreateOpen(true);
  };
  const selectedVisitId = Form.useWatch("visitId", form);
  const selectedVisit = pendingVisits.data?.find(
    (visit) => visit.id === selectedVisitId,
  );
  const applyPlannedService = () => {
    if (!selectedVisit?.plannedServiceItemId) return;
    const price = publishedBook?.lines.find(
      (line) => line.serviceItemId === selectedVisit.plannedServiceItemId,
    );
    if (!price) {
      message.warning("该预计服务不在当前已发布价目中，请手动选择实际项目");
      return;
    }
    form.setFieldValue("lines", [
      {
        lineType: "Service",
        serviceItemId: price.serviceItemId,
        quantity: 1,
        enteredPriceYuan: price.unitPriceMinor / 100,
      },
    ]);
    message.success("已带入为可编辑明细，保存前仍可更换项目和金额");
  };
  const selectedPayment = payments.data?.find(
    (payment) => payment.orderId === selectedId,
  );
  const selectedRefunds =
    refunds.data?.filter(
      (refund) => refund.paymentId === selectedPayment?.id,
    ) ?? [];
  const reservedByAllocation = (refunds.data ?? [])
    .filter((refund) => refund.status !== "Rejected")
    .flatMap((refund) => refund.lines)
    .reduce<Record<string, number>>((result, line) => {
      result[line.originalAllocationId] =
        (result[line.originalAllocationId] ?? 0) + line.amountMinor;
      return result;
    }, {});
  const beginRefund = (payment: Payment) => {
    const lines = payment.allocations
      .filter((line) => line.category !== "ManualExternal")
      .map((line) => ({
        originalAllocationId: line.id,
        amountYuan:
          Math.max(0, line.amountMinor - (reservedByAllocation[line.id] ?? 0)) /
          100,
      }));
    refundForm.setFieldsValue({ reason: "", lines });
    setRefundPayment(payment);
  };
  const shiftPendingExternal =
    currentShift.data?.status === "Open"
      ? (payments.data
          ?.flatMap((payment) => payment.allocations)
          .filter(
            (line) =>
              line.shiftId === currentShift.data?.id &&
              line.reconciliationStatus === "Pending",
          )
          .reduce((sum, line) => sum + line.amountMinor, 0) ?? 0)
      : (currentShift.data?.pendingReconciliationMinor ?? 0);
  const beginSettlement = (order: ServiceOrder) => {
    const first =
      paymentMethods.data?.find((method) => method.code === "CASH") ??
      paymentMethods.data?.[0];
    if (!first) return;
    setMemberVerification(undefined);
    settleForm.setFieldsValue({
      allocations: [
        { methodId: first.id, amountYuan: order.receivableMinor / 100 },
      ],
      cashTenderedYuan:
        first.category === "Cash" ? order.receivableMinor / 100 : undefined,
    });
    setSettleOrder(order);
  };
  const submitSettlement = (values: SettleValues) => {
    if (!settleOrder) return;
    const channelLine = values.allocations.find(
      (line) =>
        paymentMethods.data?.find((method) => method.id === line.methodId)
          ?.category === "ChannelExternal",
    );
    if (channelLine)
      initiateChannel.mutate({
        order: settleOrder,
        methodId: channelLine.methodId,
      });
    else settle.mutate({ order: settleOrder, values });
  };
  const settleAllocations = Form.useWatch("allocations", settleForm) ?? [];
  const cashTenderedYuan = Form.useWatch("cashTenderedYuan", settleForm);
  const cashAmountMinor = calculateCashAmountMinor(
    settleAllocations,
    paymentMethods.data ?? [],
  );
  const verifiedMobile = Form.useWatch("verifiedMobile", settleForm);
  const memberAmountMinor = settleAllocations.reduce(
    (sum, line) =>
      paymentMethods.data?.find((method) => method.id === line?.methodId)
        ?.category === "InternalAccount"
        ? sum + Math.round(Number(line?.amountYuan ?? 0) * 100)
        : sum,
    0,
  );
  const hasRealChannelMethod = paymentMethods.data?.some(
    (method) => method.category === "ChannelExternal",
  );
  const hasManualExternalMethod = paymentMethods.data?.some(
    (method) => method.category === "ManualExternal",
  );
  const memberAccounts: SettlementMemberAccount[] =
    settlementCustomer.data?.cards.flatMap((card) =>
      card.accounts.map((account) => ({
        ...account,
        cardLabel: card.maskedCardNo,
      })),
    ) ?? [];
  const queryChannelMutate = queryChannel.mutate;
  const queryChannelPending = queryChannel.isPending;

  useEffect(() => {
    if (!channelOrder || !["Created", "QrReady"].includes(channelOrder.status))
      return;
    const timer = window.setInterval(() => {
      if (!queryChannelPending) queryChannelMutate(channelOrder);
    }, 3000);
    return () => window.clearInterval(timer);
  }, [channelOrder, queryChannelMutate, queryChannelPending]);

  const customerDisplay = (customerId?: string) =>
    customerId
      ? (customers.data?.find((item) => item.id === customerId)?.displayName ??
        "已关联顾客")
      : "匿名顾客";
  const columns = [
    {
      title: "顾客 / 消费内容",
      key: "recognizable",
      render: (_: unknown, order: ServiceOrder) => (
        <Space orientation="vertical" size={0}>
          <strong>
            {customerDisplay(order.customerId)} ·{" "}
            {order.lines
              .map((line) => line.itemName)
              .slice(0, 2)
              .join("、")}
            {order.lines.length > 2 ? `等${order.lines.length}项` : ""}
          </strong>
          <Typography.Text type="secondary">
            追溯单号 {order.orderNo}
          </Typography.Text>
        </Space>
      ),
    },
    {
      title: "状态",
      key: "status",
      render: (_: unknown, order: ServiceOrder) => {
        const meta = statusMeta[order.status] ?? {
          label: order.status,
          color: "default",
        };
        const priceMeta =
          priceAuthorizationMeta[order.priceAuthorizationStatus];
        return (
          <Space orientation="vertical" size={2}>
            <Tag color={meta.color}>{meta.label}</Tag>
            {order.priceAuthorizationStatus !== "NotRequired" && (
              <Tag color={priceMeta?.color ?? "default"}>
                {priceMeta?.label ?? order.priceAuthorizationStatus}
              </Tag>
            )}
          </Space>
        );
      },
    },
    {
      title: "标准价合计",
      dataIndex: "referenceAmountMinor",
      align: "right" as const,
      render: money,
    },
    {
      title: "应收金额",
      dataIndex: "receivableMinor",
      align: "right" as const,
      render: (value: number) => <strong>{money(value)}</strong>,
    },
    {
      title: "录单时间",
      dataIndex: "createdAtUtc",
      render: (value: string) =>
        new Date(value).toLocaleString("zh-CN", { hour12: false }),
    },
    {
      title: "操作",
      key: "action",
      width: 90,
      render: (_: unknown, order: ServiceOrder) => (
        <Button
          size="small"
          onClick={(event) => {
            event.stopPropagation();
            setSelectedId(order.id);
          }}
        >
          查看
        </Button>
      ),
    },
  ];
  const reviewColumns = [
    {
      title: "班次",
      dataIndex: ["shift", "shiftNo"],
      render: (value: string) => <strong>{value}</strong>,
    },
    { title: "收银员", dataIndex: "operatorDisplayName" },
    {
      title: "状态",
      dataIndex: ["shift", "status"],
      render: (value: string) => (
        <Tag
          color={
            value === "ReviewPending"
              ? "gold"
              : value === "Closed"
                ? "default"
                : "green"
          }
        >
          {value === "ReviewPending"
            ? "待复核"
            : value === "Closed"
              ? "已关闭"
              : "当班中"}
        </Tag>
      ),
    },
    {
      title: "理论现金（仅现金）",
      dataIndex: ["shift", "expectedCashMinor"],
      align: "right" as const,
      render: (value?: number) =>
        value === undefined || value === null ? "—" : money(value),
    },
    {
      title: "现金差额",
      dataIndex: ["shift", "cashDifferenceMinor"],
      align: "right" as const,
      render: (value?: number) =>
        value === undefined || value === null ? (
          "—"
        ) : (
          <Typography.Text type={value === 0 ? "success" : "danger"}>
            {money(value)}
          </Typography.Text>
        ),
    },
    {
      title: "人工外部收款待核对",
      dataIndex: ["shift", "pendingReconciliationMinor"],
      align: "right" as const,
      render: (value?: number) =>
        value === undefined || value === null ? "—" : money(value),
    },
    {
      title: "操作",
      key: "action",
      width: 110,
      render: (_: unknown, item: CashierShiftReview) =>
        item.shift.status === "ReviewPending" ? (
          <Button
            size="small"
            type="primary"
            icon={<CheckCircleOutlined />}
            disabled={
              !canActivateShiftReview(
                item.shift.operatorId,
                auth.user?.id,
                isOwner,
              )
            }
            onClick={(event) => {
              event.stopPropagation();
              reviewForm.setFieldsValue({
                reason: item.shift.pendingReconciliationMinor
                  ? "已核对交班数据；外部渠道款项继续保留待核对状态。"
                  : undefined,
              });
              setReviewShift(item);
            }}
          >
            复核
          </Button>
        ) : null,
    },
  ];

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <Typography.Title level={2}>服务录单与收银</Typography.Title>
          <Typography.Paragraph>
            店长按实际服务内容和成交金额录单；设施占用时长只作接待参考。
          </Typography.Paragraph>
        </div>
        <Space>
          {isOwner && (
            <Button
              icon={<SafetyCertificateOutlined />}
              loading={pricePolicy.isLoading}
              onClick={openPricePolicy}
            >
              改价策略
            </Button>
          )}
          <Button
            icon={<WalletOutlined />}
            disabled={currentShift.data?.status === "ReviewPending"}
            onClick={() => {
              shiftForm.setFieldsValue({
                amountYuan: currentShift.data?.expectedCashMinor
                  ? currentShift.data.expectedCashMinor / 100
                  : 0,
              });
              setShiftAction(
                currentShift.data?.status === "Open" ? "submit" : "open",
              );
            }}
          >
            {currentShift.data?.status === "Open"
              ? "提交交班"
              : currentShift.data?.status === "ReviewPending"
                ? "交班待复核"
                : "开班"}
          </Button>
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={openCreate}
            disabled={!publishedBook}
          >
            新建消费单
          </Button>
        </Space>
      </div>
      <Alert
        type="info"
        showIcon
        title="已配置并启用的微信/支付宝会生成渠道付款码；只有验签回调或主动查单确认成功后才完成结算。人工登记仍保持待核对。"
      />
      <Alert
        type="warning"
        showIcon
        title="会员消费固定先扣储值本金；只有同卡本金已扣完时，才允许使用赠送奖励。"
      />
      {pricePolicy.data && (
        <Alert
          type="info"
          showIcon
          title={`当前改价策略 V${pricePolicy.data.policyVersion}：店长单行最多优惠 ${(pricePolicy.data.managerLineDiscountBasisPoints / 100).toFixed(2)}%，整单最多优惠 ${money(pricePolicy.data.managerOrderDiscountMinor)}；${pricePolicy.data.allowManagerPriceIncrease ? "允许店长直接提价" : "提价必须由最高权限审批"}。收银员的任何改价均需审批。`}
        />
      )}
      <Segmented
        block
        value={workbenchView}
        onChange={(value) => setWorkbenchView(value as typeof workbenchView)}
        options={[
          { value: "today", label: "今日收银" },
          {
            value: "pending",
            label: `待处理（${(priceApprovals.data?.length ?? 0) + (refunds.data?.filter((item) => ["PendingApproval", "Processing"].includes(item.status)).length ?? 0)}）`,
          },
          {
            value: "review",
            label: `交班复核（${shiftReviews.data?.filter((item) => item.shift.status === "ReviewPending").length ?? 0}）`,
          },
        ]}
      />
      {workbenchView === "today" && (
        <>
          <Card variant="borderless" className="shift-strip">
            {currentShift.data ? (
              <div>
                <div>
                  <Typography.Text type="secondary">当前班次</Typography.Text>
                  <strong>{currentShift.data.shiftNo}</strong>
                </div>
                <div>
                  <Typography.Text type="secondary">状态</Typography.Text>
                  <Tag
                    color={
                      currentShift.data.status === "Open" ? "green" : "gold"
                    }
                  >
                    {currentShift.data.status === "Open" ? "当班中" : "待复核"}
                  </Tag>
                </div>
                <div>
                  <Typography.Text type="secondary">备用金</Typography.Text>
                  <strong>{money(currentShift.data.openingCashMinor)}</strong>
                </div>
                <div>
                  <Typography.Text type="secondary">外部待核对</Typography.Text>
                  <strong>{money(shiftPendingExternal)}</strong>
                </div>
              </div>
            ) : (
              <div className="shift-empty">
                <Typography.Text>
                  尚未开班。现金和人工外部收款必须先归入班次。
                </Typography.Text>
                <Button
                  type="primary"
                  onClick={() => {
                    shiftForm.setFieldsValue({ amountYuan: 0 });
                    setShiftAction("open");
                  }}
                >
                  立即开班
                </Button>
              </div>
            )}
          </Card>
        </>
      )}
      {workbenchView === "review" &&
        (canReviewShifts ? (
          <Card
            variant="borderless"
            title="交班复核"
            extra={
              <Typography.Text type="secondary">
                复核只关闭班次，不把外部待核对款项改成渠道到账
              </Typography.Text>
            }
          >
            <Table<CashierShiftReview>
              rowKey={(item) => item.shift.id}
              size="small"
              columns={reviewColumns}
              dataSource={shiftReviews.data?.filter(
                (item) => item.shift.status !== "Open",
              )}
              loading={shiftReviews.isLoading}
              pagination={{ pageSize: 10 }}
              locale={{
                emptyText: <Empty description="没有待复核或已关闭班次" />,
              }}
            />
          </Card>
        ) : (
          <Alert type="info" showIcon title="当前账号没有交班复核权限。" />
        ))}
      {workbenchView === "pending" && (
        <>
          {isOwner && (
            <Card
              variant="borderless"
              title="改价审批"
              extra={
                <Typography.Text type="secondary">
                  批准只授权当前金额快照，不会替申请人修改价格
                </Typography.Text>
              }
            >
              <Space orientation="vertical" className="full-width">
                {priceApprovals.data?.map((approval) => (
                  <Card key={approval.id} size="small">
                    <div className="refund-review-row">
                      <div>
                        <Space>
                          <strong>{approval.orderNo}</strong>
                          <Tag
                            color={
                              approval.differenceMinor > 0 ? "red" : "gold"
                            }
                          >
                            {approval.differenceMinor > 0
                              ? `提价 ${money(approval.differenceMinor)}`
                              : `优惠 ${money(-approval.differenceMinor)}`}
                          </Tag>
                          <Tag>
                            {approval.requesterName} · {approval.requesterRole}
                          </Tag>
                        </Space>
                        <Typography.Text type="secondary">
                          标准 {money(approval.referenceAmountMinor)} → 应收{" "}
                          {money(approval.receivableMinor)}；单行最高优惠{" "}
                          {(
                            approval.maximumLineDiscountBasisPoints / 100
                          ).toFixed(2)}
                          %；策略 V{approval.policyVersion}
                        </Typography.Text>
                      </div>
                      <Space>
                        <Button
                          danger
                          onClick={() => {
                            priceApprovalForm.resetFields();
                            setRejectPriceApproval(approval);
                          }}
                        >
                          驳回
                        </Button>
                        <Button
                          type="primary"
                          loading={approvePrice.isPending}
                          onClick={() => approvePrice.mutate(approval)}
                        >
                          批准改价
                        </Button>
                      </Space>
                    </div>
                  </Card>
                ))}
                {!priceApprovals.isLoading && !priceApprovals.data?.length && (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description="没有待审批改价"
                  />
                )}
              </Space>
            </Card>
          )}
          {canReviewShifts && (
            <Card
              variant="borderless"
              title="退款审批与渠道处理"
              extra={
                <Typography.Text type="secondary">
                  渠道退款只有确认成功后才更新本地账务
                </Typography.Text>
              }
            >
              <Space orientation="vertical" className="full-width">
                {refunds.data
                  ?.filter((refund) =>
                    ["PendingApproval", "Processing"].includes(refund.status),
                  )
                  .map((refund) => (
                    <Card key={refund.id} size="small">
                      <div className="refund-review-row">
                        <div>
                          <Space>
                            <strong>{refund.refundNo}</strong>
                            <Tag
                              color={
                                refund.businessType === "MemberTopup"
                                  ? "purple"
                                  : "blue"
                              }
                            >
                              {refund.businessType === "MemberTopup"
                                ? "储值整单冲正"
                                : "消费退款"}
                            </Tag>
                            {refund.status === "Processing" && (
                              <Tag
                                color={
                                  refund.channelRefund?.status === "Failed"
                                    ? "red"
                                    : "processing"
                                }
                              >
                                {refund.channelRefund?.status === "Failed"
                                  ? "渠道退款失败"
                                  : "渠道处理中"}
                              </Tag>
                            )}
                          </Space>
                          <Typography.Text type="secondary">
                            {money(refund.amountMinor)} · {refund.reason}
                          </Typography.Text>
                          {refund.channelRefund?.failureCode && (
                            <Typography.Text type="danger">
                              渠道错误：{refund.channelRefund.failureCode}
                            </Typography.Text>
                          )}
                        </div>
                        {refund.status === "PendingApproval" ? (
                          canApproveRefunds ? (
                            <Space>
                              <Button
                                danger
                                onClick={() => {
                                  rejectRefundForm.resetFields();
                                  setRejectRefund(refund);
                                }}
                              >
                                拒绝
                              </Button>
                              <Button
                                type="primary"
                                loading={approveRefund.isPending}
                                onClick={() => approveRefund.mutate(refund)}
                              >
                                批准并执行
                              </Button>
                            </Space>
                          ) : (
                            <Tag color="gold">等待最高权限审批</Tag>
                          )
                        ) : (
                          <Space>
                            <Button
                              loading={queryChannelRefund.isPending}
                              onClick={() => queryChannelRefund.mutate(refund)}
                            >
                              查询渠道结果
                            </Button>
                            {canApproveRefunds &&
                              refund.channelRefund?.status === "Failed" && (
                                <Button
                                  danger
                                  loading={retryChannelRefund.isPending}
                                  onClick={() =>
                                    retryChannelRefund.mutate(refund)
                                  }
                                >
                                  使用原退款单号重试
                                </Button>
                              )}
                          </Space>
                        )}
                      </div>
                    </Card>
                  ))}
                {!refunds.isLoading &&
                  !refunds.data?.some((refund) =>
                    ["PendingApproval", "Processing"].includes(refund.status),
                  ) && (
                    <Empty
                      image={Empty.PRESENTED_IMAGE_SIMPLE}
                      description="没有待审批或处理中的退款"
                    />
                  )}
              </Space>
            </Card>
          )}
          {!isOwner && !canReviewShifts && (
            <Alert
              type="info"
              showIcon
              title="当前账号没有审批任务；待处理事项会由店长或最高权限账号完成。"
            />
          )}
        </>
      )}
      {workbenchView === "today" && (
        <>
          <div className="cashier-metrics">
            <Card variant="borderless">
              <Statistic
                title="待录单接待"
                value={pendingVisits.data?.length ?? 0}
                suffix="单"
              />
            </Card>
            <Card variant="borderless">
              <Statistic
                title="本页待确认金额"
                value={
                  (orders.data?.items ?? []).filter(
                    (order) => order.status === "Draft",
                  ).length
                }
                suffix="单"
              />
            </Card>
            <Card variant="borderless">
              <Statistic
                title="本页待支付"
                value={
                  (orders.data?.items ?? []).filter(
                    (order) => order.status === "PendingPayment",
                  ).length
                }
                suffix="单"
              />
            </Card>
          </div>
          {!publishedBook && !priceBooks.isLoading && (
            <Alert
              type="warning"
              showIcon
              title="当前没有已发布价目表，请先由最高权限账号发布价格。"
            />
          )}
          <Card
            variant="borderless"
            title="消费单查询"
            extra={
              <Typography.Text type="secondary">
                输入关键词或变更筛选后自动加载
              </Typography.Text>
            }
          >
            <Space wrap align="end">
              <Input
                value={orderKeyword}
                onChange={(event) => setOrderKeyword(event.target.value)}
                allowClear
                maxLength={100}
                placeholder="单号、顾客、项目、员工或备注"
                style={{ width: 260 }}
              />
              <Select
                allowClear
                value={orderFilters.customerId}
                onChange={(value) =>
                  setOrderFilters((current) => ({
                    ...current,
                    customerId: value,
                  }))
                }
                placeholder="全部顾客"
                showSearch
                optionFilterProp="label"
                style={{ width: 190 }}
                options={customers.data?.map((customer) => ({
                  value: customer.id,
                  label: `${customer.displayName} · ${customer.maskedMobile}`,
                }))}
              />
              <Select
                allowClear
                value={orderFilters.catalogItemId}
                onChange={(value) =>
                  setOrderFilters((current) => ({
                    ...current,
                    catalogItemId: value,
                  }))
                }
                placeholder="全部项目/产品"
                showSearch
                optionFilterProp="label"
                style={{ width: 190 }}
                options={[
                  ...(publishedBook?.lines.map((line) => ({
                    value: line.serviceItemId,
                    label: `服务 · ${line.serviceItemName}`,
                  })) ?? []),
                  ...(publishedBook?.productLines.map((line) => ({
                    value: line.productItemId,
                    label: `产品 · ${line.productItemName}`,
                  })) ?? []),
                ]}
              />
              <Select
                allowClear
                value={orderFilters.employeeId}
                onChange={(value) =>
                  setOrderFilters((current) => ({
                    ...current,
                    employeeId: value,
                  }))
                }
                placeholder="全部服务员工"
                style={{ width: 170 }}
                options={serviceEmployees.data?.map((employee) => ({
                  value: employee.id,
                  label: `${employee.displayName} · ${employee.employeeNo}`,
                }))}
              />
              <Select
                allowClear
                value={orderFilters.status}
                onChange={(value) =>
                  setOrderFilters((current) => ({ ...current, status: value }))
                }
                placeholder="全部状态"
                style={{ width: 150 }}
                options={Object.entries(statusMeta).map(([value, meta]) => ({
                  value,
                  label: meta.label,
                }))}
              />
              <Input
                type="date"
                aria-label="消费单开始日期"
                value={orderFilters.fromDate}
                onChange={(event) =>
                  setOrderFilters((current) => ({
                    ...current,
                    fromDate: event.target.value || undefined,
                  }))
                }
              />
              <Input
                type="date"
                aria-label="消费单结束日期"
                value={orderFilters.toDate}
                min={orderFilters.fromDate}
                onChange={(event) =>
                  setOrderFilters((current) => ({
                    ...current,
                    toDate: event.target.value || undefined,
                  }))
                }
              />
              <Button
                onClick={() => {
                  setOrderKeyword("");
                  setOrderFilters({});
                }}
              >
                重置
              </Button>
            </Space>
          </Card>
          <Card variant="borderless" className="table-card">
            <Table<ServiceOrder>
              rowKey="id"
              columns={columns}
              dataSource={orders.data?.items}
              loading={orders.isLoading}
              pagination={{
                current: orderPage,
                pageSize: orderPageSize,
                total: orders.data?.total ?? 0,
                showSizeChanger: false,
                showTotal: (total) => `共 ${total} 单`,
                onChange: setOrderPage,
              }}
              locale={{ emptyText: <Empty description="还没有消费单" /> }}
              onRow={(record) => ({
                onClick: () => setSelectedId(record.id),
                className: "clickable-row",
              })}
            />
          </Card>
        </>
      )}

      <Modal
        title="新建消费单"
        width={900}
        open={createOpen}
        onCancel={() => {
          setCreateOpen(false);
          form.resetFields();
        }}
        onOk={() => form.submit()}
        okText="保存草稿"
        confirmLoading={create.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="成交价不同于标准价时必须填写原因。系统会按当前角色和策略自动判断直接授权或提交最高权限审批；待审批订单不能确认或收款。"
          className="modal-alert"
        />
        <Form<OrderValues>
          form={form}
          layout="vertical"
          onFinish={(values) => create.mutate(values)}
        >
          <Form.Item
            name="source"
            label="录单来源"
            rules={[{ required: true }]}
          >
            <Select
              options={[
                { value: "standalone", label: "服务结束后直接补录" },
                { value: "visit", label: "从已结束的设施接待录入" },
              ]}
              onChange={(value) => {
                if (value === "standalone")
                  form.setFieldValue("visitId", undefined);
              }}
            />
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(before, after) => before.source !== after.source}
          >
            {({ getFieldValue }) =>
              getFieldValue("source") === "visit" ? (
                <>
                  <Form.Item
                    name="visitId"
                    label="已结束接待"
                    rules={[{ required: true, message: "请选择接待记录" }]}
                  >
                    <Select
                      showSearch
                      optionFilterProp="title"
                      placeholder="按顾客、服务、设施和时间选择"
                      optionLabelProp="title"
                      onChange={(value: string) => {
                        const visit = pendingVisits.data?.find(
                          (item) => item.id === value,
                        );
                        form.setFieldValue("customerId", visit?.customerId);
                      }}
                      options={pendingVisits.data?.map((visit) => {
                        const arrived = new Date(
                          visit.arrivedAtUtc,
                        ).toLocaleString("zh-CN", {
                          month: "2-digit",
                          day: "2-digit",
                          hour: "2-digit",
                          minute: "2-digit",
                          hour12: false,
                        });
                        const primary = `${visit.customerDisplayName} · ${visit.plannedServiceItemName ?? "未填预计服务"}`;
                        const context = `${visit.facilityNames || "未关联设施"} · ${arrived}到店 · 占用${duration(visit.facilitySeconds)}`;
                        return {
                          value: visit.id,
                          title: `${primary} · ${context}`,
                          label: (
                            <div>
                              <strong>{primary}</strong>
                              <div>
                                <Typography.Text type="secondary">
                                  {context} · 追溯编号 {visit.visitNo}
                                </Typography.Text>
                              </div>
                            </div>
                          ),
                        };
                      })}
                    />
                  </Form.Item>
                  {selectedVisit && (
                    <Alert
                      type="info"
                      showIcon
                      title={`${selectedVisit.customerDisplayName} · ${selectedVisit.plannedServiceItemName ?? "未填预计服务"} · ${selectedVisit.facilityNames || "未关联设施"}`}
                      description={`设施累计占用 ${duration(selectedVisit.facilitySeconds)}；追溯编号 ${selectedVisit.visitNo}。设施时长和预计服务都不会自动形成费用。`}
                      action={
                        selectedVisit.plannedServiceItemId ? (
                          <Button size="small" onClick={applyPlannedService}>
                            带入预计服务
                          </Button>
                        ) : undefined
                      }
                      className="modal-alert"
                    />
                  )}
                </>
              ) : null
            }
          </Form.Item>
          <Form.Item name="customerId" label="顾客/会员（可选）">
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="可按顾客档案关联，也可匿名结算"
              options={customers.data?.map((customer) => ({
                value: customer.id,
                label: `${customer.displayName} · ${customer.maskedMobile}`,
              }))}
            />
          </Form.Item>
          <Typography.Title level={5}>服务与商品</Typography.Title>
          <Form.List
            name="lines"
            rules={[
              {
                validator: async (_, lines) => {
                  if (!lines?.length)
                    throw new Error("至少添加一个服务项目或商品");
                },
              },
            ]}
          >
            {(fields, { add, remove }, { errors }) => (
              <>
                <div className="order-line-list">
                  {fields.map((field) => (
                    <OrderLineEditor
                      key={field.key}
                      field={field}
                      form={form}
                      priceBook={publishedBook}
                      products={products.data ?? []}
                      employees={serviceEmployees.data ?? []}
                      onRemove={() => remove(field.name)}
                      removable={fields.length > 1}
                    />
                  ))}
                </div>
                <Space>
                  <Button
                    icon={<PlusOutlined />}
                    onClick={() => {
                      const first = publishedBook?.lines[0];
                      const firstProduct = publishedBook?.productLines[0];
                      add(
                        first
                          ? {
                              lineType: "Service",
                              serviceItemId: first.serviceItemId,
                              quantity: 1,
                              enteredPriceYuan: first.unitPriceMinor / 100,
                            }
                          : firstProduct
                            ? {
                                lineType: "Product",
                                productItemId: firstProduct.productItemId,
                                quantity: 1,
                                enteredPriceYuan:
                                  firstProduct.unitPriceMinor / 100,
                              }
                            : {},
                      );
                    }}
                  >
                    添加项目/商品
                  </Button>
                  <Form.ErrorList errors={errors} />
                </Space>
              </>
            )}
          </Form.List>
          <Form.Item
            name="note"
            label="整单备注（可选）"
            rules={[{ max: 1000 }]}
          >
            <Input.TextArea rows={2} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>

      <Drawer
        title="消费单详情"
        size={680}
        open={Boolean(selectedId)}
        onClose={() => setSelectedId(undefined)}
        extra={
          <Space>
            {selected.data &&
              ["Draft", "PendingPayment"].includes(selected.data.status) && (
                <Button
                  danger
                  onClick={() => {
                    voidForm.resetFields();
                    setVoidOrder(selected.data);
                  }}
                >
                  作废
                </Button>
              )}
            {selected.data?.status === "Draft" && (
              <Button
                type="primary"
                icon={<FileDoneOutlined />}
                loading={confirm.isPending}
                disabled={["PendingApproval", "Rejected", "Cancelled"].includes(
                  selected.data.priceAuthorizationStatus,
                )}
                title={
                  selected.data.priceAuthorizationStatus === "PendingApproval"
                    ? "等待最高权限审批后才能确认"
                    : selected.data.priceAuthorizationStatus === "Rejected"
                      ? "改价已驳回，请作废后重新录入"
                      : undefined
                }
                onClick={() => confirm.mutate(selected.data!)}
              >
                确认金额
              </Button>
            )}
            {selected.data?.status === "PendingPayment" &&
              (currentShift.data?.status === "Open" ? (
                <Button
                  type="primary"
                  icon={<WalletOutlined />}
                  onClick={() => beginSettlement(selected.data!)}
                >
                  收款结算
                </Button>
              ) : (
                <Button
                  type="primary"
                  icon={<WalletOutlined />}
                  onClick={() => {
                    setSettleAfterShiftOpen(selected.data!);
                    shiftForm.setFieldsValue({ amountYuan: 0 });
                    setShiftAction("open");
                  }}
                >
                  开班并结算
                </Button>
              ))}
            {selected.data?.status === "PaymentProcessing" &&
              activeChannelOrder.data && (
                <Button
                  type="primary"
                  onClick={() => setChannelOrder(activeChannelOrder.data)}
                >
                  查看付款码
                </Button>
              )}
            {selectedPayment &&
              ["Paid", "PartiallyRefunded", "Refunded"].includes(
                selectedPayment.status,
              ) && (
                <Button
                  icon={<PrinterOutlined />}
                  loading={printReceipt.isPending}
                  onClick={() => {
                    const popup = window.open(
                      "",
                      "_blank",
                      "width=420,height=720",
                    );
                    if (!popup) {
                      message.warning(
                        "浏览器阻止了打印窗口，请允许本站弹出窗口后重试",
                      );
                      return;
                    }
                    popup.document.body.textContent = "正在生成可追溯小票…";
                    printReceipt.mutate({ payment: selectedPayment, popup });
                  }}
                >
                  打印 / 补打小票
                </Button>
              )}
            {selectedPayment &&
              ["Paid", "PartiallyRefunded"].includes(selectedPayment.status) &&
              selectedPayment.allocations.some(
                (line) =>
                  line.category !== "ManualExternal" &&
                  line.amountMinor > (reservedByAllocation[line.id] ?? 0),
              ) &&
              canRequestRefunds && (
                <Button danger onClick={() => beginRefund(selectedPayment)}>
                  申请退款
                </Button>
              )}
          </Space>
        }
      >
        {selected.error && (
          <Alert
            type="error"
            showIcon
            title={
              selected.error instanceof Error
                ? selected.error.message
                : "详情加载失败"
            }
          />
        )}
        {selected.data && (
          <Space orientation="vertical" size={18} className="full-width">
            <Alert
              type={
                selected.data.priceAuthorizationStatus === "Rejected"
                  ? "error"
                  : selected.data.priceAuthorizationStatus === "PendingApproval"
                    ? "warning"
                    : selected.data.status === "PendingPayment"
                      ? "warning"
                      : ["Settled", "PartiallyRefunded", "Refunded"].includes(
                            selected.data.status,
                          )
                        ? "success"
                        : "info"
              }
              showIcon
              title={
                selected.data.priceAuthorizationStatus === "PendingApproval"
                  ? "本单成交价正在等待最高权限审批；审批通过前不能确认金额或收款。"
                  : selected.data.priceAuthorizationStatus === "Rejected"
                    ? "本单改价已被驳回，不能确认或收款；请作废后按正确金额重新录入。"
                    : selected.data.status === "PendingPayment"
                      ? currentShift.data?.status === "Open"
                        ? "金额已锁定，可按实际收款方式分摊结算。"
                        : "金额已锁定；收款须归入当前账号自己的班次，点上方“开班并结算”即可先开班再收款。"
                      : selected.data.status === "PartiallyRefunded"
                        ? `消费单已部分退款 ${money(selected.data.refundedMinor)}；原支付和反向流水均保留。`
                        : selected.data.status === "Refunded"
                          ? "消费单已全额退款；原支付和反向流水均保留。"
                          : selected.data.status === "Settled"
                            ? "消费单已结算；人工外部支付仍需在交班和财务中持续核对。"
                            : selected.data.status === "Voided"
                              ? "消费单已经作废，仅保留历史金额、原因和审计记录。"
                              : "草稿可核对金额；确认后进入待支付。"
              }
            />
            <Descriptions
              bordered
              size="small"
              column={2}
              items={[
                {
                  key: "no",
                  label: "消费单号",
                  children: selected.data.orderNo,
                },
                {
                  key: "status",
                  label: "状态",
                  children:
                    statusMeta[selected.data.status]?.label ??
                    selected.data.status,
                },
                {
                  key: "priceAuthorization",
                  label: "改价授权",
                  children: (
                    <Tag
                      color={
                        priceAuthorizationMeta[
                          selected.data.priceAuthorizationStatus
                        ]?.color
                      }
                    >
                      {priceAuthorizationMeta[
                        selected.data.priceAuthorizationStatus
                      ]?.label ?? selected.data.priceAuthorizationStatus}
                    </Tag>
                  ),
                },
                {
                  key: "policy",
                  label: "策略快照",
                  children: selected.data.pricePolicyVersion
                    ? `V${selected.data.pricePolicyVersion}`
                    : "标准价无需审批",
                },
                {
                  key: "reference",
                  label: "标准价合计",
                  children: money(selected.data.referenceAmountMinor),
                },
                {
                  key: "receivable",
                  label: "应收金额",
                  children: (
                    <strong>{money(selected.data.receivableMinor)}</strong>
                  ),
                },
                {
                  key: "note",
                  label: "备注",
                  span: 2,
                  children: selected.data.note ?? "无",
                },
              ]}
            />
            <div>
              <Typography.Title level={5}>项目明细与价格快照</Typography.Title>
              {selected.data.lines.map((line) => (
                <Card key={line.id} size="small" className="order-detail-line">
                  <div>
                    <Space>
                      <strong>{line.itemName}</strong>
                      <Tag>{line.itemCode}</Tag>
                      <Tag
                        color={line.lineType === "Product" ? "purple" : "blue"}
                      >
                        {line.lineType === "Product" ? "商品" : "服务"}
                      </Tag>
                      {line.employeeName && (
                        <Tag color="cyan">服务员工：{line.employeeName}</Tag>
                      )}
                    </Space>
                    {line.lineType === "Product" &&
                      canApproveRefunds &&
                      ["Settled", "PartiallyRefunded", "Refunded"].includes(
                        selected.data!.status,
                      ) &&
                      line.returnedQuantity < line.quantity && (
                        <Button
                          size="small"
                          danger
                          onClick={() => {
                            returnForm.setFieldsValue({
                              quantity: 1,
                              reason: "",
                            });
                            setReturnLine(line);
                          }}
                        >
                          登记退货
                        </Button>
                      )}
                  </div>
                  <div className="order-detail-grid">
                    <span>
                      数量 × {line.quantity}
                      {line.unitName ?? ""}
                    </span>
                    <span>
                      {line.lineType === "Product"
                        ? `已退 ${line.returnedQuantity}${line.unitName ?? ""}`
                        : `实际服务 ${duration(line.actualSeconds)}`}
                    </span>
                    <span>标准价 {money(line.referencePriceMinor)}</span>
                    <strong>成交价 {money(line.enteredPriceMinor)}</strong>
                  </div>
                  {line.priceOverrideReason && (
                    <Typography.Text type="warning">
                      改价原因：{line.priceOverrideReason}
                    </Typography.Text>
                  )}
                </Card>
              ))}
            </div>
            {selectedPayment && (
              <div>
                <Typography.Title level={5}>支付分摊</Typography.Title>
                {selectedPayment.cashTenderedMinor !== undefined && (
                  <Alert
                    type="success"
                    showIcon
                    title={`现金实收 ${money(selectedPayment.cashTenderedMinor)}，找零 ${money(selectedPayment.cashChangeMinor ?? 0)}`}
                    className="modal-alert"
                  />
                )}
                {selectedPayment.allocations.map((allocation) => (
                  <Card
                    key={allocation.id}
                    size="small"
                    className="payment-allocation"
                  >
                    <div>
                      <strong>{allocation.methodName}</strong>
                      <strong>{money(allocation.amountMinor)}</strong>
                    </div>
                    <Space wrap>
                      <Tag
                        color={
                          allocation.confirmationStatus ===
                          "ManualPendingReconciliation"
                            ? "orange"
                            : "green"
                        }
                      >
                        {allocation.confirmationStatus ===
                        "ManualPendingReconciliation"
                          ? "人工登记"
                          : "已记录"}
                      </Tag>
                      <Tag
                        color={
                          allocation.reconciliationStatus === "Pending"
                            ? "gold"
                            : "default"
                        }
                      >
                        {allocation.reconciliationStatus === "Pending"
                          ? "待核对"
                          : "无需外部对账"}
                      </Tag>
                      {allocation.externalReference && (
                        <Typography.Text type="secondary">
                          参考号 {allocation.externalReference}
                        </Typography.Text>
                      )}
                    </Space>
                  </Card>
                ))}
              </div>
            )}
            {selectedRefunds.length > 0 && (
              <div>
                <Typography.Title level={5}>退款记录</Typography.Title>
                {selectedRefunds.map((refund) => (
                  <Card
                    key={refund.id}
                    size="small"
                    className="payment-allocation"
                  >
                    <div>
                      <strong>{refund.refundNo}</strong>
                      <strong>{money(refund.amountMinor)}</strong>
                    </div>
                    <Space wrap>
                      <Tag
                        color={
                          refund.status === "Completed"
                            ? "green"
                            : refund.status === "Rejected"
                              ? "default"
                              : refund.status === "Processing"
                                ? "processing"
                                : "gold"
                        }
                      >
                        {refund.status === "Completed"
                          ? "已完成"
                          : refund.status === "Rejected"
                            ? "已拒绝"
                            : refund.status === "Processing"
                              ? refund.channelRefund?.status === "Failed"
                                ? "渠道退款失败"
                                : "渠道处理中"
                              : "待审批"}
                      </Tag>
                      {refund.channelRefund && (
                        <Tag>
                          {refund.channelRefund.provider === "WeChatPay"
                            ? "微信原路退款"
                            : "支付宝原路退款"}
                        </Tag>
                      )}
                      <Typography.Text type="secondary">
                        {refund.reason}
                      </Typography.Text>
                      {refund.channelRefund?.failureCode && (
                        <Typography.Text type="danger">
                          {refund.channelRefund.failureCode}
                        </Typography.Text>
                      )}
                    </Space>
                    {refund.status === "Processing" && (
                      <Space className="refund-inline-actions">
                        <Button
                          size="small"
                          loading={queryChannelRefund.isPending}
                          onClick={() => queryChannelRefund.mutate(refund)}
                        >
                          查询渠道结果
                        </Button>
                        {canApproveRefunds &&
                          refund.channelRefund?.status === "Failed" && (
                            <Button
                              size="small"
                              danger
                              loading={retryChannelRefund.isPending}
                              onClick={() => retryChannelRefund.mutate(refund)}
                            >
                              安全重试
                            </Button>
                          )}
                      </Space>
                    )}
                  </Card>
                ))}
              </div>
            )}
          </Space>
        )}
      </Drawer>

      <Modal
        title={`作废消费单 · ${voidOrder?.orderNo ?? ""}`}
        open={Boolean(voidOrder)}
        onCancel={() => setVoidOrder(undefined)}
        onOk={() => voidForm.submit()}
        okText="确认作废"
        okButtonProps={{ danger: true }}
        confirmLoading={voidMutation.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="作废后不能恢复；待支付商品产生的库存预占会被释放，不会生成销售出库流水。"
          className="modal-alert"
        />
        <Form<VoidOrderValues>
          form={voidForm}
          layout="vertical"
          onFinish={(values) =>
            voidOrder && voidMutation.mutate({ order: voidOrder, values })
          }
        >
          <Form.Item
            name="reason"
            label="作废原因"
            rules={[
              { required: true, whitespace: true },
              { min: 2 },
              { max: 500 },
            ]}
          >
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={`登记商品退货 · ${returnLine?.itemName ?? ""}`}
        open={Boolean(returnLine)}
        onCancel={() => setReturnLine(undefined)}
        onOk={() => returnForm.submit()}
        okText="确认退货入库"
        confirmLoading={productReturn.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="本操作只记录商品退货并回库，不会自动退钱。需要退款时请另行提交原支付退款申请。"
          className="modal-alert"
        />
        <Form<ProductReturnValues>
          form={returnForm}
          layout="vertical"
          onFinish={(values) =>
            selected.data &&
            returnLine &&
            productReturn.mutate({
              order: selected.data,
              line: returnLine,
              values,
            })
          }
        >
          <Form.Item
            name="quantity"
            label={`退货数量（最多 ${(returnLine?.quantity ?? 0) - (returnLine?.returnedQuantity ?? 0)}${returnLine?.unitName ?? ""}）`}
            rules={[
              { required: true },
              {
                type: "number",
                min: 1,
                max:
                  (returnLine?.quantity ?? 0) -
                  (returnLine?.returnedQuantity ?? 0),
              },
            ]}
          >
            <InputNumber
              min={1}
              max={
                (returnLine?.quantity ?? 0) -
                (returnLine?.returnedQuantity ?? 0)
              }
              precision={0}
            />
          </Form.Item>
          <Form.Item
            name="reason"
            label="退货原因"
            rules={[{ required: true, whitespace: true }, { max: 500 }]}
          >
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={`申请退款 · ${refundPayment?.paymentNo ?? ""}`}
        width={720}
        open={Boolean(refundPayment)}
        onCancel={() => setRefundPayment(undefined)}
        onOk={() => refundForm.submit()}
        okText="提交退款审批"
        confirmLoading={requestRefund.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="退款必须从原支付分摊发起；提交只预占可退额度。现金和会员账户在批准后完成反向流水；真实微信/支付宝还必须等待渠道确认成功。"
          className="modal-alert"
        />
        <Form<RefundValues>
          form={refundForm}
          layout="vertical"
          onFinish={(values) =>
            refundPayment &&
            requestRefund.mutate({ payment: refundPayment, values })
          }
        >
          <Form.List
            name="lines"
            rules={[
              {
                validator: async (_, lines: RefundValues["lines"]) => {
                  if (
                    !(lines ?? []).some((line) => Number(line.amountYuan) > 0)
                  )
                    throw new Error("至少填写一笔退款金额");
                },
              },
            ]}
          >
            {(fields, _operations, { errors }) => (
              <>
                <div className="order-line-list">
                  {fields.map((field) => {
                    const allocation = refundPayment?.allocations.find(
                      (line) =>
                        line.id ===
                        refundForm.getFieldValue([
                          "lines",
                          field.name,
                          "originalAllocationId",
                        ]),
                    );
                    const remaining = Math.max(
                      0,
                      (allocation?.amountMinor ?? 0) -
                        (reservedByAllocation[allocation?.id ?? ""] ?? 0),
                    );
                    return (
                      <Card
                        key={field.key}
                        size="small"
                        title={allocation?.methodName ?? "原支付分摊"}
                        extra={
                          <Tag>
                            {allocation?.category === "Cash"
                              ? "原现金退回"
                              : allocation?.category === "ChannelExternal"
                                ? "原支付渠道退回"
                                : "原会员账户退回"}
                          </Tag>
                        }
                      >
                        <Form.Item
                          name={[field.name, "originalAllocationId"]}
                          hidden
                        >
                          <Input />
                        </Form.Item>
                        <Form.Item
                          name={[field.name, "amountYuan"]}
                          label={`退款金额（最多 ${money(remaining)}）`}
                          rules={[
                            { required: true },
                            { type: "number", min: 0, max: remaining / 100 },
                          ]}
                        >
                          <InputNumber
                            min={0}
                            max={remaining / 100}
                            precision={2}
                            prefix="¥"
                            className="full-width"
                          />
                        </Form.Item>
                        {allocation?.category === "ChannelExternal" && (
                          <Alert
                            type="info"
                            showIcon
                            title="最高权限批准后使用固定商户退款单号提交渠道；渠道成功前不会冲减本地支付和营业数据。"
                          />
                        )}
                      </Card>
                    );
                  })}
                </div>
                <Form.ErrorList errors={errors} />
              </>
            )}
          </Form.List>
          {refundPayment?.allocations.some(
            (line) => line.category === "ManualExternal",
          ) && (
            <Alert
              type="error"
              showIcon
              title="原单包含人工外部登记；该部分需等待异常退款流程，当前不能提交。"
              className="modal-alert"
            />
          )}
          <Form.Item
            name="reason"
            label="退款原因"
            rules={[
              { required: true, whitespace: true },
              { min: 2 },
              { max: 500 },
            ]}
          >
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={`拒绝退款 · ${rejectRefund?.refundNo ?? ""}`}
        open={Boolean(rejectRefund)}
        onCancel={() => setRejectRefund(undefined)}
        onOk={() => rejectRefundForm.submit()}
        okText="确认拒绝"
        confirmLoading={rejectRefundMutation.isPending}
        destroyOnHidden
      >
        <Form<RejectRefundValues>
          form={rejectRefundForm}
          layout="vertical"
          onFinish={(values) =>
            rejectRefund &&
            rejectRefundMutation.mutate({ refund: rejectRefund, values })
          }
        >
          <Form.Item
            name="reason"
            label="拒绝原因"
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

      <Modal
        title={`驳回改价 · ${rejectPriceApproval?.orderNo ?? ""}`}
        open={Boolean(rejectPriceApproval)}
        onCancel={() => setRejectPriceApproval(undefined)}
        onOk={() => priceApprovalForm.submit()}
        okText="确认驳回"
        okButtonProps={{ danger: true }}
        confirmLoading={rejectPrice.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="驳回后不会修改原成交价；该消费单将被阻止确认，需要作废后重新录入。"
          className="modal-alert"
        />
        <Form<PriceApprovalValues>
          form={priceApprovalForm}
          layout="vertical"
          onFinish={(values) =>
            rejectPriceApproval &&
            rejectPrice.mutate({ approval: rejectPriceApproval, values })
          }
        >
          <Form.Item
            name="note"
            label="驳回原因"
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

      <Modal
        title="发布改价权限策略"
        open={pricePolicyOpen}
        onCancel={() => setPricePolicyOpen(false)}
        onOk={() => pricePolicyForm.submit()}
        okText="发布新版本"
        confirmLoading={updatePricePolicy.isPending}
        destroyOnHidden
      >
        <Alert
          type="info"
          showIcon
          title={`当前为 V${pricePolicy.data?.policyVersion ?? "—"}。发布后只影响新建订单；历史订单和待审批记录保留原策略快照。`}
          className="modal-alert"
        />
        <Form<PricePolicyValues>
          form={pricePolicyForm}
          layout="vertical"
          onFinish={(values) => updatePricePolicy.mutate(values)}
        >
          <Form.Item
            name="managerLineDiscountPercent"
            label="店长单行最大优惠比例（%）"
            rules={[{ required: true }, { type: "number", min: 0, max: 100 }]}
          >
            <InputNumber
              min={0}
              max={100}
              precision={2}
              suffix="%"
              className="full-width"
            />
          </Form.Item>
          <Form.Item
            name="managerOrderDiscountYuan"
            label="店长整单最大优惠金额（元）"
            rules={[
              { required: true },
              { type: "number", min: 0, max: 100000000 },
            ]}
          >
            <InputNumber
              min={0}
              max={100000000}
              precision={2}
              prefix="¥"
              className="full-width"
            />
          </Form.Item>
          <Form.Item name="allowManagerPriceIncrease" valuePropName="checked">
            <Checkbox>允许店长无需审批直接提高成交价</Checkbox>
          </Form.Item>
          <Alert
            type="warning"
            showIcon
            title="收银员的任何改价始终需要最高权限审批；最高权限本人改价会直接授权并保留审计记录。"
          />
        </Form>
      </Modal>

      <Modal
        title={`收款结算 · ${settleOrder?.orderNo ?? ""}`}
        width={760}
        open={Boolean(settleOrder)}
        onCancel={() => {
          setSettleOrder(undefined);
          setMemberVerification(undefined);
        }}
        onOk={() => settleForm.submit()}
        okText={
          settleAllocations.some(
            (line) =>
              paymentMethods.data?.find(
                (method) => method.id === line?.methodId,
              )?.category === "ChannelExternal",
          )
            ? "生成付款码"
            : "确认收款并结算"
        }
        confirmLoading={settle.isPending || initiateChannel.isPending}
        destroyOnHidden
      >
        <Alert
          type={hasRealChannelMethod ? "warning" : "info"}
          showIcon
          title={
            hasRealChannelMethod
              ? "会员余额只按服务端账户扣减；真实微信/支付宝必须等待渠道确认，人工登记不会伪装成渠道确认。"
              : hasManualExternalMethod
                ? "真实微信/支付宝渠道当前停用；仍可选择现金、微信人工登记或支付宝人工登记完成结算。人工登记金额会进入当前班次的“人工外部收款待核对”。"
                : "真实微信/支付宝渠道当前停用；仍可选择现金完成结算。"
          }
          className="modal-alert"
        />
        <Form<SettleValues>
          form={settleForm}
          layout="vertical"
          onFinish={submitSettlement}
        >
          <Form.List
            name="allocations"
            rules={[
              {
                validator: async (_, lines: SettleAllocationValues[]) => {
                  const total = (lines ?? []).reduce(
                    (sum, line) =>
                      sum + Math.round(Number(line.amountYuan ?? 0) * 100),
                    0,
                  );
                  if (total !== settleOrder?.receivableMinor)
                    throw new Error(
                      `分摊合计必须等于 ${money(settleOrder?.receivableMinor ?? 0)}`,
                    );
                  const hasChannel = (lines ?? []).some(
                    (line) =>
                      paymentMethods.data?.find(
                        (method) => method.id === line?.methodId,
                      )?.category === "ChannelExternal",
                  );
                  if (hasChannel && lines.length !== 1)
                    throw new Error(
                      "当前版本真实渠道支付必须整单使用一种渠道，不可组合分摊",
                    );
                  if (
                    memberAmountMinor >= 50000 &&
                    (memberVerification?.status !== "Verified" ||
                      memberVerification.orderId !== settleOrder?.id ||
                      memberVerification.authorizedAmountMinor !==
                        memberAmountMinor)
                  )
                    throw new Error(
                      "会员资金扣款达到500元，必须按当前消费单和扣款金额重新完成验证码核验",
                    );
                },
              },
            ]}
          >
            {(fields, { add, remove }, { errors }) => (
              <>
                <div className="order-line-list">
                  {fields.map((field) => (
                    <PaymentAllocationEditor
                      key={field.key}
                      field={field}
                      form={settleForm}
                      methods={(paymentMethods.data ?? []).filter(
                        (method) =>
                          settleOrder?.customerId ||
                          method.category !== "InternalAccount",
                      )}
                      accounts={memberAccounts}
                      removable={fields.length > 1}
                      onRemove={() => {
                        remove(field.name);
                        setMemberVerification(undefined);
                      }}
                    />
                  ))}
                </div>
                <Space>
                  <Button
                    icon={<PlusOutlined />}
                    onClick={() => {
                      add({ amountYuan: 0 });
                      setMemberVerification(undefined);
                    }}
                  >
                    添加支付方式
                  </Button>
                  <Form.ErrorList errors={errors} />
                </Space>
              </>
            )}
          </Form.List>
          {cashAmountMinor > 0 && (
            <Card size="small" className="member-verification-card">
              <Form.Item
                name="cashTenderedYuan"
                label={`现金实收（现金应收 ${money(cashAmountMinor)}）`}
                rules={[
                  { required: true, message: "请填写顾客实际交付现金" },
                  {
                    type: "number",
                    min: cashAmountMinor / 100,
                    max: 100000000,
                  },
                ]}
              >
                <InputNumber
                  min={cashAmountMinor / 100}
                  max={100000000}
                  precision={2}
                  prefix="¥"
                  className="full-width"
                />
              </Form.Item>
              <Alert
                type={
                  (cashTenderedYuan ?? 0) * 100 >= cashAmountMinor
                    ? "success"
                    : "warning"
                }
                showIcon
                title={
                  (cashTenderedYuan ?? 0) * 100 >= cashAmountMinor
                    ? `应找零 ${money(Math.round((cashTenderedYuan ?? 0) * 100) - cashAmountMinor)}`
                    : "现金实收不能小于现金应收"
                }
              />
            </Card>
          )}
          {memberAmountMinor > 0 && (
            <Card size="small" className="member-verification-card">
              <Alert
                type="info"
                showIcon
                title={`本次使用会员资金 ${money(memberAmountMinor)}；扣款前必须核对完整手机号。`}
                className="modal-alert"
              />
              <Form.Item
                name="verifiedMobile"
                label="会员完整手机号"
                rules={[
                  { required: true, message: "请输入完整手机号进行核对" },
                  {
                    pattern: /^1[3-9]\d{9}$/,
                    message: "请输入有效的中国大陆手机号",
                  },
                ]}
              >
                <Input
                  maxLength={11}
                  inputMode="numeric"
                  onChange={() => setMemberVerification(undefined)}
                />
              </Form.Item>
              {memberAmountMinor >= 50000 && (
                <>
                  <Space align="end" className="full-width">
                    <Button
                      loading={issueVerification.isPending}
                      disabled={!verifiedMobile || !settleOrder}
                      onClick={() =>
                        settleOrder &&
                        issueVerification.mutate({
                          orderId: settleOrder.id,
                          memberAmountMinor,
                          fullMobile: verifiedMobile!,
                        })
                      }
                    >
                      获取验证码
                    </Button>
                    {memberVerification && (
                      <Typography.Text type="secondary">
                        发送至 {memberVerification.maskedMobile}，5分钟有效
                      </Typography.Text>
                    )}
                  </Space>
                  {memberVerification?.developmentCode && (
                    <Alert
                      type="warning"
                      showIcon
                      title={`本地测试验证码：${memberVerification.developmentCode}（生产环境不会回显）`}
                      className="modal-alert"
                    />
                  )}
                  {memberVerification &&
                    memberVerification.status !== "Verified" && (
                      <Space align="end" className="full-width">
                        <Form.Item
                          name="verificationCode"
                          label="6位验证码"
                          rules={[{ required: true }, { pattern: /^\d{6}$/ }]}
                          className="grow"
                        >
                          <Input maxLength={6} inputMode="numeric" />
                        </Form.Item>
                        <Button
                          type="primary"
                          loading={verifyMemberCode.isPending}
                          onClick={() => {
                            const code =
                              settleForm.getFieldValue("verificationCode");
                            if (code)
                              verifyMemberCode.mutate({
                                challengeId: memberVerification.id,
                                code,
                              });
                          }}
                        >
                          验证
                        </Button>
                      </Space>
                    )}
                  {memberVerification?.status === "Verified" && (
                    <Alert
                      type="success"
                      showIcon
                      title="验证码已通过，将与当前消费单和会员扣款金额绑定。"
                    />
                  )}
                </>
              )}
            </Card>
          )}
        </Form>
      </Modal>

      <Modal
        title={
          channelOrder?.provider === "WeChatPay" ? "微信支付" : "支付宝支付"
        }
        open={Boolean(channelOrder)}
        closable={false}
        mask={{ closable: false }}
        footer={
          channelOrder &&
          ["Created", "QrReady"].includes(channelOrder.status) ? (
            <Space>
              <Button
                loading={queryChannel.isPending}
                onClick={() => queryChannel.mutate(channelOrder)}
              >
                立即查单
              </Button>
              <Button
                danger
                loading={closeChannel.isPending}
                onClick={() => closeChannel.mutate(channelOrder)}
              >
                查单并关闭
              </Button>
            </Space>
          ) : (
            <Button type="primary" onClick={() => setChannelOrder(undefined)}>
              完成
            </Button>
          )
        }
      >
        <Space
          orientation="vertical"
          size={18}
          className="full-width"
          align="center"
        >
          {channelOrder?.qrPayload ? (
            <QRCode
              value={channelOrder.qrPayload}
              size={240}
              status={
                channelOrder.status === "Paid"
                  ? "scanned"
                  : channelOrder.status === "QrReady"
                    ? "active"
                    : "expired"
              }
            />
          ) : (
            <Alert
              type="warning"
              showIcon
              title="渠道尚未返回付款码；系统正在保留订单并等待查单，不会标记为已付款。"
            />
          )}
          <Statistic
            title="应付金额"
            value={(channelOrder?.amountMinor ?? 0) / 100}
            precision={2}
            prefix="¥"
          />
          <Tag
            color={
              channelOrder?.status === "Paid"
                ? "green"
                : channelOrder?.status === "QrReady"
                  ? "processing"
                  : channelOrder?.status === "Closed"
                    ? "default"
                    : "gold"
            }
          >
            {channelOrder?.status === "Paid"
              ? "渠道已确认支付"
              : channelOrder?.status === "QrReady"
                ? "等待顾客扫码"
                : channelOrder?.status === "Closed"
                  ? "已关单"
                  : channelOrder?.status === "Failed"
                    ? "下单失败"
                    : "创建中"}
          </Tag>
          {channelOrder?.status === "Paid" && (
            <Alert
              type="success"
              showIcon
              title="验签结果已入账，消费单已经结算。"
            />
          )}
          {channelOrder?.status === "Closed" && (
            <Alert
              type="info"
              showIcon
              title="渠道确认未支付并已关单，可重新选择其他收款方式。"
            />
          )}
          <Typography.Text type="secondary">
            商户订单号 {channelOrder?.outTradeNo}
          </Typography.Text>
        </Space>
      </Modal>

      <Modal
        title={shiftAction === "open" ? "开始收银班次" : "提交交班"}
        open={Boolean(shiftAction)}
        onCancel={() => {
          setShiftAction(undefined);
          setSettleAfterShiftOpen(undefined);
        }}
        onOk={() => shiftForm.submit()}
        okText={shiftAction === "open" ? "确认开班" : "确认交班"}
        confirmLoading={openShift.isPending || submitShift.isPending}
        destroyOnHidden
      >
        <Alert
          type="info"
          showIcon
          title={
            shiftAction === "open"
              ? "备用金只用于计算本班次理论现金，不计入营业收入。"
              : "提交后冻结本班次范围；账实一致且没有外部待核对时自动关班，否则进入独立复核。"
          }
          className="modal-alert"
        />
        <Form<ShiftValues>
          form={shiftForm}
          layout="vertical"
          onFinish={(values) =>
            shiftAction === "open"
              ? openShift.mutate(values)
              : submitShift.mutate(values)
          }
        >
          <Form.Item
            name="amountYuan"
            label={
              shiftAction === "open" ? "开班备用金（元）" : "实际清点现金（元）"
            }
            rules={[
              { required: true },
              { type: "number", min: 0, max: 100000000 },
            ]}
          >
            <InputNumber
              min={0}
              max={100000000}
              precision={2}
              prefix="¥"
              className="full-width"
            />
          </Form.Item>
          {shiftAction === "submit" && (
            <Form.Item
              name="note"
              label="交班备注（可选）"
              rules={[{ max: 500 }]}
            >
              <Input.TextArea rows={3} maxLength={500} showCount />
            </Form.Item>
          )}
        </Form>
      </Modal>
      <Modal
        title={`复核交班 · ${reviewShift?.shift.shiftNo ?? ""}`}
        open={Boolean(reviewShift)}
        onCancel={() => setReviewShift(undefined)}
        onOk={() => reviewForm.submit()}
        okText="确认复核并关班"
        confirmLoading={review.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="复核确认交班数据和差额处理，不会修改原支付流水，也不会把人工微信/支付宝标记为渠道确认。"
          className="modal-alert"
        />
        {reviewShift && (
          <Descriptions
            bordered
            size="small"
            column={2}
            className="modal-alert"
            items={[
              {
                key: "operator",
                label: "收银员",
                children: reviewShift.operatorDisplayName,
              },
              {
                key: "difference",
                label: "现金差额",
                children: money(reviewShift.shift.cashDifferenceMinor ?? 0),
              },
              {
                key: "pending",
                label: "外部待核对",
                children: money(
                  reviewShift.shift.pendingReconciliationMinor ?? 0,
                ),
              },
              {
                key: "note",
                label: "交班备注",
                children: reviewShift.shift.handoverNote ?? "无",
              },
            ]}
          />
        )}
        <Form<ReviewValues>
          form={reviewForm}
          layout="vertical"
          onFinish={(values) =>
            reviewShift && review.mutate({ item: reviewShift, values })
          }
        >
          <Form.Item
            name="reason"
            label="复核说明"
            rules={[
              {
                required: Boolean(
                  (reviewShift?.shift.cashDifferenceMinor ?? 0) !== 0 ||
                    (reviewShift?.shift.pendingReconciliationMinor ?? 0) > 0,
                ),
                message: "存在差额或外部待核对金额时必须填写复核说明",
              },
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

function OrderLineEditor({
  field,
  form,
  priceBook,
  products,
  employees,
  onRemove,
  removable,
}: {
  field: { key: number; name: number };
  form: ReturnType<typeof Form.useForm<OrderValues>>[0];
  priceBook?: PriceBook;
  products: ProductItem[];
  employees: ServiceEmployee[];
  onRemove: () => void;
  removable: boolean;
}) {
  const lineType =
    Form.useWatch(["lines", field.name, "lineType"], form) ?? "Service";
  const itemId = Form.useWatch(
    [
      "lines",
      field.name,
      lineType === "Product" ? "productItemId" : "serviceItemId",
    ],
    form,
  );
  const entered = Form.useWatch(
    ["lines", field.name, "enteredPriceYuan"],
    form,
  );
  const standardMinor =
    lineType === "Product"
      ? priceBook?.productLines.find((line) => line.productItemId === itemId)
          ?.unitPriceMinor
      : priceBook?.lines.find((line) => line.serviceItemId === itemId)
          ?.unitPriceMinor;
  const changed =
    standardMinor !== undefined &&
    Math.round(Number(entered ?? 0) * 100) !== standardMinor;
  return (
    <Card
      size="small"
      className="order-line-editor"
      extra={
        removable && (
          <Button
            type="text"
            danger
            icon={<DeleteOutlined />}
            onClick={onRemove}
            aria-label="删除项目"
          />
        )
      }
    >
      <div className="order-line-fields">
        <Form.Item
          name={[field.name, "lineType"]}
          label="类型"
          rules={[{ required: true }]}
        >
          <Select
            options={[
              { value: "Service", label: "服务项目" },
              { value: "Product", label: "商品" },
            ]}
            onChange={(value: "Service" | "Product") => {
              const first =
                value === "Product"
                  ? priceBook?.productLines[0]
                  : priceBook?.lines[0];
              form.setFieldValue(
                ["lines", field.name, "serviceItemId"],
                value === "Service" && first && "serviceItemId" in first
                  ? first.serviceItemId
                  : undefined,
              );
              form.setFieldValue(
                ["lines", field.name, "productItemId"],
                value === "Product" && first && "productItemId" in first
                  ? first.productItemId
                  : undefined,
              );
              form.setFieldValue(
                ["lines", field.name, "serviceEmployeeId"],
                undefined,
              );
              form.setFieldValue(
                ["lines", field.name, "actualMinutes"],
                undefined,
              );
              form.setFieldValue(
                ["lines", field.name, "enteredPriceYuan"],
                first ? first.unitPriceMinor / 100 : 0,
              );
            }}
          />
        </Form.Item>
        {lineType === "Product" ? (
          <Form.Item
            name={[field.name, "productItemId"]}
            label="商品"
            rules={[{ required: true }]}
          >
            <Select
              optionLabelProp="title"
              options={priceBook?.productLines.map((line) => {
                const product = products.find(
                  (item) => item.id === line.productItemId,
                );
                return {
                  value: line.productItemId,
                  title: `${line.productItemName} · ${line.unitName} · 标准价 ${money(line.unitPriceMinor)}`,
                  label: (
                    <Space>
                      {product?.imageFileId ? (
                        <Image
                          preview={false}
                          width={34}
                          height={34}
                          style={{ objectFit: "cover", borderRadius: 6 }}
                          src={`/api/v1/catalog/products/${product.id}/image?v=${product.imageFileId}`}
                        />
                      ) : (
                        <span
                          style={{
                            width: 34,
                            height: 34,
                            borderRadius: 6,
                            background: "#f3f5f7",
                            display: "grid",
                            placeItems: "center",
                          }}
                        >
                          <PictureOutlined />
                        </span>
                      )}
                      <span>
                        {line.productItemName} · {line.unitName} · 标准价{" "}
                        {money(line.unitPriceMinor)}
                      </span>
                    </Space>
                  ),
                };
              })}
              onChange={(value) => {
                const price =
                  priceBook?.productLines.find(
                    (line) => line.productItemId === value,
                  )?.unitPriceMinor ?? 0;
                form.setFieldValue(
                  ["lines", field.name, "enteredPriceYuan"],
                  price / 100,
                );
              }}
            />
          </Form.Item>
        ) : (
          <>
            <Form.Item
              name={[field.name, "serviceItemId"]}
              label="服务项目"
              rules={[{ required: true }]}
            >
              <Select
                options={priceBook?.lines.map((line) => ({
                  value: line.serviceItemId,
                  label: `${line.serviceItemName} · 标准价 ${money(line.unitPriceMinor)}`,
                }))}
                onChange={(value) => {
                  const price =
                    priceBook?.lines.find(
                      (line) => line.serviceItemId === value,
                    )?.unitPriceMinor ?? 0;
                  form.setFieldValue(
                    ["lines", field.name, "enteredPriceYuan"],
                    price / 100,
                  );
                }}
              />
            </Form.Item>
            <Form.Item
              name={[field.name, "serviceEmployeeId"]}
              label="实际服务员工"
              rules={[{ required: true, message: "请选择本次实际服务员工" }]}
            >
              <Select
                showSearch
                optionFilterProp="label"
                placeholder="选择实际服务员工"
                options={employees.map((employee) => ({
                  value: employee.id,
                  label: `${employee.displayName} · ${employee.employeeNo} · ${employee.positionName}`,
                }))}
              />
            </Form.Item>
          </>
        )}
        <Form.Item
          name={[field.name, "quantity"]}
          label="数量"
          rules={[{ required: true }, { type: "number", min: 1, max: 999 }]}
        >
          <InputNumber min={1} max={999} precision={0} />
        </Form.Item>
        {lineType === "Service" && (
          <Form.Item
            name={[field.name, "actualMinutes"]}
            label="实际时长（分钟，可选）"
            rules={[{ type: "number", min: 0, max: 1440 }]}
          >
            <InputNumber min={0} max={1440} precision={0} />
          </Form.Item>
        )}
        <Form.Item
          name={[field.name, "enteredPriceYuan"]}
          label="成交单价（元）"
          rules={[
            { required: true },
            { type: "number", min: 0, max: 100000000 },
          ]}
        >
          <InputNumber min={0} max={100000000} precision={2} prefix="¥" />
        </Form.Item>
      </div>
      <Typography.Text type="secondary">
        标准单价：
        {standardMinor === undefined ? "请选择项目" : money(standardMinor)}。
        {lineType === "Service"
          ? "实际时长仅记录，不参与金额计算。"
          : "跟踪库存的商品在确认时预占、结算时出库。"}
      </Typography.Text>
      {changed && (
        <Form.Item
          name={[field.name, "priceOverrideReason"]}
          label="改价原因"
          rules={[
            { required: true, message: "成交价与标准价不同时必须填写原因" },
            { min: 2 },
            { max: 500 },
          ]}
        >
          <Input maxLength={500} placeholder="例如：经负责人确认的现场增减项" />
        </Form.Item>
      )}
    </Card>
  );
}

function PaymentAllocationEditor({
  field,
  form,
  methods,
  accounts,
  removable,
  onRemove,
}: {
  field: { key: number; name: number };
  form: ReturnType<typeof Form.useForm<SettleValues>>[0];
  methods: PaymentMethod[];
  accounts: SettlementMemberAccount[];
  removable: boolean;
  onRemove: () => void;
}) {
  const methodId = Form.useWatch(["allocations", field.name, "methodId"], form);
  const method = methods.find((item) => item.id === methodId);
  return (
    <Card
      size="small"
      className="order-line-editor"
      extra={
        removable && (
          <Button
            type="text"
            danger
            icon={<DeleteOutlined />}
            onClick={onRemove}
            aria-label="删除支付分摊"
          />
        )
      }
    >
      <div className="payment-line-fields">
        <Form.Item
          name={[field.name, "methodId"]}
          label="支付方式"
          rules={[{ required: true }]}
        >
          <Select
            options={methods.map((item) => ({
              value: item.id,
              label: item.name,
            }))}
            onChange={() => {
              form.setFieldValue(
                ["allocations", field.name, "memberAccountId"],
                undefined,
              );
              form.setFieldValue(
                ["allocations", field.name, "externalReference"],
                undefined,
              );
            }}
          />
        </Form.Item>
        <Form.Item
          name={[field.name, "amountYuan"]}
          label="实收/扣款金额（元）"
          rules={[
            { required: true },
            { type: "number", min: 0.01, max: 100000000 },
          ]}
        >
          <InputNumber min={0.01} max={100000000} precision={2} prefix="¥" />
        </Form.Item>
      </div>
      {method?.category === "InternalAccount" && (
        <Form.Item
          name={[field.name, "memberAccountId"]}
          label="会员账户"
          rules={[{ required: true, message: "请选择对应会员账户" }]}
        >
          <Select
            options={accounts
              .filter(
                (account) =>
                  account.accountType === method.internalAccountType &&
                  account.status === "Active",
              )
              .map((account) => ({
                value: account.id,
                label: `${account.cardLabel} · 可用 ${money(account.balanceUnits)}`,
              }))}
          />
        </Form.Item>
      )}
      {method?.category === "ManualExternal" && (
        <Form.Item
          name={[field.name, "externalReference"]}
          label="交易参考号"
          rules={[
            { required: true, message: "人工外部收款必须填写参考号" },
            { min: 4 },
            { max: 100 },
          ]}
        >
          <Input
            maxLength={100}
            placeholder="填写收款记录中的交易号或可核对参考号"
          />
        </Form.Item>
      )}
      {method?.category === "ManualExternal" && (
        <Alert
          type="warning"
          showIcon
          title="该笔只会标记为“人工登记、待核对”，不会标记为渠道确认。"
        />
      )}
      {method?.category === "ChannelExternal" && (
        <Alert
          type="info"
          showIcon
          title="提交后生成一次性付款码；本页不能手工把它改成支付成功。"
        />
      )}
    </Card>
  );
}
