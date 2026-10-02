// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { EmployeeCommissionsPage } from "./EmployeeCommissionsPage";
const apiMock = vi.hoisted(() => vi.fn());
vi.mock("../api/client", () => ({ apiRequest: apiMock }));
vi.mock("../auth/useAuth", () => ({ useAuth: () => ({ user: { tenantId: "brand1", stores: [{ id: "store1", code: "S001", name: "测试店" }] } }) }));
const line = { lineId: "line1", orderId: "order1", orderNo: "SO-001", storeId: "store1", storeCode: "S001", storeName: "测试店",
  paidAtUtc: "2026-10-02T03:00:00Z", employeeId: "employee1", employeeNo: "EMP000001", employeeName: "服务老师",
  positionCode: "POS000001", positionName: "高级老师", itemCode: "SV000001", itemName: "基础服务", quantity: 1,
  unitPriceMinor: 8000, lineAmountMinor: 8000, commissionMode: "Percentage", rateBasisPoints: 1550, ruleSource: "PositionService",
  grossCommissionMinor: 1240, refundDeductionMinor: 310, netCommissionMinor: 930, orderRefundedMinor: 2000,
  actualSeconds: 600, referencePriceMinor: 10000, pricingSource: "ManualOverride" };
beforeAll(() => {
  Object.defineProperty(window, "matchMedia", { writable: true, value: vi.fn().mockImplementation(() => ({ matches: false,
    addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() })) });
  Object.defineProperty(globalThis, "ResizeObserver", { configurable: true, value: class { observe() {} unobserve() {} disconnect() {} } });
});
afterEach(cleanup);
beforeEach(() => apiMock.mockReset().mockResolvedValue({ timeZoneId: "Asia/Shanghai", total: 25, page: 1, pageSize: 20,
  totals: { orderCount: 25, lineCount: 25, serviceQuantity: 25, serviceRevenueMinor: 200000, grossCommissionMinor: 31000, refundDeductionMinor: 310, netCommissionMinor: 30690 },
  employees: [{ employeeId: "employee1", employeeNo: "EMP000001", employeeName: "服务老师", serviceQuantity: 25, orderCount: 25,
    grossServiceRevenueMinor: 200000, grossCommissionMinor: 31000, refundDeductionMinor: 310, netCommissionMinor: 30690 }], items: [line] }));
function mount(classic = false) {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <EmployeeCommissionsPage classic={classic} />
  </QueryClientProvider>);
}
describe("employee commission report", () => {
  it("shows global totals independent of one visible page and opens per-line snapshots", async () => {
    mount();
    expect(await screen.findByText("SO-001")).toBeTruthy();
    expect(screen.getAllByText(/306[.,]90/).length).toBeGreaterThan(0);
    expect(screen.getByText("15.5%")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /详\s*情/ }));
    expect(await screen.findByText("提成记录详情")).toBeTruthy();
    expect(screen.getByText("该条提成退款冲减")).toBeTruthy();
    expect(screen.getByText("高级老师 · POS000001")).toBeTruthy();
  });
  it("auto searches and retains the classic shell while opening the same financial report", async () => {
    mount(true);
    fireEvent.change(screen.getByLabelText("实时查询员工提成"), { target: { value: "EMP000001" } });
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith(expect.stringContaining("query=EMP000001"), expect.anything()), { timeout: 1500 });
    expect(document.querySelector(".classic-feature-panel")).toBeTruthy();
    fireEvent.change(screen.getByLabelText("提成开始日期"), { target: { value: "2026-10-01" } });
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith(expect.stringContaining("fromDate=2026-10-01"), expect.anything()));
  }, 15000);
});
