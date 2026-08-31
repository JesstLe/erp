export function buildManualPaymentReference(methodCode: string, reference?: string, groupBuyPlatform?: string) {
  const normalizedReference = reference?.trim() ?? ''
  return methodCode === 'GROUP_BUY_MANUAL'
    ? `${groupBuyPlatform?.trim() ?? ''}:${normalizedReference}`
    : normalizedReference
}
