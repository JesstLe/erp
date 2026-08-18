export function toUtcIso(localDateTime: string) {
  const value = new Date(localDateTime)
  if (Number.isNaN(value.getTime())) throw new Error('请输入有效时间')
  return value.toISOString()
}

export function toLocalDateTimeValue(value: Date | string) {
  const date = typeof value === 'string' ? new Date(value) : value
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

export function isValidPeriod(startsAtLocal: string, endsAtLocal: string, minimumMinutes: number) {
  const start = new Date(startsAtLocal)
  const end = new Date(endsAtLocal)
  return Number.isFinite(start.getTime()) && Number.isFinite(end.getTime()) &&
    end.getTime() - start.getTime() >= minimumMinutes * 60_000
}
