# UI 设计刷新方案

## 背景与目标

当前 ERP Web 前端基于 React 19 + Ant Design 6 + 全局 `styles.css`。虽然功能完整，但存在以下问题：

- Ant Design 6 的 bundle 与 runtime 开销较大，在低配（奔腾/赛扬、2-4GB 内存、老旧浏览器）设备上易出现卡顿。
- 部分页面体积失控，如 `CashierPage.tsx` 达 128KB，首屏与交互负担重。
- 现有视觉风格偏向传统 B2B Dashboard，难以达到一流 SaaS 产品的精致感。

本次改造目标：

1. 将 UI 提升至一流公司（Notion/Figma 式）的简洁、专业、高商业感水准。
2. 保持"只换肤不换骨"：不改动业务逻辑与页面布局结构，仅替换设计系统、组件库与样式。
3. 保证低配老旧电脑上流畅运行，无明显卡顿。

## 设计原则

按优先级排序：

1. **性能优先**：首屏 JS < 200KB（gzipped），避免 Long Tasks > 50ms，减少不必要的重渲染。
2. **可访问性**：遵循 WCAG 2.1 AA，支持键盘导航、焦点管理、足够的颜色对比度。
3. **商业专业感**：清晰的视觉层级、一致的设计语言、稳定的交互反馈。
4. **内容密度适中**：不低效也不过度空旷，适合门店运营人员高频操作。

视觉方向采用 **Notion/Figma 式"内容优先"语言**：

- 中性背景与文字，减少装饰性渐变。
- 块状卡片与清晰边框，强调信息结构。
- 克制动效，默认减少或关闭复杂动画。
- 清晰的字体层级，数字使用等宽字体tabular-nums。

## Design Token 系统

### 颜色

| Token | 值 | 用途 |
|-------|-----|------|
| `--bg` | `#ffffff` | 主背景 |
| `--bg-secondary` | `#f7f7f7` | 次级背景、卡片 hover |
| `--bg-tertiary` | `#eeeeee` | 分隔区、禁用背景 |
| `--fg` | `#111111` | 主文本 |
| `--fg-secondary` | `#6b6b6b` | 次级文本 |
| `--fg-tertiary` | `#9a9a9a` | 占位、禁用文本 |
| `--border` | `#e5e5e5` | 边框 |
| `--border-strong` | `#d4d4d4` | 强边框、焦点 |
| `--primary` | `#138f84` | 品牌主色（降低饱和度使用） |
| `--primary-hover` | `#0a756c` | 主色 hover |
| `--primary-subtle` | `#e6f4f2` | 主色浅色背景 |
| `--success` | `#10b981` | 成功 |
| `--warning` | `#f59e0b` | 警告 |
| `--error` | `#ef4444` | 错误 |
| `--info` | `#3b82f6` | 信息 |

### 字体

- 英文显示/标题：`Satoshi`, sans-serif
- 中文回退：`"PingFang SC"`, `"Microsoft YaHei"`, sans-serif
- 完整栈：`Satoshi, "PingFang SC", "Microsoft YaHei", sans-serif`
- 数字：`font-variant-numeric: tabular-nums`

### 间距

以 4px 为基准：

| Token | 值 |
|-------|-----|
| `--space-1` | 4px |
| `--space-2` | 8px |
| `--space-3` | 12px |
| `--space-4` | 16px |
| `--space-5` | 24px |
| `--space-6` | 32px |
| `--space-7` | 48px |

### 圆角

| Token | 值 |
|-------|-----|
| `--radius-sm` | 4px |
| `--radius-md` | 6px |
| `--radius-lg` | 8px |
| `--radius-xl` | 12px |

### 阴影

仅用于浮动层，极克制：

| Token | 值 |
|-------|-----|
| `--shadow-1` | `0 1px 2px rgba(0,0,0,0.05)` |
| `--shadow-2` | `0 2px 4px rgba(0,0,0,0.05)` |
| `--shadow-3` | `0 4px 8px rgba(0,0,0,0.08)` |

## 组件库架构

### 核心依赖

- **Radix UI Primitives**：按需安装每个 primitive（Dialog、DropdownMenu、Select、Tabs、Tooltip、Popover、Checkbox、RadioGroup、Switch、Slider、Accordion、ScrollArea、Separator、Avatar）。Radix 为 headless，无样式、可访问性极佳、包体积极小。
- **Tailwind CSS v4**：原子样式引擎，配合 CSS variables 实现 token 系统。
- **lucide-react**：图标库，tree-shakeable，单个图标 SVG 极小。
- **react-day-picker**：日期选择，轻量、无 moment 依赖。
- **@tanstack/react-virtual**：表格/长列表虚拟滚动。

### 自研组件（`src/ui/`）

| 组件 | 说明 |
|------|------|
| `button.tsx` | 多种变体：default、primary、ghost、destructive、outline、link |
| `input.tsx` | 文本输入，含 error/disabled 状态 |
| `textarea.tsx` | 多行文本 |
| `select.tsx` | 基于 Radix Select |
| `dropdown-menu.tsx` | 基于 Radix DropdownMenu |
| `dialog.tsx` | 基于 Radix Dialog |
| `tabs.tsx` | 基于 Radix Tabs |
| `tooltip.tsx` | 基于 Radix Tooltip，默认减少动效 |
| `popover.tsx` | 基于 Radix Popover |
| `table.tsx` | 基于原生 table，支持 @tanstack/react-virtual |
| `card.tsx` | 卡片容器 |
| `badge.tsx` | 状态标签 |
| `avatar.tsx` | 基于 Radix Avatar |
| `checkbox.tsx` | 基于 Radix Checkbox |
| `radio-group.tsx` | 基于 Radix RadioGroup |
| `switch.tsx` | 基于 Radix Switch |
| `sidebar.tsx` | 应用侧边栏 |
| `header.tsx` | 应用顶栏 |
| `separator.tsx` | 分隔线 |
| `skeleton.tsx` | 加载占位，无 shimmer 动画 |
| `form.tsx` | 轻量表单上下文与校验提示 |
| `label.tsx` | 表单标签 |

### 不引入的依赖

- 重型动画库（Framer Motion、GSAP）：所有动效使用 CSS transition，且默认简短。
- 重型图表库：现有手写 CSS 图表（`.daily-chart`/`.daily-bars`）保留并适配新 tokens。
- 重型表单库：使用原生 form + 自研 Form 上下文。
- 全量图标库：仅引入 lucide-react 中实际使用的图标。

## 文件结构

```
apps/web/
├── src/
│   ├── main.tsx                    # 替换 ConfigProvider 为新的 token provider
│   ├── App.tsx                     # 路由与懒加载
│   ├── styles/
│   │   └── index.css               # Tailwind 入口 + CSS variables tokens
│   ├── ui/                         # 自研组件库
│   │   ├── button.tsx
│   │   ├── input.tsx
│   │   ├── table.tsx
│   │   ├── card.tsx
│   │   ├── dialog.tsx
│   │   ├── dropdown-menu.tsx
│   │   ├── select.tsx
│   │   ├── tabs.tsx
│   │   ├── tooltip.tsx
│   │   ├── badge.tsx
│   │   ├── avatar.tsx
│   │   ├── sidebar.tsx
│   │   ├── header.tsx
│   │   └── ...
│   ├── layout/
│   │   ├── AppLayout.tsx           # 主应用布局
│   │   └── PlatformLayout.tsx      # 平台管理布局
│   ├── pages/                      # 现有页面，仅逐步替换 antd 引用
│   └── lib/
│       └── utils.ts                # cn() 工具函数
├── tailwind.config.ts
└── package.json                    # 新增/移除依赖
```

## 迁移策略

1. **阶段 1：基础设施**（分支：`feat/ui-design-refresh`）
   - 安装 Tailwind CSS v4、Radix primitives、lucide-react、react-day-picker、@tanstack/react-virtual。
   - 移除 antd 与 @ant-design/icons（最终阶段完成）。
   - 建立 `src/styles/index.css` token 系统。
   - 建立 `src/lib/utils.ts`。

2. **阶段 2：公共布局**
   - 迁移 `AppLayout.tsx` 的 Sidebar、Header。
   - 迁移 `PlatformLayout.tsx`。
   - 迁移登录页 `LoginPage.tsx` 与平台登录页 `PlatformLoginPage.tsx`。

3. **阶段 3：页面迁移**（按优先级）
   1. `DashboardPage.tsx`
   2. `LoginPage.tsx` / `PlatformLoginPage.tsx`
   3. `CashierPage.tsx`（最大，拆分组件后迁移）
   4. `CustomersPage.tsx`
   5. `FacilitiesPage.tsx`
   6. `SchedulingPage.tsx`
   7. 其余页面（Inventory、Reports、Employees、OrganizationSettings、PaymentChannels、Audit、PriceBooks、Products、ServiceItems、SupplyChain 等）

4. **阶段 4：性能优化**
   - 路由级代码分割（React.lazy + Suspense）。
   - 对 `CashierPage.tsx`、`CustomersPage.tsx` 等巨型页面做组件拆分。
   - 长表格接入 @tanstack/react-virtual 虚拟滚动。
   - 组件级 memoization，减少低配置设备上的重渲染。
   - 字体按需加载（仅加载 Satoshi 必要字重）。

5. **阶段 5：回归测试与设计 QA**
   - 运行现有测试（vitest + Testing Library）。
   - 更新 `design-qa.md` 与 `design-audit/` 截图。
   - 在低配环境或 Chrome DevTools 性能面板上验证首屏与交互帧率。

## 性能目标

| 指标 | 目标 |
|------|------|
| 首屏 JS | < 200KB gzipped |
| Long Tasks | 无 > 50ms 的长任务 |
| 首屏渲染 | 在模拟低配设备上 < 3s（3G 慢网 + 4x CPU 降速） |
| 交互帧率 | 列表滚动、表单输入、弹窗开关保持 60fps |
| 内存占用 | 长时间使用不持续增长 |

## 风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| Radix primitive 覆盖 Ant Design 复杂组件（DatePicker、Cascader、TreeSelect）工作量大 | 对复杂组件优先使用轻量第三方（react-day-picker）或保留 antd 单个组件逐步替换 |
| 页面迁移过程中出现视觉不一致 | 每次迁移一个页面后立即做设计 QA，不批量合并 |
| 老旧浏览器不支持 Tailwind v4 部分特性 | 配置 browserslist 包含 Chrome 80+ / Edge 80+，必要时加 polyfill |
| 测试覆盖下降 | 保持现有测试，新增组件级测试，确保 80% 以上覆盖率 |

## 分支与提交计划

- 从当前分支 `feat/ecommerce-auto-selection` 切出 `feat/ui-design-refresh`。
- 每个阶段独立 commit，例如：
  - `feat: setup Tailwind and design tokens`
  - `feat: migrate app sidebar and header to new UI`
  - `feat: migrate login page to new UI`
  - `feat: migrate dashboard page to new UI`
  - `perf: add route-level code splitting and virtual scroll`
  - `docs: update design qa and audit screenshots`
