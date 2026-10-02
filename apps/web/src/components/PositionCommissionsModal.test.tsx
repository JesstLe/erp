// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { PositionCommissionsModal } from "./PositionCommissionsModal";
import { commissionLabel, commissionSourceLabel } from "./positionCommission";
import type { PositionCommissions, PositionServiceCommission } from "../api/types";

const apiMock = vi.hoisted(() => vi.fn());
vi.mock("../api/client", () => ({ apiRequest: apiMock, ApiError: class extends Error {} }));
const position = { id: "p1", code: "POS000001", name: "高级老师", sortOrder: 0, status: "ENABLED", version: 7 };
const service: PositionServiceCommission = { serviceItemId: "s1", code: "SV000001", name: "基础服务", status: "Enabled",
  rateBasisPoints: null, fallbackMode: "FixedAmount", fallbackRateBasisPoints: null, fallbackFixedMinor: 2500 };
const configuration: PositionCommissions = { position: { ...position, defaultCommissionRateBasisPoints: 2000 },
  services: [service, { ...service, serviceItemId: "s2", code: "SV000002", name: "高级服务", rateBasisPoints: 1550 }] };
beforeAll(() => {
  Object.defineProperty(globalThis, "ResizeObserver", { configurable: true,
    value: class { observe() {} unobserve() {} disconnect() {} } });
  Object.defineProperty(window, "matchMedia", { writable: true, value: vi.fn().mockImplementation(() => ({
    matches: false, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(),
    removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  })) });
});
afterEach(cleanup);
beforeEach(() => apiMock.mockReset().mockResolvedValue(configuration));
function mount() {
  const onClose = vi.fn();
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <PositionCommissionsModal position={position} onClose={onClose} />
  </QueryClientProvider>);
  return onClose;
}
describe("position commission setup", () => {
  it("shows override, explicit zero, default and old fixed fallback distinctly", () => {
    expect(commissionLabel(service, 1550, 2000)).toBe("15.5%");
    expect(commissionLabel(service, 0, 2000)).toBe("0%（不计提）");
    expect(commissionLabel(service, undefined, 2000)).toBe("20%");
    expect(commissionLabel(service, undefined, null)).toBe("每次 ¥25.00（项目规则）");
    expect(commissionLabel(service, undefined, 0)).toBe("0%（不计提）");
    expect(commissionSourceLabel(1550, 2000)).toBe("用此项目单独比例");
    expect(commissionSourceLabel(0, 2000)).toBe("用此项目单独比例");
    expect(commissionSourceLabel(undefined, 2000)).toBe("用岗位通用比例");
    expect(commissionSourceLabel(undefined, 0)).toBe("用岗位通用比例");
    expect(commissionSourceLabel(undefined, null)).toBe("用服务项目原设置");
  });
  it("saves a custom default and per-project ratio with the current version", async () => {
    const close = mount();
    const input = await screen.findByLabelText("岗位通用比例");
    await waitFor(() => expect((input as HTMLInputElement).value).toBe("20.00"));
    expect(screen.queryByText(/合伙人|暂未纳入/)).toBeNull();
    expect(screen.getByText(/按服务成交金额（含数量）计算提成/)).toBeTruthy();
    expect(screen.getByText("提成只算一次，不叠加")).toBeTruthy();
    expect(screen.getByText(/提成是15元，不是35元/)).toBeTruthy();
    expect(screen.getByText("用此项目单独比例")).toBeTruthy();
    expect(screen.getByText("用岗位通用比例")).toBeTruthy();
    fireEvent.change(input, { target: { value: "12.5" } });
    fireEvent.blur(input);
    fireEvent.change(screen.getByLabelText("基础服务提成比例"), { target: { value: "0" } });
    fireEvent.blur(screen.getByLabelText("基础服务提成比例"));
    fireEvent.click(screen.getByRole("button", { name: "保存提成规则" }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith("/api/v1/employees/positions/p1/commissions",
      expect.objectContaining({ method: "PUT", body: JSON.stringify({ defaultRateBasisPoints: 1250,
        expectedVersion: 7, services: [{ serviceItemId: "s2", rateBasisPoints: 1550 }, { serviceItemId: "s1", rateBasisPoints: 0 }] }) })));
    await waitFor(() => expect(close).toHaveBeenCalled());
  });
  it("batch applies to selected query results and can clear them back to inheritance", async () => {
    mount();
    await waitFor(() => expect((screen.getByLabelText("岗位通用比例") as HTMLInputElement).value).toBe("20.00"));
    fireEvent.click(screen.getByRole("button", { name: "全选查询结果" }));
    const batch = screen.getByLabelText("批量提成比例");
    fireEvent.change(batch, { target: { value: "18.75" } });
    fireEvent.blur(batch);
    fireEvent.click(screen.getByRole("button", { name: "应用到选中项目" }));
    await waitFor(() => expect(screen.getAllByText("18.75%")).toHaveLength(2));
    fireEvent.click(screen.getByRole("button", { name: "清空选中项目比例" }));
    fireEvent.click(screen.getByRole("button", { name: "清空通用比例" }));
    fireEvent.click(screen.getByRole("button", { name: "保存提成规则" }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith("/api/v1/employees/positions/p1/commissions",
      expect.objectContaining({ method: "PUT", body: JSON.stringify({ defaultRateBasisPoints: null, expectedVersion: 7, services: [] }) })));
  }, 15000);
});
