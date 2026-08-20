export const Permission = {
  DashboardRead: 'dashboard.read',
  CatalogRead: 'catalog.read',
  CatalogWrite: 'catalog.write',
  PricePublish: 'price.publish',
  FacilityOperate: 'facility.operate',
  FacilityConfigure: 'facility.configure',
  FacilityConfigureAllStores: 'facility.configure.all-stores',
  SchedulingOperate: 'scheduling.operate',
  SchedulingShiftManage: 'scheduling.shift.manage',
  CustomerRead: 'customer.read',
  CustomerWrite: 'customer.write',
  CustomerManage: 'customer.manage',
  CustomerExport: 'customer.export',
  CustomerMerge: 'customer.merge',
  CustomerExportFullMobile: 'customer.export.full-mobile',
  MembershipOpen: 'membership.open',
  MembershipCardTypeManage: 'membership.card-type.manage',
  MembershipTopup: 'membership.topup',
  MembershipManage: 'membership.manage',
  MembershipAdmin: 'membership.admin',
  MembershipGrantBonus: 'membership.grant-bonus',
  MembershipReverse: 'membership.reverse',
  ServiceRecordManage: 'service-record.manage',
  CashierCheckout: 'cashier.checkout',
  CashierApprovePrice: 'cashier.approve-price',
  RefundApprove: 'refund.approve',
  RefundRequest: 'refund.request',
  ShiftReview: 'shift.review',
  InventoryRead: 'inventory.read',
  InventoryWrite: 'inventory.write',
  SupplyChainRead: 'supply-chain.read',
  SupplyChainOperate: 'supply-chain.operate',
  SupplyChainManage: 'supply-chain.manage',
  ReportRead: 'report.read',
  AuditRead: 'audit.read',
  OrganizationManage: 'organization.manage',
  EmployeeManage: 'employee.manage',
  PaymentChannelRead: 'payment-channel.read',
  PaymentChannelManage: 'payment-channel.manage',
} as const

export type PermissionCode = typeof Permission[keyof typeof Permission]

export interface AuthorizationBypassEnvironment {
  DEV: boolean
  VITE_LOCAL_AUTHORIZATION_BYPASS?: string
}

export function isLocalAuthorizationBypassEnabled(environment: AuthorizationBypassEnvironment): boolean {
  return environment.DEV && environment.VITE_LOCAL_AUTHORIZATION_BYPASS === 'true'
}

export function hasPermission(
  granted: readonly string[] | undefined,
  required: PermissionCode,
  bypass = false,
): boolean {
  return bypass || (granted?.includes(required) ?? false)
}
