import { readFile, writeFile } from 'node:fs/promises'

const sourcePath = new URL('../design-audit/legacy-ui-inventory.json', import.meta.url)
const targetPath = new URL('../apps/web/src/classic/classicUiManifest.json', import.meta.url)

const source = JSON.parse(await readFile(sourcePath, 'utf8'))

const keyMap = {
  pos: 'cashier',
  vip: 'customer',
  sales: 'promotion',
  buy: 'purchase',
  sell: 'sales',
  depot: 'inventory',
  joins: 'distribution',
  pay: 'employee',
  fund: 'finance',
  report: 'reports',
  boss: 'decision',
  sms: 'sms',
}

const queryPattern = /查询|统计|报表|明细|汇总|排行|排名|分析|状况|余额|占比|趋势|结构|分布/
const excludedActionPattern = /rightToggle/

const cleanField = (field) => ({
  id: field.id || field.name || '',
  label: field.label || '',
  name: field.name || '',
  tag: field.tag || 'INPUT',
  type: field.type || 'text',
  placeholder: field.placeholder || '',
  options: Array.isArray(field.options) ? field.options : [],
})

const uniqueStrings = (items) => [...new Set(items.filter(Boolean))]

const modules = Object.entries(source.modules).map(([legacyKey, module]) => {
  const originalPages = source.pages[legacyKey] || []
  const directActions = module.actions.filter((action) =>
    action.text && action.text !== '查看更多报表' && action.text !== '查询' && !excludedActionPattern.test(action.onclick || ''),
  )
  const pageLabels = new Set(originalPages.map((page) => page.label))
  const unmatchedActions = directActions.filter((action) => !pageLabels.has(action.text))
  const runtimeQueries = source.runtimeQueries?.[legacyKey] || []

  const pages = originalPages.map((page, index) => {
    const label = page.label === '查询' && unmatchedActions.length ? unmatchedActions.shift().text : page.label
    const runtimeQuery = runtimeQueries.find((query) => query.label === label)
    const fields = [...(page.fields || []).map(cleanField)]
    for (const runtimeField of runtimeQuery?.fields || []) {
      const normalized = cleanField(runtimeField)
      if (!fields.some((field) => (field.id || field.name) === (normalized.id || normalized.name))) fields.push(normalized)
    }

    return {
      id: `${keyMap[legacyKey]}-${String(index + 1).padStart(3, '0')}`,
      label,
      kind: queryPattern.test(label) ? 'query' : 'management',
      controls: uniqueStrings((page.controls || []).map((control) => control.text)),
      fields,
      fieldLabels: uniqueStrings([...(runtimeQuery?.labels || []), ...(page.headings || [])]),
      tableHeaders: uniqueStrings(page.tableHeaders || []),
      tabs: uniqueStrings(page.tabs || []),
      sourceTitle: page.title || '',
      backendStatus: 'pending',
    }
  })
  const actionOrder = new Map(directActions.map((action, index) => [action.text, index]))
  pages.sort((left, right) => (actionOrder.get(left.label) ?? Number.MAX_SAFE_INTEGER) - (actionOrder.get(right.label) ?? Number.MAX_SAFE_INTEGER))

  return {
    key: keyMap[legacyKey],
    legacyKey,
    label: module.label,
    pages,
  }
})

const manifest = {
  schemaVersion: 1,
  sourceSummary: {
    moduleCount: modules.length,
    pageCount: modules.reduce((sum, module) => sum + module.pages.length, 0),
    excludedModules: source.exclusions,
  },
  modules,
}

await writeFile(targetPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8')
console.log(`Generated ${manifest.sourceSummary.pageCount} classic UI pages at ${targetPath.pathname}`)
