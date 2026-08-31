export function normalizeExpectedDurationMinutes(minutes?: number): number | null {
  if (minutes === undefined || !Number.isInteger(minutes)) return null
  return minutes >= 1 && minutes <= 1440 ? minutes : null
}
