import { readFileSync } from 'node:fs'
import { writeFileSync } from 'node:fs'

const manifest = JSON.parse(readFileSync(new URL('./classicUiManifest.json', import.meta.url), 'utf8'))

export const classicQaPages = manifest.modules.flatMap((module) =>
  module.pages.map((page) => ({
    moduleKey: module.key,
    pageId: page.id,
    label: page.label,
    controls: page.controls.length,
    fields: page.fields.length,
    headers: page.tableHeaders.length,
  })),
)

export function saveClassicQaScreenshot(path, bytes) {
  writeFileSync(path, bytes)
}
