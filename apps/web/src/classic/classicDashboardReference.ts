export type ClassicDashboardLayout = 'cashier' | 'standard' | 'single-chart' | 'analysis'

export interface ClassicDashboardReference {
  legacyKey: string
  layout: ClassicDashboardLayout
  chartTitles: readonly string[]
  latestTitle?: string
  latestHeaders?: readonly string[]
  managementTitle: string
  managementActionCount: number
  queryTitle?: string
  chartDataMode: 'operations' | 'empty'
}

/**
 * Browser-visible reference captured from the authorised legacy ERP.
 *
 * Titles confirmed by the saved screenshots use their original wording. Where the
 * legacy chart title only exists inside the authenticated outer-frame script, the
 * closest module wording is used until that script can be inspected after login.
 */
export const classicDashboardReference = {
  cashier: {
    legacyKey: 'pos',
    layout: 'cashier',
    chartTitles: [],
    managementTitle: '前台收银',
    managementActionCount: 10,
    chartDataMode: 'empty',
  },
  customer: {
    legacyKey: 'vip',
    layout: 'standard',
    chartTitles: ['顾客卡类占比', '顾客登记趋势图表'],
    latestTitle: '最新登记顾客列表',
    latestHeaders: ['会员卡号', '会员姓名', '性别', '手机号码', '办卡门店', '会员卡类', '来店渠道', '登记时间'],
    managementTitle: '顾客管理',
    managementActionCount: 11,
    queryTitle: '报表查询',
    chartDataMode: 'empty',
  },
  promotion: {
    legacyKey: 'sales',
    layout: 'standard',
    chartTitles: ['促销项目占比', '促销期间金额图表'],
    latestTitle: '最新门店促销单列表',
    latestHeaders: ['单号', '日期', '所属门店', '促销方案', '开始日期', '截止日期', '开始时段', '截止时段', '制单人', '审核人'],
    managementTitle: '促销管理',
    managementActionCount: 1,
    queryTitle: '报表查询',
    chartDataMode: 'empty',
  },
  purchase: {
    legacyKey: 'buy',
    layout: 'standard',
    chartTitles: ['产品分类进货占比', '本年进货金额图表'],
    latestTitle: '最新进货入库单列表',
    latestHeaders: ['单号', '日期', '供应商', '经办人', '货品总额', '优惠金额', '应付金额', '已付金额', '制单人', '审核人'],
    managementTitle: '进货管理',
    managementActionCount: 4,
    queryTitle: '报表查询',
    chartDataMode: 'empty',
  },
  sales: {
    legacyKey: 'sell',
    layout: 'standard',
    chartTitles: ['产品分类销售占比', '本年销售金额图表'],
    latestTitle: '最新销售出库单列表',
    latestHeaders: ['单号', '日期', '销售客户', '经办人', '货品总额', '优惠金额', '应收金额', '已收金额', '制单人', '审核人'],
    managementTitle: '销售管理',
    managementActionCount: 4,
    queryTitle: '报表查询',
    chartDataMode: 'empty',
  },
  inventory: {
    legacyKey: 'depot',
    layout: 'standard',
    chartTitles: ['产品分类库存占比', '产品分类库存图表'],
    latestTitle: '库存最多商品排行',
    latestHeaders: ['所属门店', '产品分类', '产品品牌', '产品编号', '产品名称', '规格', '单位', '库存数量'],
    managementTitle: '库存管理',
    managementActionCount: 7,
    queryTitle: '报表查询',
    chartDataMode: 'empty',
  },
  distribution: {
    legacyKey: 'joins',
    layout: 'standard',
    chartTitles: ['门店配送占比', '本年门店配送金额图表'],
    latestTitle: '最新门店配送单列表',
    latestHeaders: ['单号', '日期', '调入门店', '货品总额', '优惠金额', '应收金额', '已收金额', '制单人', '审核人', '验收人'],
    managementTitle: '配货管理',
    managementActionCount: 4,
    queryTitle: '报表查询',
    chartDataMode: 'empty',
  },
  employee: {
    legacyKey: 'pay',
    layout: 'single-chart',
    chartTitles: ['本月员工综合业绩图表'],
    latestTitle: '最新登记员工列表',
    latestHeaders: ['所属门店', '员工编号', '员工姓名', '性别', '手机号码', '员工岗位', '在职状态', '入职日期'],
    managementTitle: '员工管理',
    managementActionCount: 5,
    queryTitle: '报表查询',
    chartDataMode: 'empty',
  },
  finance: {
    legacyKey: 'fund',
    layout: 'standard',
    chartTitles: ['今日收银情况占比', '本月费用开支金额图表'],
    latestTitle: '最新费用开支单列表',
    latestHeaders: ['单号', '日期', '所属门店', '经办人', '付款账户', '付款金额', '制单人', '审核人'],
    managementTitle: '财务管理',
    managementActionCount: 7,
    queryTitle: '报表查询',
    chartDataMode: 'operations',
  },
  reports: {
    legacyKey: 'report',
    layout: 'analysis',
    chartTitles: ['收入情况占比', '项目消费图表', '顾客消费走势'],
    managementTitle: '项目分析',
    managementActionCount: 13,
    queryTitle: '经营分析',
    chartDataMode: 'operations',
  },
  decision: {
    legacyKey: 'boss',
    layout: 'analysis',
    chartTitles: ['收入情况占比', '项目消费图表', '顾客消费走势'],
    managementTitle: '决策支持',
    managementActionCount: 14,
    queryTitle: '经营分析',
    chartDataMode: 'operations',
  },
  sms: {
    legacyKey: 'sms',
    layout: 'standard',
    chartTitles: ['短信发送对象占比', '本月短信发送图表'],
    latestTitle: '最新发送短信列表',
    latestHeaders: ['所属门店', '手机号码', '短信内容', '发送时间', '状态'],
    managementTitle: '短信管理',
    managementActionCount: 5,
    queryTitle: '短信设置',
    chartDataMode: 'empty',
  },
} as const satisfies Record<string, ClassicDashboardReference>
