interface AllocationInput { methodId?: string; amountYuan?: number }
interface MethodInput { id: string; category: string }

export function cashAmountMinor(allocations: AllocationInput[], methods: MethodInput[]): number {
  return allocations.reduce((sum, line) => {
    const method = methods.find((item) => item.id === line.methodId)
    return method?.category === 'Cash' ? sum + Math.round(Number(line.amountYuan ?? 0) * 100) : sum
  }, 0)
}

export function cashTenderedMinorForSubmission(allocations: AllocationInput[], methods: MethodInput[],
  cashTenderedYuan?: number): number | null {
  if (cashAmountMinor(allocations, methods) === 0 || cashTenderedYuan === undefined) return null
  return Math.round(cashTenderedYuan * 100)
}
