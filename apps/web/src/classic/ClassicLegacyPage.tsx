import {
  DeleteOutlined,
  EditOutlined,
  ExportOutlined,
  FileAddOutlined,
  FileSearchOutlined,
  PrinterOutlined,
  ReloadOutlined,
  SearchOutlined,
  StopOutlined,
  TableOutlined,
} from '@ant-design/icons'
import { Button, Empty, Form, Input, Modal, Select, Space, Tabs, Tag, Tooltip, message } from 'antd'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  classicDefaultHeaders,
  getClassicFeatureMapping,
  type ClassicManifestField,
  type ClassicManifestModule,
  type ClassicManifestPage,
} from './classicManifest'
import { classifyClassicPage, splitClassicControls, uniqueClassicLabels } from './classicPageRules'

const dangerousPattern = /删除|审核|反审|付款|退款|退卡|发送|清账/
const formPattern = /新增|新建|修改|调阅|开单|导入|核算|登记|发放|开卡|储值|预约|护理|兑换|增减|入库|出库|退货|盘点|调价|申请|配送|存款|取款|收入|支出/

const labelAliases: Record<string, string> = {
  search_years: '年份', search_month: '月份', search_area: '所在地区', search_zone: '城市/区域', search_shopv: '所属门店',
  search_bdate: '开始日期', search_edate: '结束日期', search_user: '操作人员', search_key: '关键词', search_name: '名称', search_code: '编号',
  search_shop: '所属门店', search_date: '业务日期', search_mode: '统计方式', shop_name: '所属门店', shop_namei: '调入门店', shop_nameo: '调出门店',
  fund_namei: '收款账户', fund_nameo: '付款账户', scan_bar: '扫码录入', bill_ccheck: '审核状态', bill_peopleM: '男性人数', bill_peopleF: '女性人数',
  bill_peoageM: '男性平均年龄', bill_peoageF: '女性平均年龄', send_hand: '手机号码', send_often: '常用短语', send_text: '短信内容', send_time: '发送时间',
}

function getFieldLabel(field: ClassicManifestField, index: number, page: ClassicManifestPage) {
  return field.label || labelAliases[field.id] || labelAliases[field.name] || page.fieldLabels[index] || field.placeholder || `查询条件 ${index + 1}`
}

function buildFallbackFields(page: ClassicManifestPage): ClassicManifestField[] {
  if (page.fields.length) return page.fields
  if (page.kind === 'query') return [
    { id: 'search_bdate', name: 'search_bdate', label: '开始日期', tag: 'INPUT', type: 'date', placeholder: '', options: [] },
    { id: 'search_edate', name: 'search_edate', label: '结束日期', tag: 'INPUT', type: 'date', placeholder: '', options: [] },
    { id: 'search_key', name: 'search_key', label: '关键词', tag: 'INPUT', type: 'text', placeholder: '请输入编号、名称或手机号', options: [] },
  ]
  return []
}

function FieldControl({ field, label }: { field: ClassicManifestField; label: string }) {
  if (field.options.length || field.tag === 'SELECT') {
    return <Select allowClear showSearch placeholder={field.placeholder || '请选择'} options={field.options.map((option) => ({ value: option, label: option }))} />
  }
  if (field.type === 'file') return <Input type="file" />
  if (/备注|摘要|说明|内容|地址/.test(label)) return <Input.TextArea rows={2} placeholder={field.placeholder || '请输入'} />
  return <Input type={field.type === 'date' ? 'date' : field.type === 'number' ? 'number' : field.type === 'password' ? 'password' : 'text'} placeholder={field.placeholder || '请输入'} />
}

function exportHeaders(label: string, headers: string[]) {
  const csv = `\uFEFF${headers.map((header) => `"${header.replaceAll('"', '""')}"`).join(',')}\n`
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const link = document.createElement('a')
  link.href = URL.createObjectURL(blob)
  link.download = `${label}.csv`
  link.click()
  URL.revokeObjectURL(link.href)
}

const controlIcon = (control: string) => {
  if (control.includes('查询') || control.includes('查找')) return <SearchOutlined />
  if (control.includes('刷新')) return <ReloadOutlined />
  if (control.includes('表格')) return <TableOutlined />
  if (control.includes('打印')) return <PrinterOutlined />
  if (control.includes('导出')) return <ExportOutlined />
  if (control.includes('删除')) return <DeleteOutlined />
  if (control.includes('修改')) return <EditOutlined />
  if (control.includes('新增') || control.includes('新建') || control.includes('开单')) return <FileAddOutlined />
  if (control.includes('退出') || control.includes('取消') || control.includes('关闭')) return <StopOutlined />
  return <FileSearchOutlined />
}

function ClassicFieldGrid({ fields, page, namePrefix = '' }: { fields: ClassicManifestField[]; page: ClassicManifestPage; namePrefix?: string }) {
  return <div className="classic-business-form">{fields.map((field, index) => {
    const label = getFieldLabel(field, index, page)
    return <Form.Item key={`${field.id}-${index}`} name={`${namePrefix}${field.id || field.name || `field-${index}`}`} label={label}><FieldControl field={field} label={label} /></Form.Item>
  })}</div>
}

export function ClassicLegacyPage({ module, page }: { module: ClassicManifestModule; page: ClassicManifestPage }) {
  const navigate = useNavigate()
  const mapping = getClassicFeatureMapping(module.key, page.label)
  const layout = classifyClassicPage(module.key, page)
  const [queryOpen, setQueryOpen] = useState(false)
  const [formOpen, setFormOpen] = useState(false)
  const [compact, setCompact] = useState(true)
  const [lastQuery, setLastQuery] = useState<Record<string, unknown>>()
  const [activeNavigation, setActiveNavigation] = useState<string>()
  const fields = useMemo(() => buildFallbackFields(page).filter((field) => Boolean(field.id || field.name || field.label)), [page])
  const headers = uniqueClassicLabels(page.tableHeaders.length ? page.tableHeaders : classicDefaultHeaders[module.key] ?? ['编号', '名称', '状态'])
  const splitControls = splitClassicControls(page.controls.length ? page.controls : page.kind === 'query' ? ['查询', '刷新', '表格', '打印', '导出', '退出'] : ['新增', '修改', '删除', '查询', '刷新', '退出'])
  const toolbarControls = splitControls.toolbar.length ? splitControls.toolbar : page.controls.slice(0, 12)
  const navigationControls = splitControls.navigation

  const runControl = (control: string) => {
    if (control.includes('查询') || control.includes('查找')) return setQueryOpen(true)
    if (control.includes('刷新')) return void message.success('页面已刷新')
    if (control.includes('表格')) return setCompact((value) => !value)
    if (control.includes('打印')) return window.print()
    if (control.includes('导出')) return exportHeaders(page.label, headers)
    if (control.includes('退出') || control.includes('取消') || control.includes('关闭')) return navigate(`/ui/new/${module.key}`)
    if (dangerousPattern.test(control)) return void message.warning('该动作尚未接入新系统状态机，本页不会写入或破坏业务数据')
    if (/确定|保存|结算/.test(control)) return void message.info('表单结构已完成；后端未接入的页面不会写入数据')
    if (formPattern.test(control) || control.includes('调阅')) return setFormOpen(true)
    return void message.info('该控件位置已复刻，后端能力待接入')
  }

  const grid = <section className={`classic-data-grid ${compact ? 'is-compact' : ''}`}>
    <div className="classic-data-scroll"><table><thead><tr>{headers.map((header) => <th key={header}>{header}</th>)}</tr></thead><tbody><tr><td colSpan={headers.length}><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={mapping.status === 'pending' ? '页面结构已完成，等待后端数据接入' : '当前查询条件下暂无记录'} /></td></tr></tbody></table></div>
    <footer><span>共 0 条记录</span><span>经典页面编号：{page.id}</span><Tag color={mapping.status === 'integrated' ? 'green' : mapping.status === 'partial' ? 'blue' : 'orange'}>{mapping.status === 'integrated' ? '已接入' : mapping.status === 'partial' ? '部分接入' : '待接入'}</Tag></footer>
  </section>

  if (layout === 'form' || layout === 'document') {
    return <div className={`classic-legacy-page classic-legacy-form-page ${layout === 'document' ? 'classic-legacy-document-page' : ''}`}>
      <header className="classic-page-caption"><div><strong>{page.label}</strong><span>{module.label}管理</span></div><Tag color={mapping.status === 'integrated' ? 'green' : mapping.status === 'partial' ? 'blue' : 'orange'}>{mapping.status === 'integrated' ? '已接入' : mapping.status === 'partial' ? '部分接入' : '后端待接入'}</Tag></header>
      {page.tabs.length > 0 && <Tabs size="small" items={page.tabs.map((tab) => ({ key: tab, label: tab, children: null }))} />}
      <Form layout="vertical" className="classic-direct-form" onFinish={() => message.info(mapping.path ? '请进入已接入业务完成提交' : '表单已通过前端校验；当前未写入数据')}>
        <ClassicFieldGrid fields={fields} page={page} />
        {layout === 'document' && <div className="classic-document-lines"><header><strong>单据明细</strong><span>明细操作与原系统位置一致，后端状态机接入后开放写入</span></header>{grid}</div>}
        <footer>{toolbarControls.map((control) => /确定|保存|结算/.test(control) ? <Button key={control} type="primary" htmlType="submit" icon={controlIcon(control)}>{control}</Button> : <Button key={control} icon={controlIcon(control)} onClick={() => runControl(control)}>{control}</Button>)}{mapping.path && <Button onClick={() => navigate(mapping.path!)}>进入已接入业务</Button>}</footer>
      </Form>
      <div className="classic-page-note">{mapping.note}</div>
    </div>
  }

  return <div className={`classic-legacy-page classic-legacy-${layout}-page`}>
    <header className="classic-page-caption"><div><strong>{page.label}</strong><span>{mapping.note}</span></div>{mapping.path && <Button size="small" onClick={() => navigate(mapping.path!)}>进入已接入业务</Button>}</header>
    {page.tabs.length > 0 && <Tabs size="small" items={page.tabs.map((tab) => ({ key: tab, label: tab, children: null }))} />}
    <section className="classic-legacy-toolbar" aria-label={`${page.label}工具栏`}>{toolbarControls.map((control) => {
      const disabled = dangerousPattern.test(control) && mapping.status === 'pending'
      const button = <Button key={control} size="small" icon={controlIcon(control)} disabled={disabled} onClick={() => runControl(control)}>{control}</Button>
      return disabled ? <Tooltip key={control} title="后端状态机和审计尚未接入">{button}</Tooltip> : button
    })}</section>
    {lastQuery && <div className="classic-query-summary"><SearchOutlined /> 已应用 {Object.values(lastQuery).filter(Boolean).length} 个查询条件</div>}
    <div className={`classic-legacy-grid-layout ${navigationControls.length ? 'has-navigation' : ''}`}>
      {navigationControls.length > 0 && <aside className="classic-legacy-tree"><h3>{page.label}分类</h3><button type="button" className={!activeNavigation ? 'active' : ''} onClick={() => setActiveNavigation(undefined)}>全部</button>{navigationControls.map((control) => <button key={control} type="button" className={activeNavigation === control ? 'active' : ''} onClick={() => setActiveNavigation(control)}>{control}</button>)}</aside>}
      <div className="classic-legacy-grid-main">{layout === 'report' && <section className="classic-report-filter"><Form layout="inline"><ClassicFieldGrid fields={fields.slice(0, 8)} page={page} namePrefix="report-" /><Button type="primary" icon={<SearchOutlined />} onClick={() => message.info('报表口径后端待接入')}>查询</Button></Form></section>}{grid}</div>
    </div>

    <Modal title={`${page.label} · 查询条件`} open={queryOpen} onCancel={() => setQueryOpen(false)} footer={null} destroyOnHidden width={fields.length > 8 ? 820 : 620}>
      <Form layout="vertical" onFinish={(values) => { setLastQuery(values); setQueryOpen(false); message.success('查询条件已应用') }}><ClassicFieldGrid fields={fields} page={page} namePrefix="query-" /><Space><Button type="primary" htmlType="submit">确定</Button><Button onClick={() => setQueryOpen(false)}>取消</Button><Button htmlType="reset">清空</Button></Space></Form>
    </Modal>

    <Modal title={`${page.label} · 业务表单`} open={formOpen} onCancel={() => setFormOpen(false)} footer={<Space><Button onClick={() => setFormOpen(false)}>关闭</Button>{mapping.path && <Button type="primary" onClick={() => navigate(mapping.path!)}>进入已接入业务</Button>}</Space>} width={820}>
      {fields.length ? <Form layout="vertical"><ClassicFieldGrid fields={fields} page={page} namePrefix="business-" /></Form> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="参考系统未直接暴露该表单字段；已登记为后端与字段设计缺口" />}
      <div className="classic-modal-note">本窗口只复刻字段与交互位置，不会向旧系统或新系统写入数据。</div>
    </Modal>
  </div>
}
