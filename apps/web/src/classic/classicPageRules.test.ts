import { describe, expect, it } from 'vitest'
import type { ClassicManifestPage } from './classicManifest'
import { classicUiManifest } from './classicManifest'
import { classifyClassicPage, splitClassicControls, uniqueClassicLabels } from './classicPageRules'

const page = (partial: Partial<ClassicManifestPage>): ClassicManifestPage => ({
  id: 'page-001', label: '示例', kind: 'management', controls: [], fields: [], fieldLabels: [], tableHeaders: [], tabs: [], sourceTitle: '思维云店', backendStatus: 'pending', ...partial,
})

describe('classicPageRules', () => {
  it('区分业务表单、数据表格和报表页，并把树节点从工具栏分离', () => {
    expect(classifyClassicPage('customer', page({ fields: [{ id: 'name', label: '姓名', name: '', tag: 'INPUT', type: 'text', placeholder: '', options: [] }], controls: ['确定', '取消'] }))).toBe('form')
    expect(classifyClassicPage('promotion', page({ fields: [{ id: 'name', label: '方案', name: '', tag: 'INPUT', type: 'text', placeholder: '', options: [] }], tableHeaders: ['商品名称'], controls: ['保存单据', '退出'] }))).toBe('document')
    expect(classifyClassicPage('inventory', page({ kind: 'query', controls: ['查询', '刷新'] }))).toBe('grid')
    expect(classifyClassicPage('reports', page({ kind: 'query', controls: ['查询'] }))).toBe('report')
    expect(splitClassicControls(['新增', '修改', '产品分类', '护理用品']).toolbar).toEqual(['新增', '修改'])
    expect(splitClassicControls(['新增', '修改', '产品分类', '护理用品']).navigation).toEqual(['产品分类', '护理用品'])
    expect(uniqueClassicLabels(['门店', '门店', '', '金额'])).toEqual(['门店', '金额'])
  })

  it('覆盖清单内全部经典页面', () => {
    const pages = classicUiManifest.modules.flatMap((module) => module.pages.map((item) => ({ moduleKey: module.key, page: item })))
    expect(pages).toHaveLength(199)
    expect(pages.every(({ moduleKey, page: item }) => Boolean(classifyClassicPage(moduleKey, item)) && item.controls.length > 0)).toBe(true)
    expect(new Set(pages.map(({ page: item }) => item.id)).size).toBe(199)
  })
})
