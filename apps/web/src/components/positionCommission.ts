import type { PositionServiceCommission } from "../api/types";

export function commissionSourceLabel(override: number | undefined, defaultRate: number | null): string {
  if (override != null) return "用此项目单独比例";
  if (defaultRate != null) return "用岗位通用比例";
  return "用服务项目原设置";
}

export function commissionLabel(service: PositionServiceCommission, override: number | undefined,
  defaultRate: number | null): string {
  const rate = override ?? defaultRate;
  if (rate != null) return `${rate / 100}%${rate === 0 ? "（不计提）" : ""}`;
  if (service.fallbackMode === "Percentage") return `${(service.fallbackRateBasisPoints ?? 0) / 100}%（项目规则）`;
  if (service.fallbackMode === "FixedAmount") return `每次 ¥${((service.fallbackFixedMinor ?? 0) / 100).toFixed(2)}（项目规则）`;
  return "不计提（项目规则）";
}
