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
