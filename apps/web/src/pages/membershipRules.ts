interface RefundAllocation { id: string; amountMinor: number }

export function buildRemainingRefundLines(allocations: RefundAllocation[], alreadyRefundedMinor: number,
  requestedMinor: number): { originalAllocationId: string; amountMinor: number }[] {
  if (!Number.isSafeInteger(requestedMinor) || requestedMinor <= 0 || alreadyRefundedMinor < 0)
    throw new Error('退款金额无效')
  let remaining = requestedMinor
  let previouslyRefunded = alreadyRefundedMinor
  const lines = allocations.flatMap((line) => {
    const alreadyUsed = Math.min(previouslyRefunded, line.amountMinor)
    previouslyRefunded -= alreadyUsed
    const amountMinor = Math.min(remaining, line.amountMinor - alreadyUsed)
    remaining -= amountMinor
    return amountMinor > 0 ? [{ originalAllocationId: line.id, amountMinor }] : []
  })
  if (remaining !== 0) throw new Error('可退支付分摊不足，请刷新后重试')
  return lines
}

export function isServicePassDue(status: string, validTo: string | undefined, localDate: string): boolean {
  return status === 'Active' && Boolean(validTo && validTo < localDate)
}
