export function commissionDateRange(days: number, timeZone = "Asia/Shanghai", now = new Date()) {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(now);
  const part = (type: string) => parts.find(item => item.type === type)!.value;
  const toDate = `${part("year")}-${part("month")}-${part("day")}`;
  const from = new Date(`${toDate}T12:00:00Z`);
  from.setUTCDate(from.getUTCDate() - days + 1);
  return { fromDate: from.toISOString().slice(0, 10), toDate };
}
