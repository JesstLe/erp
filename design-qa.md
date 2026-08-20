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
