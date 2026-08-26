import rawManifest from './classicUiManifest.json'

export type ClassicBackendStatus = 'integrated' | 'partial' | 'pending'

export interface ClassicManifestField {
  id: string
  label: string
  name: string
  tag: string
  type: string
  placeholder: string
  options: string[]
}

export interface ClassicManifestPage {
  id: string
  label: string
  kind: 'management' | 'query'
  controls: string[]
  fields: ClassicManifestField[]
  fieldLabels: string[]
  tableHeaders: string[]
  tabs: string[]
  sourceTitle: string
  backendStatus: string
}

export interface ClassicManifestModule {
  key: string
  legacyKey: string
  label: string
  pages: ClassicManifestPage[]
}

export interface ClassicFeatureMapping {
  path?: string
  status: ClassicBackendStatus
  note: string
}

interface ClassicUiManifest {
  schemaVersion: number
  sourceSummary: {
    moduleCount: number
    pageCount: number
    excludedModules: string[]
  }
  modules: ClassicManifestModule[]
}

export const classicUiManifest = rawManifest as ClassicUiManifest

export const getClassicManifestModule = (moduleKey: string) =>
  classicUiManifest.modules.find((module) => module.key === moduleKey)

export const getClassicManifestPage = (moduleKey: string, pageId: string) =>
  getClassicManifestModule(moduleKey)?.pages.find((page) => page.id === pageId)

const contains = (label: string, values: string[]) => values.some((value) => label.includes(value))

export function getClassicFeatureMapping(moduleKey: string, label: string): ClassicFeatureMapping {
  switch (moduleKey) {
    case 'cashier':
      if (contains(label, ['预约'])) return { path: '/ui/new/cashier/scheduling', status: 'integrated', note: '已接入预约与排班能力。' }
      if (contains(label, ['护理', '开卡', '储值', '积分', '兑换'])) return { path: '/ui/new/customer/list', status: 'partial', note: '已接入顾客、会员与服务记录；旧版专用单据字段仍按页面清单补齐。' }
      if (contains(label, ['交班'])) return { path: '/ui/new/finance/checkout', status: 'integrated', note: '已接入交班与复核流程。' }
      return { path: '/ui/new/cashier/checkout', status: 'partial', note: '已接入服务录单与收银；旧版单据布局由本页承接。' }
    case 'customer':
      return { path: '/ui/new/customer/list', status: 'partial', note: '已接入顾客、会员、储值和服务档案；专项退款与统计仍需逐项补齐。' }
    case 'promotion':
      if (contains(label, ['服务'])) return { path: '/ui/new/promotion/services', status: 'partial', note: '已接入服务项目，促销规则后端仍待补齐。' }
      if (contains(label, ['产品'])) return { path: '/ui/new/promotion/products', status: 'partial', note: '已接入产品目录，促销规则后端仍待补齐。' }
      return { path: '/ui/new/promotion/prices', status: 'partial', note: '已接入价格版本；促销开单和适用规则待接入。' }
    case 'purchase':
      return { path: '/ui/new/purchase/manage', status: 'partial', note: '已接入供应链和入库批次；旧版订单、退货、付款单据链待补齐。' }
    case 'sales':
      return { path: '/ui/new/sales/orders', status: 'partial', note: '已接入商品/服务录单与收款；旧版销售单据链待补齐。' }
    case 'inventory':
      return { path: '/ui/new/inventory/manage', status: 'partial', note: '已接入库存余额和库存调整；旧版专项单据与报表待补齐。' }
    case 'distribution':
      return { path: '/ui/new/distribution/manage', status: 'partial', note: '已接入品牌内门店调拨；配送申请和退货状态机待补齐。' }
    case 'employee':
      return { path: '/ui/new/employee/manage', status: 'partial', note: '已接入员工、账号与权限；工资、奖惩和个税流程待接入。' }
    case 'finance':
      if (contains(label, ['交班', '结算'])) return { path: '/ui/new/finance/checkout', status: 'partial', note: '已接入交班与对账，其他财务单据仍待补齐。' }
      return { path: '/ui/new/finance/reports', status: 'partial', note: '已接入经营报表；会计类单据和专项口径待补齐。' }
    case 'reports':
    case 'decision':
      return { path: '/ui/new/reports/operations', status: 'partial', note: '已接入经营总览和门店汇总；本页专项口径与导出需逐项对齐。' }
    case 'sms':
      return { status: 'pending', note: '短信供应商、模板、余额和发送审计尚未接入；当前仅完成经典页面与交互位置。' }
    default:
      return { status: 'pending', note: '后端能力尚未接入。' }
  }
}

export const classicDefaultHeaders: Record<string, string[]> = {
  cashier: ['单据号', '日期', '顾客', '业务类型', '金额', '状态'],
  customer: ['顾客编号', '顾客名称', '手机号', '会员类型', '储值余额', '状态'],
  promotion: ['活动编号', '活动名称', '适用范围', '开始日期', '结束日期', '状态'],
  purchase: ['单号', '日期', '供应商', '经办人', '货品总额', '优惠金额', '应付金额', '已付金额', '制单人', '审核人'],
  sales: ['单号', '日期', '客户', '经办人', '销售金额', '优惠金额', '应收金额', '已收金额', '制单人', '审核人'],
  inventory: ['商品编码', '商品名称', '规格', '单位', '期初数量', '入库数量', '出库数量', '结存数量'],
  distribution: ['调拨单号', '申请门店', '配送门店', '申请日期', '商品数量', '金额', '状态'],
  employee: ['员工编号', '员工姓名', '门店', '职位', '业绩金额', '提成金额', '状态'],
  finance: ['单据号', '业务日期', '门店', '收支类型', '金额', '经办人', '审核状态'],
  reports: ['序号', '门店编号', '门店名称', '本期金额', '上期金额', '同比', '环比'],
  decision: ['序号', '分析对象', '指标值', '占比', '排名', '变化趋势'],
  sms: ['发送编号', '发送时间', '接收对象', '短信类型', '条数', '发送状态'],
}
