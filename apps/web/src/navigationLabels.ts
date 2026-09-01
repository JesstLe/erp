export const defaultNavigationLabels: Record<string, string> = {
  '/': '经营工作台',
  '/facilities': '设施接待',
  '/scheduling': '预约与排班',
  '/customers': '顾客与会员',
  '/cashier': '服务录单与收银',
  '/inventory': '商品库存',
  '/supply-chain': '采购与供应链',
  '/catalog/items': '服务项目',
  '/catalog/products': '产品目录',
  '/catalog/prices': '价格管理',
  '/reports': '经营报表',
  '/audit': '审计记录',
  '/settings/facilities': '门店设施配置',
  '/settings/organization': '品牌与门店',
  '/settings/employees': '员工与权限',
  '/settings/payment-channels': '支付渠道配置',
}

export const configurableNavigationItems = Object.entries(defaultNavigationLabels)
  .filter(([key]) => key !== '/cashier')
  .map(([key, label]) => ({ key, label }))

export function resolveNavigationLabel(key: string, labels?: Record<string, string>) {
  return labels?.[key]?.trim() || defaultNavigationLabels[key] || key
}
