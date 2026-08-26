import type { ClassicManifestPage } from './classicManifest'

export type ClassicPageLayout = 'form' | 'document' | 'grid' | 'report'

const toolbarPattern = /新增|新建|修改|批量|删除|查询|查找|导入|刷新|表格|打印|导出|退出|调阅|审核|反审|结算|保存|确定|取消|关闭|开单|入库|出库|退货|盘点|调价|申请|配送|登记|发放|核算|还款|退款|退卡|兑换|增减|发送|清账|付款|收款|存款|取款/
const reportModulePattern = /^(reports|decision)$/

export function classifyClassicPage(moduleKey: string, page: ClassicManifestPage): ClassicPageLayout {
  const meaningfulControls = page.controls.filter((control) => !/取消|关闭|退出/.test(control))
  if (page.kind === 'management' && page.fields.length > 0 && page.tableHeaders.length > 0) return 'document'
  if (page.fields.length > 0 && meaningfulControls.length > 0 && meaningfulControls.every((control) => /确定|保存|结算/.test(control)) && page.kind === 'management') return 'form'
  if (reportModulePattern.test(moduleKey)) return 'report'
  return 'grid'
}

export function splitClassicControls(controls: string[]) {
  const toolbar: string[] = []
  const navigation: string[] = []
  for (const control of controls) {
    if (/Toggle Expand Collapse Grid/i.test(control)) continue
    if (toolbarPattern.test(control)) toolbar.push(control)
    else navigation.push(control)
  }
  return { toolbar, navigation }
}

export function uniqueClassicLabels(labels: string[]) {
  return labels.filter((label, index) => Boolean(label.trim()) && labels.indexOf(label) === index)
}
