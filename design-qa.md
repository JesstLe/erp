# Login redesign — Design QA

- Source visual truth: `/Users/lv/.codex/generated_images/01a010a9-8dad-7aa3-8252-0efe73bee46a/exec-f71f7b09-9d53-40d4-8e0b-1409a72e0a24.png`
- Implementation screenshot: `/Users/lv/Workspace/erp/docs/design-qa/login-implementation-1440x1024.png`
- Full-view comparison evidence: `/Users/lv/Workspace/erp/docs/design-qa/login-design-comparison.png`
- Mobile evidence: `/Users/lv/Workspace/erp/docs/design-qa/login-implementation-mobile-390x844.png`
- Viewport/state: desktop 1440 × 1024 CSS px, DPR 1, unauthenticated idle login state; mobile 390 × 844 CSS px.
- Density normalization: source 1487 × 1058 px was normalized to 1440 × 1024 px; implementation is 1440 × 1024 px at DPR 1.

## Findings

No actionable P0/P1/P2 mismatch remains.

- Fonts and typography: the implementation uses the product's existing Inter / PingFang SC / Microsoft YaHei stack. Display size, weight and hierarchy closely match the selected visual. Small rasterization differences from the generated source are acceptable P3 variation.
- Spacing and layout rhythm: 61/39 split, 410 px form width, input heights, CTA height, divider, whitespace and footer placement match the source composition. No clipping or unintended scroll occurs.
- Colors and visual tokens: pearl background, cyan/blue/violet artwork and raster gradient CTA preserve the source palette and contrast. Focus, hover and active states remain visible.
- Image quality and asset fidelity: the decorative artwork and button color field use dedicated generated raster assets; the supplied lightning mark uses the original repository SVG. No CSS/div illustration substitutes or placeholder imagery remain.
- Copy and content: all visible login copy matches the selected direction, while the recovery dialog truthfully reflects the current password-reset capability.
- Icons and accessibility: Ant Design icons provide a consistent stroke family; inputs retain explicit labels and autocomplete attributes, controls are keyboard reachable, focus outlines remain present, and decorative art is excluded from the accessibility tree.
- Responsiveness: at 390 × 844 the decorative panel collapses, the form remains fully usable, and document width equals viewport width with no horizontal overflow.

Focused region comparison was not required after normalization: the full 1440 × 1024 comparison keeps the logo, form labels, icons, input borders, checkbox, CTA and footer text legible at review scale.

## Comparison history

1. Initial implementation: the CTA was solid violet and the form began roughly 40 px farther right than the source. These were P2 fidelity issues.
2. Fixes: added a dedicated teal-to-violet raster CTA asset; changed the form panel alignment and constrained the form to 410 px.
3. Post-fix evidence: the final comparison shows the form beginning within approximately 5 px of the normalized source alignment, with matching control widths and the intended CTA palette. No actionable P0/P1/P2 issue remains.

## Primary interactions tested

- Empty submit displays both required-field messages.
- Password visibility control renders and remains interactive.
- “记住账号” checkbox changes state.
- “忘记密码？” opens and closes the recovery guidance dialog.
- “立即注册” navigates to the merchant registration screen and browser back returns to login.
- Browser console errors checked: none.

## Follow-up polish

- P3: exact glyph antialiasing may vary across Windows and macOS because the preferred Chinese system font differs.

final result: passed

---

# Modern facility cashier using classic workflow — Design QA

- Source visual truth: `/Users/lv/Workspace/erp/design-audit/flow-compare-2026-08-27/05-classic-service-counter-1280.png`
- Browser-rendered implementation: `/Users/lv/Workspace/erp/design-audit/flow-compare-2026-08-27/10-modern-service-counter-qa.png`
- Full-view comparison evidence: `/Users/lv/Workspace/erp/design-audit/flow-compare-2026-08-27/09-classic-modern-comparison-final.png`
- Viewport/state: 1280 × 720 CSS px at DPR 1, authenticated OWNER, active facility with a persisted empty draft after the interaction test was cleaned up.
- Density normalization: source and implementation were captured at the same 1280 × 720 viewport. The modern product shell intentionally keeps its existing white sidebar and header; the compared operation area preserves the classic information architecture inside that shell.

## Findings

No actionable P0/P1/P2 mismatch remains in the approved scope of classic workflow and field-order parity inside the modern visual system.

- Information architecture: the top sequence is 主单信息、顾客预约、会员刷卡、项目列表、产品列表、结算. The center keeps the bill/totals on the left and the active form or catalog on the right. The bottom sequence keeps whole-order employee, consultant, discount, facility switch, merge, prebill, void/clear, pause/resume, end service and return actions.
- Layout and scrolling: the workbench is fixed to the usable viewport beneath the modern header. The action footer remains visible at 1280 × 720; the dense 主单信息 panel scrolls independently (`clientHeight=371`, `scrollHeight=708`, `overflow=auto`) so no reception field is inaccessible.
- Typography, spacing and tokens: the classic compact operation density and ordering are retained, while borders, control radii, colors and surfaces use the existing modern UI tokens as explicitly requested.
- Content and field fidelity: customer, notes, source, manual ticket number, male/female guest counts and age bands are present. Catalog cards retain code, name, price and duration/stock; the bill retains quantity, employee, original price, actual price and line amount.
- Backend truthfulness: refresh restores a server-side versioned draft. Employee, consultant, discount, facility change, merge, prebill, end-service and settlement controls call real APIs rather than simulating success. Official payment and complex allocation continue through the existing authoritative cashier flow.
- Accessibility and states: tabs, catalog items and footer controls are semantic interactive controls; loading, empty, conflict and permission states are visible. The browser console had zero errors or warnings after the final pass.

The full 1280 × 720 comparison keeps the top field order, center two-column structure and bottom action order legible. The dense 主单信息 fields were additionally opened and inspected in-browser, and their scroll measurements were recorded above; a resampled crop was not needed.

## Comparison history

1. Initial modern pass reproduced the classic zones but the persistent footer was clipped at 720 px and the 主单信息 fields extended below an unscrollable panel. These were P2 and P1 usability mismatches.
2. Fixes constrained the workbench to the available viewport, made its body min-height safe, pinned the footer inside the workbench and gave the active detail panel independent vertical scrolling.
3. The final capture shows all persistent actions without clipping, and browser measurement confirms every reception field remains reachable without moving the application shell.

## Primary interactions and checks

- Entered an active facility from the modern facilities page and restored the same persisted draft after reload.
- Opened 主单信息 and verified all reception fields and independent scrolling.
- Added a real service project, selected a whole-order employee and generated a server-side prebill snapshot.
- Reloaded and verified the ¥100 draft restored; then cleared the test draft and confirmed the local workbench returned to ¥0.
- Real PostgreSQL integration flow passed facility start, draft restore, standalone product order, service/employee/consultant/reception update, bill merge, prebill, facility end, confirmation, shift and payment.
- Domain cashier tests passed 17/17. The targeted real-API PostgreSQL flow passed 1/1. Frontend production build and API compile passed with zero warnings/errors; production dependency audit found zero vulnerabilities.
- Browser console errors and warnings checked: none.

## Intentional differences

- P3: the surrounding modern white sidebar/header, rounded controls and modern color tokens remain by design. The user requested classic operation logic and layout inside the modern UI, not a second visual copy of the classic shell.

final result: passed

---

# Classic UI module-specific dashboard rebuild — Design QA

- Source visual truth: `/Users/lv/Workspace/erp/design-audit/02-purchase-dashboard.png` and `/Users/lv/Workspace/erp/design-audit/08-reports-dashboard.png`
- Browser-rendered implementations: `/Users/lv/Workspace/erp/docs/design-qa/classic-reference-rebuild/implementation-purchase-final-1274x660.png` and `/Users/lv/Workspace/erp/docs/design-qa/classic-reference-rebuild/implementation-reports-final-1274x660.png`
- Full-view comparison evidence: `/Users/lv/Workspace/erp/docs/design-qa/classic-reference-rebuild/comparison-purchase-final.png` and `/Users/lv/Workspace/erp/docs/design-qa/classic-reference-rebuild/comparison-reports-final.png`
- Viewport/state: 1274 × 660 CSS px at DPR 1, authenticated OWNER, collapsed “查看更多” state.
- Density normalization: source captures are 1280 × 660 px and implementations are 1274 × 660 px. The six-pixel width difference comes from the in-app browser viewport boundary; height and DPR match, and no vertical resampling was used.

## Findings

The captured purchase and reports layouts have no remaining actionable P0/P1/P2 visual mismatch. The full 12-module handoff remains blocked only because five chart-title pairs are defined in the authenticated outer-frame script and have not yet been read directly.

- Fonts and typography: compact Arial / Microsoft YaHei typography, regular panel headings, 12 px navigation/table density and muted gray labels match the source.
- Spacing and layout rhythm: purchase uses two top charts, one lower list and two equal-height right groups; reports uses two top analyses and one full-width lower trend without a fabricated latest-record table. The accepted classic shell retains its store/account bar above the source content.
- Colors and visual tokens: blue rail, gray canvas, white square panels, gray group headings, blue series and green expand controls follow the reference palette.
- Image quality and assets: the screens rely on the repository lightning mark, Ant Design icons and Recharts output. No placeholder illustration, emoji or hand-built SVG is used.
- Copy and content: captured list titles, table headings, group titles, button order and expand labels are preserved. Business rows and customer data are deliberately absent from the reference package.
- Accessibility and interaction: semantic buttons remain keyboard reachable. “查看更多报表” expands to the full captured list and changes to “收起”; the representative interaction passed and the browser console reported zero errors.

Focused crops were not required: at the original 660 px height the combined full-view evidence keeps chart titles, table fields, group labels and expand controls legible.

## Comparison history

1. Initial implementation used the same category-share, amount chart and latest-list template for every module. This was a P1 information-architecture and fidelity mismatch.
2. First fix introduced four real structures: cashier workbench, standard two-chart dashboard, employee single-chart dashboard and report/decision three-chart analysis.
3. First comparison found P2 drift in empty donut shape, analysis period controls, report fallback time axis and right-group overflow.
4. Final fixes changed the empty donut to the captured 270-degree form, restored legends and period selectors, used a 31-day report axis, split right actions by the captured group boundary, and made “查看更多” expand in place.
5. Post-fix comparisons show matching major-region proportions, panel types, table density, action hierarchy and empty-state chart geometry for the captured representative layouts.

## Remaining capture blocker

The browser-visible inner pages and sanitized source structures have been captured for all included modules. Exact chart titles for 顾客、促销、配货、员工、短信 still require one authenticated login to the reference system so the outer-frame chart script can be inspected. Credentials, CAPTCHA, cookies and customer data will not be saved.

## Primary interactions and checks

- Opened the purchase and reports classic routes in the authenticated local app.
- Expanded and collapsed the purchase report group.
- Checked browser console errors: none.
- Frontend lint completed without errors after the final fix; all 30 tests passed; the final TypeScript and Vite production build passed.

final result: blocked

---

# Classic UI complete page set — Design QA

- Source visual truth: `/Users/lv/Workspace/erp/design-audit/02-purchase-dashboard.png`
- Browser-rendered implementation: `/Users/lv/Workspace/erp/docs/design-qa/classic-complete-purchase-dashboard-final-1280.jpg`
- Full-view comparison evidence: `/Users/lv/Workspace/erp/docs/design-qa/classic-complete-purchase-comparison-final.png`
- Focused interaction evidence: `/Users/lv/Workspace/erp/docs/design-qa/classic-complete-purchase-query-final-1280x660.jpg`
- Viewport/state: desktop 1280 × 660 CSS px at DPR 1, authenticated OWNER, 进货 module dashboard and 进货入库单查询 modal.
- Density normalization: source and dashboard implementation are both 1280 × 660 px. The combined comparison is 2560 × 660 px with source on the left and implementation on the right; no resampling was used.

## Findings

No actionable P0/P1/P2 mismatch remains in the classic UI page-set scope.

- Fonts and typography: the implementation uses the same compact Arial / Microsoft YaHei stack, 12 px table and navigation density, regular-weight panel headings and narrow old-style labels. Text remains legible without introducing the larger modern dashboard typography.
- Spacing and layout rhythm: the source composition is preserved: narrow blue module rail, two upper charts, lower latest-record grid, and stacked management/report links on the right. The implementation adds the existing product's 42 px store/account bar so the same authenticated tenant and store context remains visible; content proportions below it remain aligned with the source.
- Colors and visual tokens: the blue rail, light gray canvas, white square panels, gray section headers, blue chart series and green “查看更多报表” action match the reference palette. Modern rounded-card styling and heavy shadows are absent.
- Image quality and asset fidelity: the product's existing lightning brand asset and Ant Design icon family are used. The reference contains charts and icons rather than raster product imagery, so no generated or placeholder image assets are required.
- Copy and content: the right-side 进货管理 and 报表查询 order now follows the captured source. The latest-inbound table uses the exact ten captured headings: 单号、日期、供应商、经办人、货品总额、优惠金额、应付金额、已付金额、制单人、审核人.
- States and interactions: the inspected legacy manifest produces 12 module dashboards and 199 unique subpage routes. The representative query page preserves 调阅单据、删除单据、查询、刷新、表格、打印、导出、退出; 查询 opens the labeled date/keyword modal, print uses the browser print surface, export produces CSV, and unavailable dangerous operations are disabled or explained instead of pretending to save.
- Accessibility: navigation and toolbars use semantic buttons, the page toolbar has an accessible region label, form fields have visible labels, and disabled backend operations expose explanatory tooltips.
- Responsiveness: the classic interface remains desktop-first like the reference. Existing compact CSS moves the module rail to a bottom scrollable bar under 640 px and stacks query fields; the approved fidelity comparison is the reference desktop viewport.

## Comparison history

1. Initial full-set implementation: the module dashboard still used a six-column generic latest-record table, while the source used ten purchase-specific columns. This was a P1 content and density mismatch.
2. Fix: introduced module-specific classic table headers and changed the dashboard query entry to use the inspected manifest order. The 进货 page now uses all ten reference headings and preserves the exact report-link ordering.
3. Post-fix evidence: `classic-complete-purchase-comparison-final.png` shows the source and implementation at the same 1280 × 660 viewport with matching major-region proportions, navigation order, chart placement, table density and right-side entry hierarchy.

## Primary interactions tested

- Opened the 进货 module from the independent classic route.
- Opened 进货入库单查询 from the captured report order.
- Verified the page URL, original toolbar controls, exact purchase table headings and backend status notice.
- Opened the 查询 modal and verified start date, end date, keyword, 确定, 取消 and 清空 controls.
- Production build, static analysis and all 30 frontend tests passed after the complete page set was generated.
- Browser-rendered page showed no Vite error overlay or broken route state.

## Follow-up polish

- P3: the source has no tenant/store/account header; the implementation intentionally retains the 42 px product header so users can safely see and switch the active store.
- P3: the classic bundle contains the full 199-page manifest and produces a size advisory after minification; it remains lazy-loaded away from the modern UI and does not block the build.

final result: passed

---

# White sidebar and owner reports — Design QA

- Source visual truth: `/Users/lv/.codex/generated_images/01a010a9-8dad-7aa3-8252-0efe73bee46a/exec-6d1db35b-4a07-41d4-be57-e135cf9967ad.png`
- Browser-rendered implementation: `/Users/lv/Workspace/erp/docs/design-qa/sidebar-implementation-1720x914.png`
- Full-view comparison evidence: `/Users/lv/Workspace/erp/docs/design-qa/sidebar-design-comparison.png`
- Owner report evidence: `/Users/lv/Workspace/erp/docs/design-qa/reports-owner-overview-1720x914.png`
- Compact viewport evidence: `/Users/lv/Workspace/erp/docs/design-qa/reports-owner-overview-1280x720.png`
- Viewport/state: 1720 × 914 CSS px at DPR 1 for the source comparison, authenticated OWNER on the dashboard; 1280 × 720 CSS px for sidebar overflow and owner-report checks.
- Density normalization: source and implementation are both 1720 × 914 px at DPR 1, so no resampling was required.

## Findings

No actionable P0/P1/P2 mismatch remains in the approved sidebar scope.

- Fonts and typography: the implementation preserves the product's existing Inter / PingFang SC / Microsoft YaHei stack. Active navigation weight and dark foreground hierarchy match the selected visual; small rasterization differences are acceptable P3 variation.
- Spacing and layout rhythm: the 232 px production sidebar remains intentionally narrower than the generated concept so the user-requested right-side layout stays unchanged. Menu rows use the selected 44 px height, 8 px radius and stable vertical spacing. At 1280 × 720 the menu scrolls independently while the version footer remains visible.
- Colors and visual tokens: the measured implementation values match the selected direction: `#FAFAFA` sidebar, `#E5E7EB` divider, `#ECEDEF` selected row, `#20242C` active text and `#7657E8` active indicator. The dark teal background and glow were removed.
- Image quality and asset fidelity: the existing lightning brand asset is reused directly and remains sharp. No emoji, CSS drawing or replacement logo was introduced.
- Copy and content: all current authorized menu names, version text and store/account context remain unchanged. The redesign does not rename or hide existing product functions.
- Responsiveness and overflow: browser measurements show document width equals viewport width at both 1720 and 1280. At 1280 × 720 the sidebar navigation has `clientHeight=620`, `scrollHeight=815`, `overflow-y=auto`, and the persistent footer remains visible.
- Owner reports: the rendered OWNER page contains the brand multi-store overview, current-store daily/period revenue, stored-value totals, payment reconciliation, channel differences and shift review state. The 1720 and 1280 captures show no page-level horizontal overflow; the dense store table uses its own horizontal container when required.

The full-view comparison keeps the entire sidebar legible, so a second crop was not required. The sidebar itself is the focused comparison region and occupies the full height in both equal-size halves of the combined evidence.

## Comparison history

1. Earlier implementation: the sidebar used a dark teal background, light text, teal selected fill and a heavy right shadow. This was a P1 mismatch with the user-selected white concept.
2. Fixes: switched Ant Design Menu to light mode; added the off-white surface, gray divider, neutral hover/selected states, purple active indicator, gray scrollbar and subdued footer; retained independent navigation scrolling.
3. Post-fix evidence: the 1720 × 914 combined comparison shows the intended white/neutral visual hierarchy and active-state treatment. The 1280 × 720 browser pass confirms the original long-menu rendering problem does not return.

## Primary interactions tested

- Dashboard and “经营报表” menu selection both render the purple active indicator and neutral selected row.
- The long navigation region remains independently scrollable and the version footer remains pinned.
- OWNER report loads brand/store revenue, stored-value and reconciliation content from the live local API.
- 1720 × 914 and 1280 × 720 views have no page-level horizontal overflow.
- Browser console errors checked: none.

## Follow-up polish

- P3: if the right-side visual direction is later approved, re-evaluate the 232 px sidebar width together with the new content grid instead of changing it in isolation.

final result: passed
