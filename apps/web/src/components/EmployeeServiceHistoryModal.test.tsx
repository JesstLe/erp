// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { EmployeeServiceHistoryModal } from "./EmployeeServiceHistoryModal";
import { commissionDateRange } from "./employeeCommissionDates";
const apiMock = vi.hoisted(() => vi.fn());
const reportState = vi.hoisted(() => ({ failure: undefined as Error | undefined }));
vi.mock("@tanstack/react-query", async importOriginal => {
  const actual = await importOriginal<typeof import("@tanstack/react-query")>();
  return { ...actual, useQuery: (...args: Parameters<typeof actual.useQuery>) => {
    const result = actual.useQuery(...args);
    // Inject the query error state to test display semantics independently of promise-rejection tooling.
    return reportState.failure ? { ...result, data: undefined, isError: true, isLoading: false, isFetching: false, error: reportState.failure } : result;
  } };
});
vi.mock("../api/client", () => ({ apiRequest: apiMock }));
vi.mock("../auth/useAuth", () => ({ useAuth: () => ({ user: { tenantId: "brand1", stores: [{ id: "store1", code: "S001", name: "测试店" }] } }) }));
const employee = { id: "employee1", employeeNo: "EMP000001", displayName: "服务老师", positionCode: "POS000001", status: "Active",
  roles: [], stores: [{ id: "store1", code: "S001", name: "测试店", isPrimary: true }], createdAtUtc: "2026-10-01T00:00:00Z", version: 1 };
beforeAll(() => {
  Object.defineProperty(window, "matchMedia", { writable: true, value: vi.fn().mockImplementation(() => ({ matches: false,
    addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() })) });
  Object.defineProperty(globalThis, "ResizeObserver", { configurable: true, value: class { observe() {} unobserve() {} disconnect() {} } });
});
const clients: QueryClient[] = [];
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); });
beforeEach(() => { reportState.failure = undefined; apiMock.mockReset().mockResolvedValue({ timeZoneId: "Asia/Shanghai", total: 0, page: 1, pageSize: 20,
  totals: { orderCount: 0, lineCount: 0, serviceQuantity: 0, serviceRevenueMinor: 0, grossCommissionMinor: 0, refundDeductionMinor: 0, netCommissionMinor: 0 },
  employees: [], items: [] }); });
function mount(classic = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  render(<QueryClientProvider client={client}>
    <EmployeeServiceHistoryModal employee={employee} positionName="高级老师" classic={classic} onClose={() => {}} />
  </QueryClientProvider>);
}
describe("employee service history in-place modal", () => {
  it("uses brand-local today and ranges across month/year boundaries", () => {
    expect(commissionDateRange(1, "Asia/Shanghai", new Date("2025-12-31T16:30:00Z"))).toEqual({ fromDate: "2026-01-01", toDate: "2026-01-01" });
    expect(commissionDateRange(7, "Asia/Shanghai", new Date("2025-12-31T16:30:00Z"))).toEqual({ fromDate: "2025-12-26", toDate: "2026-01-01" });
  });
  it("locks exact employee ID for today, custom dates, all history and reset within classic UI", async () => {
    mount(true);
    await waitFor(() => expect(apiMock).toHaveBeenCalled());
    const initial = new URL(apiMock.mock.calls[0][0], "https://example.test").searchParams;
    expect(initial.get("employeeId")).toBe(employee.id);
    expect(initial.get("fromDate")).toBe(commissionDateRange(1).fromDate);
    expect(initial.get("toDate")).toBe(initial.get("fromDate"));
    expect(document.querySelector(".classic-feature-panel")).toBeTruthy();
    expect(screen.queryByText("按员工汇总")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "全部历史" }));
    await waitFor(() => {
      const p = new URL(apiMock.mock.lastCall![0], "https://example.test").searchParams;
      expect(p.get("employeeId")).toBe(employee.id); expect(p.has("fromDate")).toBe(false); expect(p.has("toDate")).toBe(false);
    });
    fireEvent.change(screen.getByLabelText("提成开始日期"), { target: { value: "2026-09-01" } });
    fireEvent.change(screen.getByLabelText("提成结束日期"), { target: { value: "2026-09-30" } });
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith(expect.stringContaining("fromDate=2026-09-01&toDate=2026-09-30"), expect.anything()));
    fireEvent.click(screen.getByRole("button", { name: "重置筛选" }));
    expect((screen.getByLabelText("提成开始日期") as HTMLInputElement).value).toBe(initial.get("fromDate"));
    expect(screen.getByText(/EMP000001（精确关联）/)).toBeTruthy();
  }, 15000);
  it("rejects reversed dates without querying, and distinguishes loading failure from a zero balance", async () => {
    reportState.failure = new Error("服务记录加载失败");
    mount();
    expect(await screen.findByText("服务记录加载失败")).toBeTruthy();
    expect(document.querySelectorAll(".ant-statistic-content-value")[0]?.textContent).toBe("—");
    fireEvent.change(screen.getByLabelText("提成开始日期"), { target: { value: "2099-12-31" } });
    expect(await screen.findByText(/开始日期不得晚于结束日期/)).toBeTruthy();
    expect(screen.getByRole("button", { name: /刷\s*新/ }).hasAttribute("disabled")).toBe(true);
    expect(apiMock.mock.calls.some(([url]) => String(url).includes("fromDate=2099-12-31"))).toBe(false);
  }, 15000);
});
