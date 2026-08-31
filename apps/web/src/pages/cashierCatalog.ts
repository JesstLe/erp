import type { InventoryBalance, PriceBook, ProductItem, ServiceItem } from '../api/types'

export function buildCashierServiceCatalog(items: ServiceItem[] | undefined, priceBook: PriceBook | undefined) {
  const prices = new Map((priceBook?.lines ?? []).map((line) => [line.serviceItemId, line.unitPriceMinor]))
  return (items ?? [])
    .filter((item) => item.status.toUpperCase() === 'ENABLED')
    .map((item) => ({
      id: item.id,
      code: item.code,
      name: item.name,
      duration: item.standardDurationMinutes,
      priceMinor: prices.get(item.id) ?? 0,
      hasPublishedPrice: prices.has(item.id),
    }))
}

export function buildCashierProductCatalog(
  items: ProductItem[] | undefined,
  inventory: InventoryBalance[] | undefined,
  priceBook: PriceBook | undefined,
) {
  const prices = new Map((priceBook?.productLines ?? []).map((line) => [line.productItemId, line.unitPriceMinor]))
  return (items ?? [])
    .filter((item) => item.status.toUpperCase() === 'ENABLED')
    .map((item) => ({
      id: item.id,
      code: item.code,
      name: item.name,
      unitName: item.unitName,
      priceMinor: prices.get(item.id) ?? 0,
      hasPublishedPrice: prices.has(item.id),
      stock: inventory?.find((entry) => entry.productItemId === item.id)?.availableQuantity,
    }))
}
