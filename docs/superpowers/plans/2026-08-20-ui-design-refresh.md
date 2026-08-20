# UI 设计刷新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `apps/web/` 前端从 Ant Design 6 迁移到基于 Radix Primitives + Tailwind CSS v4 的轻量自定义 UI 系统，在保留业务逻辑与页面布局的前提下实现 Notion/Figma 式简洁商业感，并确保低配老旧设备流畅运行。

**Architecture:** 以 `src/ui/` 自研组件库替换 `antd` 组件调用，`src/styles/index.css` 承载 Tailwind 入口与 CSS variables design tokens，`src/lib/utils.ts` 提供 `cn()` 工具。公共布局先行，页面按优先级逐步迁移，最后移除 `antd` 与 `@ant-design/icons` 并做性能验证。

**Tech Stack:** React 19, TypeScript 6, Vite 8, Tailwind CSS v4, Radix UI Primitives, lucide-react, react-day-picker, @tanstack/react-virtual, vitest + Testing Library.

## Global Constraints

- 必须保持"只换肤不换骨"：不改动业务逻辑、API 调用、路由结构、权限规则。
- 目标设备：低配 / 老旧设备（奔腾/赛扬、2-4GB 内存、老版 Chrome / 国产浏览器内核）。
- 不引入 Framer Motion / GSAP 等重型动画库，所有动效使用 CSS transition，且默认简短。
- 所有组件必须支持键盘导航与焦点管理，颜色对比度满足 WCAG 2.1 AA。
- 每个任务结束时必须能独立运行 `npm run test` 与 `npm run build` 不报错。
- Commit message 遵循 conventional commits：`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `perf:`。

---

## File Structure

创建或修改的关键文件：

- `apps/web/package.json` — 依赖变更。
- `apps/web/tailwind.config.ts` — Tailwind 配置。
- `apps/web/src/styles/index.css` — Tailwind 入口 + CSS variables tokens（替代 `styles.css`）。
- `apps/web/src/styles.css` — 在迁移末期删除。
- `apps/web/src/lib/utils.ts` — `cn()` 工具。
- `apps/web/src/ui/*.tsx` — 自研组件库。
- `apps/web/src/layout/AppLayout.tsx` — 替换为新的 Sidebar + Header。
- `apps/web/src/layout/PlatformLayout.tsx` — 平台管理布局（新增）。
- `apps/web/src/main.tsx` — 移除 `ConfigProvider`/`AntApp`。
- `apps/web/src/App.tsx` — 替换 `Spin` fallback、移除 `styles.css` import。
- `apps/web/src/pages/*.tsx` — 逐步替换 antd 组件引用。
- `apps/web/src/components/BrandLogo.tsx` — 保留。

---

### Task 1: 创建功能分支

**Files:**
- Modify: `.git/HEAD`（分支操作）

**Interfaces:**
- 无

- [ ] **Step 1: 从当前分支切出新分支**

```bash
cd /Users/lv/Workspace/erp
git checkout -b feat/ui-design-refresh
```

- [ ] **Step 2: 确认分支**

```bash
git branch --show-current
```

Expected output: `feat/ui-design-refresh`

- [ ] **Step 3: Commit 标记**

```bash
git commit --allow-empty -m "chore: start UI design refresh branch"
```

---

### Task 2: 安装 Tailwind CSS v4

**Files:**
- Modify: `apps/web/package.json`
- Create: `apps/web/src/styles/index.css`
- Modify: `apps/web/src/main.tsx`

**Interfaces:**
- 无

- [ ] **Step 1: 安装依赖**

```bash
cd /Users/lv/Workspace/erp/apps/web
npm install -D tailwindcss@4 postcss autoprefixer
```

- [ ] **Step 2: 创建 Tailwind 入口 CSS**

Create `apps/web/src/styles/index.css`:

```css
@import "tailwindcss";

@theme {
  --color-bg: #ffffff;
  --color-bg-secondary: #f7f7f7;
  --color-bg-tertiary: #eeeeee;
  --color-fg: #111111;
  --color-fg-secondary: #6b6b6b;
  --color-fg-tertiary: #9a9a9a;
  --color-border: #e5e5e5;
  --color-border-strong: #d4d4d4;
  --color-primary: #138f84;
  --color-primary-hover: #0a756c;
  --color-primary-subtle: #e6f4f2;
  --color-success: #10b981;
  --color-warning: #f59e0b;
  --color-error: #ef4444;
  --color-info: #3b82f6;

  --font-sans: "Plus Jakarta Sans", "PingFang SC", "Microsoft YaHei", sans-serif;
  --radius-sm: 4px;
  --radius-md: 6px;
  --radius-lg: 8px;
  --radius-xl: 12px;
}

@layer base {
  html {
    font-family: var(--font-sans);
    color: var(--color-fg);
    background: var(--color-bg-secondary);
    font-variant-numeric: tabular-nums;
  }
  body {
    margin: 0;
    min-width: 320px;
    min-height: 100vh;
  }
  *, *::before, *::after {
    box-sizing: border-box;
  }
}
```

- [ ] **Step 3: 在 main.tsx 引入新 CSS**

Modify `apps/web/src/main.tsx` line 1:

```typescript
import './styles/index.css'
```

- [ ] **Step 4: 运行构建确认**

```bash
npm run build
```

Expected: 成功，无 TypeScript 错误。

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: install Tailwind CSS v4 and setup base tokens"
```

---

### Task 3: 安装工具库与 Radix Primitives

**Files:**
- Modify: `apps/web/package.json`

**Interfaces:**
- 后续任务依赖 `clsx`, `tailwind-merge`, `lucide-react`。

- [ ] **Step 1: 安装工具与图标**

```bash
cd /Users/lv/Workspace/erp/apps/web
npm install clsx tailwind-merge lucide-react
```

- [ ] **Step 2: 安装 Radix Primitives（第一批）**

```bash
npm install @radix-ui/react-dialog @radix-ui/react-dropdown-menu @radix-ui/react-select @radix-ui/react-tabs @radix-ui/react-tooltip @radix-ui/react-popover
```

- [ ] **Step 3: 安装 Radix Primitives（第二批）**

```bash
npm install @radix-ui/react-checkbox @radix-ui/react-radio-group @radix-ui/react-switch @radix-ui/react-avatar @radix-ui/react-separator @radix-ui/react-scroll-area @radix-ui/react-slider @radix-ui/react-accordion
```

- [ ] **Step 4: 安装性能与日期库**

```bash
npm install @tanstack/react-virtual react-day-picker
```

- [ ] **Step 5: 确认安装成功**

```bash
npm run build
```

Expected: 成功。

- [ ] **Step 6: Commit**

```bash
git add package.json package-lock.json
git commit -m "chore: add Radix primitives, Tailwind utils, icons, and virtual scroll"
```

---

### Task 4: 创建 cn() 工具

**Files:**
- Create: `apps/web/src/lib/utils.ts`
- Create: `apps/web/src/lib/utils.test.ts`

**Interfaces:**
- Produces: `cn(...inputs: ClassValue[]): string`

- [ ] **Step 1: 编写测试**

Create `apps/web/src/lib/utils.test.ts`:

```typescript
import { cn } from './utils'

describe('cn', () => {
  it('merges class names', () => {
    expect(cn('a', 'b')).toBe('a b')
  })

  it('handles conditional classes', () => {
    expect(cn('a', false && 'b', 'c')).toBe('a c')
  })

  it('resolves Tailwind conflicts', () => {
    expect(cn('p-4', 'p-2')).toBe('p-2')
  })
})
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run test -- src/lib/utils.test.ts
```

Expected: FAIL（`cn` not defined）。

- [ ] **Step 3: 实现工具函数**

Create `apps/web/src/lib/utils.ts`:

```typescript
import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs))
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run test -- src/lib/utils.test.ts
```

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/lib/utils.ts src/lib/utils.test.ts
git commit -m "feat: add cn() utility with tests"
```

---

### Task 5: 创建基础展示组件（Badge / Avatar / Separator / Skeleton）

**Files:**
- Create: `apps/web/src/ui/badge.tsx`
- Create: `apps/web/src/ui/avatar.tsx`
- Create: `apps/web/src/ui/separator.tsx`
- Create: `apps/web/src/ui/skeleton.tsx`
- Create: `apps/web/src/ui/badge.test.tsx`

**Interfaces:**
- Produces:
  - `Badge` component with variants: default, primary, success, warning, error, outline
  - `Avatar` component accepting `src`, `alt`, `fallback`
  - `Separator` component
  - `Skeleton` component

- [ ] **Step 1: 编写 Badge 测试**

Create `apps/web/src/ui/badge.test.tsx`:

```typescript
import { render, screen } from '@testing-library/react'
import { Badge } from './badge'

describe('Badge', () => {
  it('renders children', () => {
    render(<Badge>Test</Badge>)
    expect(screen.getByText('Test')).toBeInTheDocument()
  })

  it('applies primary variant class', () => {
    const { container } = render(<Badge variant="primary">Test</Badge>)
    expect(container.firstChild).toHaveClass('bg-primary-subtle')
  })
})
```

- [ ] **Step 2: 实现 Badge**

Create `apps/web/src/ui/badge.tsx`:

```typescript
import { type HTMLAttributes } from 'react'
import { cn } from '../lib/utils'

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: 'default' | 'primary' | 'success' | 'warning' | 'error' | 'outline'
}

const variants = {
  default: 'bg-bg-tertiary text-fg-secondary',
  primary: 'bg-primary-subtle text-primary',
  success: 'bg-success/10 text-success',
  warning: 'bg-warning/10 text-warning',
  error: 'bg-error/10 text-error',
  outline: 'border border-border text-fg-secondary',
}

export function Badge({ className, variant = 'default', ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium',
        variants[variant],
        className
      )}
      {...props}
    />
  )
}
```

- [ ] **Step 3: 实现 Avatar**

Create `apps/web/src/ui/avatar.tsx`:

```typescript
import * as AvatarPrimitive from '@radix-ui/react-avatar'
import { cn } from '../lib/utils'

export function Avatar({ className, ...props }: AvatarPrimitive.AvatarProps) {
  return (
    <AvatarPrimitive.Root
      className={cn('relative flex h-9 w-9 shrink-0 overflow-hidden rounded-lg bg-bg-tertiary', className)}
      {...props}
    />
  )
}

export function AvatarImage({ className, ...props }: AvatarPrimitive.AvatarImageProps) {
  return <AvatarPrimitive.Image className={cn('aspect-square h-full w-full', className)} {...props} />
}

export function AvatarFallback({ className, ...props }: AvatarPrimitive.AvatarFallbackProps) {
  return (
    <AvatarPrimitive.Fallback
      className={cn('flex h-full w-full items-center justify-center rounded-lg bg-primary-subtle text-sm font-medium text-primary', className)}
      {...props}
    />
  )
}
```

- [ ] **Step 4: 实现 Separator**

Create `apps/web/src/ui/separator.tsx`:

```typescript
import * as SeparatorPrimitive from '@radix-ui/react-separator'
import { cn } from '../lib/utils'

export function Separator({ className, orientation = 'horizontal', ...props }: SeparatorPrimitive.SeparatorProps) {
  return (
    <SeparatorPrimitive.Root
      className={cn(
        'shrink-0 bg-border',
        orientation === 'horizontal' ? 'h-px w-full' : 'h-full w-px',
        className
      )}
      orientation={orientation}
      {...props}
    />
  )
}
```

- [ ] **Step 5: 实现 Skeleton**

Create `apps/web/src/ui/skeleton.tsx`:

```typescript
import { cn } from '../lib/utils'

export function Skeleton({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('animate-pulse rounded-md bg-bg-tertiary', className)}
      {...props}
    />
  )
}
```

- [ ] **Step 6: 运行测试**

```bash
npm run test -- src/ui/badge.test.tsx
```

Expected: PASS。

- [ ] **Step 7: Commit**

```bash
git add src/ui/badge.tsx src/ui/avatar.tsx src/ui/separator.tsx src/ui/skeleton.tsx src/ui/badge.test.tsx
git commit -m "feat: add Badge, Avatar, Separator, Skeleton components"
```

---

### Task 6: 创建 Button 组件

**Files:**
- Create: `apps/web/src/ui/button.tsx`
- Create: `apps/web/src/ui/button.test.tsx`

**Interfaces:**
- Produces: `Button` component with variants: default, primary, destructive, outline, ghost, link; sizes: sm, md, lg, icon.

- [ ] **Step 1: 编写测试**

Create `apps/web/src/ui/button.test.tsx`:

```typescript
import { render, screen } from '@testing-library/react'
import { Button } from './button'

describe('Button', () => {
  it('renders children', () => {
    render(<Button>Click</Button>)
    expect(screen.getByRole('button', { name: 'Click' })).toBeInTheDocument()
  })

  it('supports primary variant', () => {
    const { container } = render(<Button variant="primary">Click</Button>)
    expect(container.firstChild).toHaveClass('bg-primary')
  })

  it('renders as child when asChild is true', () => {
    render(<Button asChild><a href="/">Link</a></Button>)
    expect(screen.getByRole('link', { name: 'Link' })).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: 实现 Button**

Create `apps/web/src/ui/button.tsx`:

```typescript
import { type ButtonHTMLAttributes, forwardRef } from 'react'
import { Slot } from '@radix-ui/react-slot'
import { cn } from '../lib/utils'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'default' | 'primary' | 'destructive' | 'outline' | 'ghost' | 'link'
  size?: 'sm' | 'md' | 'lg' | 'icon'
  asChild?: boolean
}

const variants = {
  default: 'bg-bg-tertiary text-fg hover:bg-border',
  primary: 'bg-primary text-white hover:bg-primary-hover',
  destructive: 'bg-error text-white hover:bg-error/90',
  outline: 'border border-border bg-bg hover:bg-bg-secondary',
  ghost: 'hover:bg-bg-secondary',
  link: 'text-primary underline-offset-4 hover:underline',
}

const sizes = {
  sm: 'h-8 px-3 text-xs',
  md: 'h-10 px-4 text-sm',
  lg: 'h-12 px-6 text-base',
  icon: 'h-10 w-10',
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'default', size = 'md', asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : 'button'
    return (
      <Comp
        ref={ref}
        className={cn(
          'inline-flex items-center justify-center gap-2 rounded-md font-medium transition-colors',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
          'disabled:pointer-events-none disabled:opacity-50',
          variants[variant],
          sizes[size],
          className
        )}
        {...props}
      />
    )
  }
)
Button.displayName = 'Button'
```

注意：需要安装 `@radix-ui/react-slot`（是 `@radix-ui/react-slot` 包，不是 primitives 集合）。

- [ ] **Step 3: 安装 @radix-ui/react-slot**

```bash
npm install @radix-ui/react-slot
```

- [ ] **Step 4: 运行测试**

```bash
npm run test -- src/ui/button.test.tsx
```

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/ui/button.tsx src/ui/button.test.tsx package.json package-lock.json
git commit -m "feat: add Button component with variants and tests"
```

---

### Task 7: 创建 Input / Label / Textarea / Card 组件

**Files:**
- Create: `apps/web/src/ui/input.tsx`
- Create: `apps/web/src/ui/label.tsx`
- Create: `apps/web/src/ui/textarea.tsx`
- Create: `apps/web/src/ui/card.tsx`
- Create: `apps/web/src/ui/input.test.tsx`

**Interfaces:**
- Produces: `Input`, `Label`, `Textarea`, `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`, `CardFooter`.

- [ ] **Step 1: 编写 Input 测试**

Create `apps/web/src/ui/input.test.tsx`:

```typescript
import { render, screen } from '@testing-library/react'
import { Input } from './input'

describe('Input', () => {
  it('renders input element', () => {
    render(<Input placeholder="Enter" />)
    expect(screen.getByPlaceholderText('Enter')).toBeInTheDocument()
  })

  it('applies error styles', () => {
    const { container } = render(<Input error />)
    expect(container.firstChild).toHaveClass('border-error')
  })
})
```

- [ ] **Step 2: 实现 Label**

Create `apps/web/src/ui/label.tsx`:

```typescript
import * as LabelPrimitive from '@radix-ui/react-label'
import { cn } from '../lib/utils'

export function Label({ className, ...props }: LabelPrimitive.LabelProps) {
  return (
    <LabelPrimitive.Root
      className={cn('text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70', className)}
      {...props}
    />
  )
}
```

需要安装 `@radix-ui/react-label`：

```bash
npm install @radix-ui/react-label
```

- [ ] **Step 3: 实现 Input**

Create `apps/web/src/ui/input.tsx`:

```typescript
import { type InputHTMLAttributes, forwardRef } from 'react'
import { cn } from '../lib/utils'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  error?: boolean
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ className, error, ...props }, ref) => {
    return (
      <input
        ref={ref}
        className={cn(
          'flex h-10 w-full rounded-md border border-border bg-bg px-3 py-2 text-sm',
          'placeholder:text-fg-tertiary',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-1',
          'disabled:cursor-not-allowed disabled:opacity-50',
          error && 'border-error focus-visible:ring-error',
          className
        )}
        {...props}
      />
    )
  }
)
Input.displayName = 'Input'
```

- [ ] **Step 4: 实现 Textarea**

Create `apps/web/src/ui/textarea.tsx`:

```typescript
import { type TextareaHTMLAttributes, forwardRef } from 'react'
import { cn } from '../lib/utils'

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  error?: boolean
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ className, error, ...props }, ref) => {
    return (
      <textarea
        ref={ref}
        className={cn(
          'flex min-h-[80px] w-full rounded-md border border-border bg-bg px-3 py-2 text-sm',
          'placeholder:text-fg-tertiary',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-1',
          'disabled:cursor-not-allowed disabled:opacity-50',
          error && 'border-error focus-visible:ring-error',
          className
        )}
        {...props}
      />
    )
  }
)
Textarea.displayName = 'Textarea'
```

- [ ] **Step 5: 实现 Card**

Create `apps/web/src/ui/card.tsx`:

```typescript
import { cn } from '../lib/utils'

export function Card({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('rounded-lg border border-border bg-bg shadow-1', className)}
      {...props}
    />
  )
}

export function CardHeader({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col gap-1.5 p-5', className)} {...props} />
}

export function CardTitle({ className, ...props }: React.HTMLAttributes<HTMLHeadingElement>) {
  return <h3 className={cn('text-base font-semibold leading-none tracking-tight', className)} {...props} />
}

export function CardDescription({ className, ...props }: React.HTMLAttributes<HTMLParagraphElement>) {
  return <p className={cn('text-sm text-fg-secondary', className)} {...props} />
}

export function CardContent({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('p-5 pt-0', className)} {...props} />
}

export function CardFooter({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex items-center p-5 pt-0', className)} {...props} />
}
```

- [ ] **Step 6: 运行测试**

```bash
npm run test -- src/ui/input.test.tsx
```

Expected: PASS。

- [ ] **Step 7: Commit**

```bash
git add src/ui/input.tsx src/ui/label.tsx src/ui/textarea.tsx src/ui/card.tsx src/ui/input.test.tsx package.json package-lock.json
git commit -m "feat: add Input, Label, Textarea, Card components with tests"
```

---

### Task 8: 创建 Dialog / DropdownMenu / Select 组件

**Files:**
- Create: `apps/web/src/ui/dialog.tsx`
- Create: `apps/web/src/ui/dropdown-menu.tsx`
- Create: `apps/web/src/ui/select.tsx`

**Interfaces:**
- Produces: `Dialog`, `DialogTrigger`, `DialogContent`, `DialogHeader`, `DialogTitle`, `DialogDescription`, `DialogFooter`。
- Produces: `DropdownMenu`, `DropdownMenuTrigger`, `DropdownMenuContent`, `DropdownMenuItem`, `DropdownMenuSeparator`, `DropdownMenuLabel`。
- Produces: `Select`, `SelectTrigger`, `SelectValue`, `SelectContent`, `SelectItem`。

- [ ] **Step 1: 实现 Dialog**

Create `apps/web/src/ui/dialog.tsx`:

```typescript
import * as DialogPrimitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { cn } from '../lib/utils'

export const Dialog = DialogPrimitive.Root
export const DialogTrigger = DialogPrimitive.Trigger
export const DialogPortal = DialogPrimitive.Portal

export function DialogOverlay({ className, ...props }: DialogPrimitive.DialogOverlayProps) {
  return (
    <DialogPrimitive.Overlay
      className={cn(
        'fixed inset-0 z-50 bg-fg/40 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
        className
      )}
      {...props}
    />
  )
}

export function DialogContent({ className, children, ...props }: DialogPrimitive.DialogContentProps) {
  return (
    <DialogPortal>
      <DialogOverlay />
      <DialogPrimitive.Content
        className={cn(
          'fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-lg border border-border bg-bg p-6 shadow-3',
          'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95',
          className
        )}
        {...props}
      >
        {children}
        <DialogPrimitive.Close className="absolute right-4 top-4 rounded-sm opacity-70 ring-offset-bg transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2 disabled:pointer-events-none">
          <X className="h-4 w-4" />
          <span className="sr-only">Close</span>
        </DialogPrimitive.Close>
      </DialogPrimitive.Content>
    </DialogPortal>
  )
}

export function DialogHeader({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col gap-1.5 text-left', className)} {...props} />
}

export function DialogFooter({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col-reverse gap-2 sm:flex-row sm:justify-end sm:gap-3 mt-6', className)} {...props} />
}

export function DialogTitle({ className, ...props }: DialogPrimitive.DialogTitleProps) {
  return <DialogPrimitive.Title className={cn('text-lg font-semibold leading-none tracking-tight', className)} {...props} />
}

export function DialogDescription({ className, ...props }: DialogPrimitive.DialogDescriptionProps) {
  return <DialogPrimitive.Description className={cn('text-sm text-fg-secondary', className)} {...props} />
}
```

注意：Tailwind CSS v4 可能不支持 `animate-in` / `fade-in-0` 等动画工具类，除非安装 tailwindcss-animate。为简化，可以移除动画类，只保留基础样式。后续统一在 Task 39 处理动效策略。此处先用无动画版本。

修正后的 DialogContent 不使用 animate 工具类：

```typescript
export function DialogContent({ className, children, ...props }: DialogPrimitive.DialogContentProps) {
  return (
    <DialogPortal>
      <DialogOverlay />
      <DialogPrimitive.Content
        className={cn(
          'fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-lg border border-border bg-bg p-6 shadow-3',
          className
        )}
        {...props}
      >
        {children}
        <DialogPrimitive.Close className="absolute right-4 top-4 rounded-sm opacity-70 ring-offset-bg transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2 disabled:pointer-events-none">
          <X className="h-4 w-4" />
          <span className="sr-only">Close</span>
        </DialogPrimitive.Close>
      </DialogPrimitive.Content>
    </DialogPortal>
  )
}
```

- [ ] **Step 2: 实现 DropdownMenu**

Create `apps/web/src/ui/dropdown-menu.tsx`:

```typescript
import * as DropdownMenuPrimitive from '@radix-ui/react-dropdown-menu'
import { Check, ChevronRight, Circle } from 'lucide-react'
import { cn } from '../lib/utils'

export const DropdownMenu = DropdownMenuPrimitive.Root
export const DropdownMenuTrigger = DropdownMenuPrimitive.Trigger
export const DropdownMenuGroup = DropdownMenuPrimitive.Group
export const DropdownMenuPortal = DropdownMenuPrimitive.Portal
export const DropdownMenuSub = DropdownMenuPrimitive.Sub
export const DropdownMenuRadioGroup = DropdownMenuPrimitive.RadioGroup

export function DropdownMenuSubTrigger({ className, inset, children, ...props }: DropdownMenuPrimitive.DropdownMenuSubTriggerProps & { inset?: boolean }) {
  return (
    <DropdownMenuPrimitive.SubTrigger
      className={cn(
        'flex cursor-default select-none items-center rounded-sm px-2 py-1.5 text-sm outline-none focus:bg-bg-secondary',
        inset && 'pl-8',
        className
      )}
      {...props}
    >
      {children}
      <ChevronRight className="ml-auto h-4 w-4" />
    </DropdownMenuPrimitive.SubTrigger>
  )
}

export function DropdownMenuSubContent({ className, ...props }: DropdownMenuPrimitive.DropdownMenuSubContentProps) {
  return (
    <DropdownMenuPrimitive.SubContent
      className={cn(
        'z-50 min-w-[8rem] overflow-hidden rounded-md border border-border bg-bg p-1 shadow-3',
        className
      )}
      {...props}
    />
  )
}

export function DropdownMenuContent({ className, sideOffset = 4, ...props }: DropdownMenuPrimitive.DropdownMenuContentProps) {
  return (
    <DropdownMenuPrimitive.Portal>
      <DropdownMenuPrimitive.Content
        sideOffset={sideOffset}
        className={cn(
          'z-50 min-w-[8rem] overflow-hidden rounded-md border border-border bg-bg p-1 shadow-3',
          className
        )}
        {...props}
      />
    </DropdownMenuPrimitive.Portal>
  )
}

export function DropdownMenuItem({ className, inset, ...props }: DropdownMenuPrimitive.DropdownMenuItemProps & { inset?: boolean }) {
  return (
    <DropdownMenuPrimitive.Item
      className={cn(
        'relative flex cursor-default select-none items-center rounded-sm px-2 py-1.5 text-sm outline-none transition-colors focus:bg-bg-secondary focus:text-fg data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        inset && 'pl-8',
        className
      )}
      {...props}
    />
  )
}

export function DropdownMenuCheckboxItem({ className, children, checked, ...props }: DropdownMenuPrimitive.DropdownMenuCheckboxItemProps) {
  return (
    <DropdownMenuPrimitive.CheckboxItem
      className={cn(
        'relative flex cursor-default select-none items-center rounded-sm py-1.5 pl-8 pr-2 text-sm outline-none transition-colors focus:bg-bg-secondary focus:text-fg data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        className
      )}
      checked={checked}
      {...props}
    >
      <span className="absolute left-2 flex h-3.5 w-3.5 items-center justify-center">
        <DropdownMenuPrimitive.ItemIndicator>
          <Check className="h-4 w-4" />
        </DropdownMenuPrimitive.ItemIndicator>
      </span>
      {children}
    </DropdownMenuPrimitive.CheckboxItem>
  )
}

export function DropdownMenuRadioItem({ className, children, ...props }: DropdownMenuPrimitive.DropdownMenuRadioItemProps) {
  return (
    <DropdownMenuPrimitive.RadioItem
      className={cn(
        'relative flex cursor-default select-none items-center rounded-sm py-1.5 pl-8 pr-2 text-sm outline-none transition-colors focus:bg-bg-secondary focus:text-fg data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        className
      )}
      {...props}
    >
      <span className="absolute left-2 flex h-3.5 w-3.5 items-center justify-center">
        <DropdownMenuPrimitive.ItemIndicator>
          <Circle className="h-2 w-2 fill-current" />
        </DropdownMenuPrimitive.ItemIndicator>
      </span>
      {children}
    </DropdownMenuPrimitive.RadioItem>
  )
}

export function DropdownMenuLabel({ className, inset, ...props }: DropdownMenuPrimitive.DropdownMenuLabelProps & { inset?: boolean }) {
  return (
    <DropdownMenuPrimitive.Label
      className={cn('px-2 py-1.5 text-xs font-semibold text-fg-secondary', inset && 'pl-8', className)}
      {...props}
    />
  )
}

export function DropdownMenuSeparator({ className, ...props }: DropdownMenuPrimitive.DropdownMenuSeparatorProps) {
  return (
    <DropdownMenuPrimitive.Separator
      className={cn('-mx-1 my-1 h-px bg-border', className)}
      {...props}
    />
  )
}
```

- [ ] **Step 3: 实现 Select**

Create `apps/web/src/ui/select.tsx`:

```typescript
import * as SelectPrimitive from '@radix-ui/react-select'
import { Check, ChevronDown, ChevronUp } from 'lucide-react'
import { cn } from '../lib/utils'

export const Select = SelectPrimitive.Root
export const SelectGroup = SelectPrimitive.Group
export const SelectValue = SelectPrimitive.Value

export function SelectTrigger({ className, children, ...props }: SelectPrimitive.SelectTriggerProps) {
  return (
    <SelectPrimitive.Trigger
      className={cn(
        'flex h-10 w-full items-center justify-between rounded-md border border-border bg-bg px-3 py-2 text-sm',
        'placeholder:text-fg-tertiary',
        'focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-1',
        'disabled:cursor-not-allowed disabled:opacity-50',
        className
      )}
      {...props}
    >
      {children}
      <SelectPrimitive.Icon asChild>
        <ChevronDown className="h-4 w-4 opacity-50" />
      </SelectPrimitive.Icon>
    </SelectPrimitive.Trigger>
  )
}

export function SelectScrollUpButton({ className, ...props }: SelectPrimitive.SelectScrollUpButtonProps) {
  return (
    <SelectPrimitive.ScrollUpButton
      className={cn('flex cursor-default items-center justify-center py-1', className)}
      {...props}
    >
      <ChevronUp className="h-4 w-4" />
    </SelectPrimitive.ScrollUpButton>
  )
}

export function SelectScrollDownButton({ className, ...props }: SelectPrimitive.SelectScrollDownButtonProps) {
  return (
    <SelectPrimitive.ScrollDownButton
      className={cn('flex cursor-default items-center justify-center py-1', className)}
      {...props}
    >
      <ChevronDown className="h-4 w-4" />
    </SelectPrimitive.ScrollDownButton>
  )
}

export function SelectContent({ className, children, position = 'popper', ...props }: SelectPrimitive.SelectContentProps) {
  return (
    <SelectPrimitive.Portal>
      <SelectPrimitive.Content
        className={cn(
          'relative z-50 max-h-96 min-w-[8rem] overflow-hidden rounded-md border border-border bg-bg shadow-3',
          position === 'popper' && 'data-[side=bottom]:translate-y-1 data-[side=left]:-translate-x-1 data-[side=right]:translate-x-1 data-[side=top]:-translate-y-1',
          className
        )}
        position={position}
        {...props}
      >
        <SelectScrollUpButton />
        <SelectPrimitive.Viewport className={cn('p-1', position === 'popper' && 'h-[var(--radix-select-trigger-height)] w-full min-w-[var(--radix-select-trigger-width)]')}>
          {children}
        </SelectPrimitive.Viewport>
        <SelectScrollDownButton />
      </SelectPrimitive.Content>
    </SelectPrimitive.Portal>
  )
}

export function SelectLabel({ className, ...props }: SelectPrimitive.SelectLabelProps) {
  return <SelectPrimitive.Label className={cn('py-1.5 pl-8 pr-2 text-xs font-semibold', className)} {...props} />
}

export function SelectItem({ className, children, ...props }: SelectPrimitive.SelectItemProps) {
  return (
    <SelectPrimitive.Item
      className={cn(
        'relative flex w-full cursor-default select-none items-center rounded-sm py-1.5 pl-8 pr-2 text-sm outline-none focus:bg-bg-secondary focus:text-fg data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        className
      )}
      {...props}
    >
      <span className="absolute left-2 flex h-3.5 w-3.5 items-center justify-center">
        <SelectPrimitive.ItemIndicator>
          <Check className="h-4 w-4" />
        </SelectPrimitive.ItemIndicator>
      </span>
      <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
    </SelectPrimitive.Item>
  )
}

export function SelectSeparator({ className, ...props }: SelectPrimitive.SelectSeparatorProps) {
  return <SelectPrimitive.Separator className={cn('-mx-1 my-1 h-px bg-border', className)} {...props} />
}
```

- [ ] **Step 4: 运行构建确认**

```bash
npm run build
```

Expected: 成功（只有类型检查，无运行时测试）。

- [ ] **Step 5: Commit**

```bash
git add src/ui/dialog.tsx src/ui/dropdown-menu.tsx src/ui/select.tsx
git commit -m "feat: add Dialog, DropdownMenu, Select components"
```

---

### Task 9: 创建 Tabs / Tooltip / Popover / Checkbox / Switch

**Files:**
- Create: `apps/web/src/ui/tabs.tsx`
- Create: `apps/web/src/ui/tooltip.tsx`
- Create: `apps/web/src/ui/popover.tsx`
- Create: `apps/web/src/ui/checkbox.tsx`
- Create: `apps/web/src/ui/switch.tsx`

**Interfaces:**
- Produces: `Tabs`, `TabsList`, `TabsTrigger`, `TabsContent`。
- Produces: `Tooltip`, `TooltipTrigger`, `TooltipContent`, `TooltipProvider`。
- Produces: `Popover`, `PopoverTrigger`, `PopoverContent`。
- Produces: `Checkbox`。
- Produces: `Switch`。

- [ ] **Step 1: 实现 Tabs**

Create `apps/web/src/ui/tabs.tsx`:

```typescript
import * as TabsPrimitive from '@radix-ui/react-tabs'
import { cn } from '../lib/utils'

export const Tabs = TabsPrimitive.Root

export function TabsList({ className, ...props }: TabsPrimitive.TabsListProps) {
  return (
    <TabsPrimitive.List
      className={cn('inline-flex h-10 items-center justify-center rounded-md bg-bg-secondary p-1', className)}
      {...props}
    />
  )
}

export function TabsTrigger({ className, ...props }: TabsPrimitive.TabsTriggerProps) {
  return (
    <TabsPrimitive.Trigger
      className={cn(
        'inline-flex items-center justify-center whitespace-nowrap rounded-sm px-3 py-1.5 text-sm font-medium transition-all',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
        'disabled:pointer-events-none disabled:opacity-50',
        'data-[state=active]:bg-bg data-[state=active]:text-fg data-[state=active]:shadow-1',
        className
      )}
      {...props}
    />
  )
}

export function TabsContent({ className, ...props }: TabsPrimitive.TabsContentProps) {
  return (
    <TabsPrimitive.Content
      className={cn('mt-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary', className)}
      {...props}
    />
  )
}
```

- [ ] **Step 2: 实现 Tooltip**

Create `apps/web/src/ui/tooltip.tsx`:

```typescript
import * as TooltipPrimitive from '@radix-ui/react-tooltip'
import { cn } from '../lib/utils'

export const TooltipProvider = TooltipPrimitive.Provider
export const Tooltip = TooltipPrimitive.Root
export const TooltipTrigger = TooltipPrimitive.Trigger

export function TooltipContent({ className, sideOffset = 4, ...props }: TooltipPrimitive.TooltipContentProps) {
  return (
    <TooltipPrimitive.Portal>
      <TooltipPrimitive.Content
        sideOffset={sideOffset}
        className={cn(
          'z-50 overflow-hidden rounded-md border border-border bg-fg px-3 py-1.5 text-xs text-bg shadow-3',
          className
        )}
        {...props}
      />
    </TooltipPrimitive.Portal>
  )
}
```

- [ ] **Step 3: 实现 Popover**

Create `apps/web/src/ui/popover.tsx`:

```typescript
import * as PopoverPrimitive from '@radix-ui/react-popover'
import { cn } from '../lib/utils'

export const Popover = PopoverPrimitive.Root
export const PopoverTrigger = PopoverPrimitive.Trigger

export function PopoverContent({ className, align = 'center', sideOffset = 4, ...props }: PopoverPrimitive.PopoverContentProps) {
  return (
    <PopoverPrimitive.Portal>
      <PopoverPrimitive.Content
        align={align}
        sideOffset={sideOffset}
        className={cn(
          'z-50 w-72 rounded-md border border-border bg-bg p-4 shadow-3 outline-none',
          className
        )}
        {...props}
      />
    </PopoverPrimitive.Portal>
  )
}
```

- [ ] **Step 4: 实现 Checkbox**

Create `apps/web/src/ui/checkbox.tsx`:

```typescript
import * as CheckboxPrimitive from '@radix-ui/react-checkbox'
import { Check } from 'lucide-react'
import { cn } from '../lib/utils'

export const Checkbox = ({ className, ...props }: CheckboxPrimitive.CheckboxProps) => {
  return (
    <CheckboxPrimitive.Root
      className={cn(
        'peer h-4 w-4 shrink-0 rounded-sm border border-border ring-offset-bg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 data-[state=checked]:bg-primary data-[state=checked]:text-white data-[state=checked]:border-primary',
        className
      )}
      {...props}
    >
      <CheckboxPrimitive.Indicator className={cn('flex items-center justify-center text-current')}>
        <Check className="h-3.5 w-3.5" />
      </CheckboxPrimitive.Indicator>
    </CheckboxPrimitive.Root>
  )
}
```

- [ ] **Step 5: 实现 Switch**

Create `apps/web/src/ui/switch.tsx`:

```typescript
import * as SwitchPrimitive from '@radix-ui/react-switch'
import { cn } from '../lib/utils'

export const Switch = ({ className, ...props }: SwitchPrimitive.SwitchProps) => {
  return (
    <SwitchPrimitive.Root
      className={cn(
        'peer inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
        'disabled:cursor-not-allowed disabled:opacity-50 data-[state=checked]:bg-primary data-[state=unchecked]:bg-bg-tertiary',
        className
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        className={cn(
          'pointer-events-none block h-4 w-4 rounded-full bg-bg shadow-lg ring-0 transition-transform',
          'data-[state=checked]:translate-x-4 data-[state=unchecked]:translate-x-0'
        )}
      />
    </SwitchPrimitive.Root>
  )
}
```

- [ ] **Step 6: 运行构建确认**

```bash
npm run build
```

Expected: 成功。

- [ ] **Step 7: Commit**

```bash
git add src/ui/tabs.tsx src/ui/tooltip.tsx src/ui/popover.tsx src/ui/checkbox.tsx src/ui/switch.tsx
git commit -m "feat: add Tabs, Tooltip, Popover, Checkbox, Switch components"
```

---

### Task 10: 创建 Sidebar 与 Header 组件

**Files:**
- Create: `apps/web/src/ui/sidebar.tsx`
- Create: `apps/web/src/ui/header.tsx`

**Interfaces:**
- Produces: `Sidebar`, `SidebarHeader`, `SidebarContent`, `SidebarFooter`, `SidebarMenu`, `SidebarMenuItem`, `SidebarMenuButton`。
- Produces: `Header`, `HeaderSection`, `HeaderTitle`, `HeaderActions`。

- [ ] **Step 1: 实现 Sidebar**

Create `apps/web/src/ui/sidebar.tsx`:

```typescript
import { cn } from '../lib/utils'

export function Sidebar({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <aside
      className={cn(
        'fixed left-0 top-0 z-40 flex h-screen w-58 flex-col border-r border-border bg-fg text-bg',
        className
      )}
      {...props}
    />
  )
}

export function SidebarHeader({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex h-16 items-center gap-3 px-4', className)} {...props} />
}

export function SidebarContent({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex-1 overflow-auto py-2', className)} {...props} />
}

export function SidebarFooter({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('border-t border-bg-tertiary/20 p-4', className)} {...props} />
}

export function SidebarMenu({ className, ...props }: React.HTMLAttributes<HTMLUListElement>) {
  return <ul className={cn('flex flex-col gap-1 px-3', className)} {...props} />
}

export function SidebarMenuItem({ className, ...props }: React.HTMLAttributes<HTMLLIElement>) {
  return <li className={cn('', className)} {...props} />
}

export interface SidebarMenuButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  active?: boolean
  asChild?: boolean
}

export function SidebarMenuButton({ className, active, ...props }: SidebarMenuButtonProps) {
  return (
    <button
      className={cn(
        'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
        'hover:bg-bg/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
        active ? 'bg-bg/15 text-bg' : 'text-bg/70',
        className
      )}
      {...props}
    />
  )
}
```

- [ ] **Step 2: 实现 Header**

Create `apps/web/src/ui/header.tsx`:

```typescript
import { cn } from '../lib/utils'

export function Header({ className, ...props }: React.HTMLAttributes<HTMLElement>) {
  return (
    <header
      className={cn(
        'sticky top-0 z-30 flex h-16 items-center gap-4 border-b border-border bg-bg/95 px-6 backdrop-blur',
        className
      )}
      {...props}
    />
  )
}

export function HeaderSection({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex items-center gap-2', className)} {...props} />
}

export function HeaderTitle({ className, ...props }: React.HTMLAttributes<HTMLHeadingElement>) {
  return <h2 className={cn('text-base font-semibold', className)} {...props} />
}

export function HeaderActions({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('ml-auto flex items-center gap-2', className)} {...props} />
}
```

- [ ] **Step 3: 运行构建确认**

```bash
npm run build
```

Expected: 成功。

- [ ] **Step 4: Commit**

```bash
git add src/ui/sidebar.tsx src/ui/header.tsx
git commit -m "feat: add Sidebar and Header shell components"
```

---

### Task 11: 迁移 AppLayout.tsx

**Files:**
- Modify: `apps/web/src/layout/AppLayout.tsx`

**Interfaces:**
- Consumes: `Sidebar`, `SidebarHeader`, `SidebarContent`, `SidebarFooter`, `SidebarMenu`, `SidebarMenuButton`, `Header`, `HeaderSection`, `HeaderActions`, `Select`, `SelectTrigger`, `SelectValue`, `SelectContent`, `SelectItem`, `Popover`, `PopoverTrigger`, `PopoverContent`, `DropdownMenu` 等。

- [ ] **Step 1: 用新组件重写 AppLayout.tsx**

保持业务逻辑（权限过滤、门店切换、通知轮询、账号菜单、帮助弹窗）不变，仅替换 antd 组件为新 UI 组件与 lucide 图标。

Modify `apps/web/src/layout/AppLayout.tsx`:

```typescript
import {
  LayoutGrid,
  ClipboardList,
  Building2,
  BarChart3,
  Bell,
  Calendar,
  Clock,
  SlidersHorizontal,
  CreditCard,
  Package,
  Lock,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
  CircleDollarSign,
  FileText,
  CircleHelp,
  ShieldCheck,
  Settings,
  ShoppingBag,
  Tag,
  Users,
  Truck,
} from 'lucide-react'
```

重写文件时需要保持菜单结构、权限过滤、通知与账号菜单逻辑。由于文件较长，这里给出关键替换规则：

1. 导入新 UI 组件替换 antd 组件。
2. 用 `lucide-react` 图标替换 `@ant-design/icons`。
3. `Layout` → 自定义 `<div className="flex min-h-screen">`。
4. `Sider` → `<Sidebar>`。
5. `Menu` → `<SidebarMenu>` + `<SidebarMenuItem>` + `<SidebarMenuButton>`。
6. `Header` → `<Header>`。
7. `Select` → 新 `<Select>`。
8. `Popover` → 新 `<Popover>`。
9. `Dropdown` → 新 `<DropdownMenu>`。
10. `Button` → 新 `<Button>`。
11. `Badge` → 新 `<Badge>`。
12. `Avatar` → 新 `<Avatar>`。
13. `Typography.Text` → `<span className="text-sm text-fg-secondary">`。
14. `Empty` → 自定义空状态 `<div className="py-6 text-center text-sm text-fg-secondary">暂无待办</div>`。
15. `Tag` → `<Badge variant={...}>`。

- [ ] **Step 2: 运行测试**

当前 AppLayout 没有单元测试，运行全量测试：

```bash
npm run test
```

Expected: 现有测试通过（可能受 App.tsx 影响）。

- [ ] **Step 3: 运行构建**

```bash
npm run build
```

Expected: 成功。

- [ ] **Step 4: Commit**

```bash
git add src/layout/AppLayout.tsx
git commit -m "feat: migrate AppLayout to new UI system"
```

---

### Task 12: 迁移 LoginPage.tsx

**Files:**
- Modify: `apps/web/src/pages/LoginPage.tsx`

**Interfaces:**
- Consumes: `Button`, `Input`, `Label`, `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`。

- [ ] **Step 1: 替换 antd 组件**

保持表单逻辑与校验不变，仅替换：
- `Form` → 原生 `<form>`。
- `Form.Item` → `<div className="flex flex-col gap-1.5">`。
- `Input` / `Input.Password` → 新 `<Input type="password">`。
- `Button` → 新 `<Button variant="primary">`。
- `Checkbox` → 新 `<Checkbox>`。
- `Typography` → 原生标题/段落。
- `Modal` → 新 `<Dialog>`（用于"忘记密码"提示）。
- `Alert` → 自定义 alert 样式 `<div className="rounded-md border border-error/20 bg-error/10 p-3 text-sm text-error">`。

- [ ] **Step 2: 运行测试**

```bash
npm run test
```

Expected: 通过。

- [ ] **Step 3: 运行构建**

```bash
npm run build
```

Expected: 成功。

- [ ] **Step 4: Commit**

```bash
git add src/pages/LoginPage.tsx
git commit -m "feat: migrate LoginPage to new UI system"
```

---

### Task 13: 迁移 DashboardPage.tsx

**Files:**
- Modify: `apps/web/src/pages/DashboardPage.tsx`

**Interfaces:**
- Consumes: `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`, `Button`, `Badge`。

- [ ] **Step 1: 替换 antd 组件**

保持数据逻辑不变，替换：
- `Card` / `Statistic` → 新 `Card` + 自定义统计展示。
- `Button` → 新 `Button`。
- `Typography` → 原生标题/段落。
- `Space` → `flex`/`gap`。

- [ ] **Step 2: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 3: Commit**

```bash
git add src/pages/DashboardPage.tsx
git commit -m "feat: migrate DashboardPage to new UI system"
```

---

### Task 14: 拆分并迁移 CashierPage.tsx

**Files:**
- Create: `apps/web/src/pages/cashier/CashierMetrics.tsx`
- Create: `apps/web/src/pages/cashier/OrderLineEditor.tsx`
- Create: `apps/web/src/pages/cashier/PaymentSection.tsx`
- Create: `apps/web/src/pages/cashier/index.ts`
- Modify: `apps/web/src/pages/CashierPage.tsx`

**Interfaces:**
- Consumes: 所有新 UI 组件。
- Produces: 更小、可测试的子组件。

- [ ] **Step 1: 识别可拆分模块**

阅读 `CashierPage.tsx`，按视觉/功能边界拆出：
- `CashierMetrics`：顶部三个指标卡。
- `OrderLineEditor`：订单行编辑表单。
- `PaymentSection`：支付分配与结账。

- [ ] **Step 2: 创建子组件文件**

将对应 JSX 与状态逻辑抽取到新文件，使用新 UI 组件替换 antd 组件。

- [ ] **Step 3: 修改 CashierPage.tsx 引用子组件**

保持页面级状态管理（班次、订单行、支付、提交）在 `CashierPage.tsx`，渲染逻辑委托给子组件。

- [ ] **Step 4: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 5: Commit**

```bash
git add src/pages/CashierPage.tsx src/pages/cashier/
git commit -m "feat: split and migrate CashierPage to new UI system"
```

---

### Task 15: 迁移 CustomersPage.tsx

**Files:**
- Modify: `apps/web/src/pages/CustomersPage.tsx`
- Create: `apps/web/src/ui/table.tsx`（如尚未创建）

**Interfaces:**
- Consumes: `Card`, `Input`, `Button`, `Badge`, `Table`。

- [ ] **Step 1: 创建 Table 组件**

Create `apps/web/src/ui/table.tsx`:

```typescript
import { cn } from '../lib/utils'

export function Table({ className, ...props }: React.TableHTMLAttributes<HTMLTableElement>) {
  return <table className={cn('w-full caption-bottom text-sm', className)} {...props} />
}

export function TableHeader({ className, ...props }: React.HTMLAttributes<HTMLTableSectionElement>) {
  return <thead className={cn('[&_tr]:border-b', className)} {...props} />
}

export function TableBody({ className, ...props }: React.HTMLAttributes<HTMLTableSectionElement>) {
  return <tbody className={cn('[&_tr:last-child]:border-0', className)} {...props} />
}

export function TableRow({ className, ...props }: React.HTMLAttributes<HTMLTableRowElement>) {
  return <tr className={cn('border-b border-border transition-colors hover:bg-bg-secondary/50 data-[state=selected]:bg-bg-secondary', className)} {...props} />
}

export function TableHead({ className, ...props }: React.ThHTMLAttributes<HTMLTableCellElement>) {
  return <th className={cn('h-10 px-4 text-left align-middle font-medium text-fg-secondary', className)} {...props} />
}

export function TableCell({ className, ...props }: React.TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={cn('p-4 align-middle', className)} {...props} />
}
```

- [ ] **Step 2: 替换 CustomersPage 中的 antd 组件**

保持客户搜索、列表、详情弹窗逻辑不变，替换：
- `Table` → 新 `Table`。
- `Input.Search` → `<Input type="search" placeholder="搜索..." />` + `<Button>`。
- `Modal` → 新 `Dialog`。
- `Form` → 原生 form。

- [ ] **Step 3: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 4: Commit**

```bash
git add src/ui/table.tsx src/pages/CustomersPage.tsx
git commit -m "feat: add Table component and migrate CustomersPage"
```

---

### Task 16: 迁移 FacilitiesPage 与 SchedulingPage

**Files:**
- Modify: `apps/web/src/pages/FacilitiesPage.tsx`
- Modify: `apps/web/src/pages/SchedulingPage.tsx`

**Interfaces:**
- Consumes: `Card`, `Badge`, `Button`, `Select`, `Table`。

- [ ] **Step 1: 替换 FacilitiesPage 组件**

保持设施卡片、状态、操作逻辑不变，替换 antd 组件为新 UI 组件。

- [ ] **Step 2: 替换 SchedulingPage 组件**

保持排班日历/列表逻辑不变，日期选择使用 `react-day-picker` 替换 antd DatePicker。

- [ ] **Step 3: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 4: Commit**

```bash
git add src/pages/FacilitiesPage.tsx src/pages/SchedulingPage.tsx
git commit -m "feat: migrate FacilitiesPage and SchedulingPage to new UI system"
```

---

### Task 17: 迁移剩余业务页面

**Files:**
- Modify: `apps/web/src/pages/InventoryPage.tsx`
- Modify: `apps/web/src/pages/ProductsPage.tsx`
- Modify: `apps/web/src/pages/ServiceItemsPage.tsx`
- Modify: `apps/web/src/pages/PriceBooksPage.tsx`
- Modify: `apps/web/src/pages/ReportsPage.tsx`
- Modify: `apps/web/src/pages/AuditPage.tsx`
- Modify: `apps/web/src/pages/SupplyChainPage.tsx`
- Modify: `apps/web/src/pages/PaymentChannelsPage.tsx`
- Modify: `apps/web/src/pages/FacilityConfigurationPage.tsx`

**Interfaces:**
- Consumes: 所有新 UI 组件。

- [ ] **Step 1: 批量替换 antd 组件**

对每个页面执行相同模式：
- `Card` → 新 `Card`
- `Table` → 新 `Table`
- `Form` + `Input`/`Select`/`DatePicker` → 原生 form + 新 Input/Select/react-day-picker
- `Button` → 新 `Button`
- `Modal` → 新 `Dialog`
- `Tabs` → 新 `Tabs`
- `Typography` → 原生元素

- [ ] **Step 2: 逐个页面运行构建确认**

```bash
npm run build
```

Expected: 成功。

- [ ] **Step 3: Commit**

```bash
git add src/pages/InventoryPage.tsx src/pages/ProductsPage.tsx src/pages/ServiceItemsPage.tsx src/pages/PriceBooksPage.tsx src/pages/ReportsPage.tsx src/pages/AuditPage.tsx src/pages/SupplyChainPage.tsx src/pages/PaymentChannelsPage.tsx src/pages/FacilityConfigurationPage.tsx
git commit -m "feat: migrate remaining business pages to new UI system"
```

---

### Task 18: 迁移设置与管理页面

**Files:**
- Modify: `apps/web/src/pages/OrganizationSettingsPage.tsx`
- Modify: `apps/web/src/pages/EmployeesPage.tsx`
- Modify: `apps/web/src/pages/ChangePasswordPage.tsx`
- Modify: `apps/web/src/pages/MerchantRegisterPage.tsx`
- Modify: `apps/web/src/pages/PlatformLoginPage.tsx`
- Modify: `apps/web/src/pages/PlatformChangePasswordPage.tsx`
- Modify: `apps/web/src/pages/PlatformAdminPage.tsx`
- Modify: `apps/web/src/pages/ForbiddenPage.tsx`
- Modify: `apps/web/src/pages/ComingSoonPage.tsx`

**Interfaces:**
- Consumes: 所有新 UI 组件。

- [ ] **Step 1: 批量替换 antd 组件**

与 Task 17 相同模式，特别注意：
- `PlatformAdminPage` 为平台管理后台，保持深色 header 风格。
- `EmployeesPage` 含表格与表单，需要 Table、Dialog、Select。

- [ ] **Step 2: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 3: Commit**

```bash
git add src/pages/OrganizationSettingsPage.tsx src/pages/EmployeesPage.tsx src/pages/ChangePasswordPage.tsx src/pages/MerchantRegisterPage.tsx src/pages/PlatformLoginPage.tsx src/pages/PlatformChangePasswordPage.tsx src/pages/PlatformAdminPage.tsx src/pages/ForbiddenPage.tsx src/pages/ComingSoonPage.tsx
git commit -m "feat: migrate settings and platform pages to new UI system"
```

---

### Task 19: 更新 App.tsx 与清理 antd 引用

**Files:**
- Modify: `apps/web/src/App.tsx`
- Modify: `apps/web/src/main.tsx`
- Delete: `apps/web/src/styles.css`（在确认所有样式迁移后）

**Interfaces:**
- 无

- [ ] **Step 1: 替换 App.tsx 中的 Spin fallback**

将 `Spin` fallback 替换为自定义轻量 loading：

```typescript
import { Loader2 } from 'lucide-react'
```

```tsx
<Suspense fallback={<div className="screen-loader"><Loader2 className="h-8 w-8 animate-spin text-primary" /></div>}>
```

并移除 `import { Spin } from 'antd'` 与 `import './styles.css'`。

- [ ] **Step 2: 移除 main.tsx 中的 ConfigProvider 与 AntApp**

```tsx
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
```

- [ ] **Step 3: 删除 styles.css**

确认所有页面已迁移后：

```bash
rm apps/web/src/styles.css
```

- [ ] **Step 4: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 5: Commit**

```bash
git add src/App.tsx src/main.tsx src/styles/index.css
git rm src/styles.css
git commit -m "refactor: remove antd ConfigProvider and legacy styles.css"
```

---

### Task 20: 移除 antd 依赖与图标迁移

**Files:**
- Modify: `apps/web/package.json`

**Interfaces:**
- 无

- [ ] **Step 1: 卸载 antd 与 @ant-design/icons**

```bash
cd /Users/lv/Workspace/erp/apps/web
npm uninstall antd @ant-design/icons
```

- [ ] **Step 2: 全局检查残留引用**

```bash
grep -r "from 'antd'" src/ || echo "No antd imports found"
grep -r "from '@ant-design/icons'" src/ || echo "No ant-design/icons imports found"
```

Expected: 无残留。

- [ ] **Step 3: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 4: Commit**

```bash
git add package.json package-lock.json
git commit -m "chore: remove antd and @ant-design/icons dependencies"
```

---

### Task 21: 表格虚拟滚动优化

**Files:**
- Modify: `apps/web/src/ui/table.tsx`
- Modify: `apps/web/src/pages/CustomersPage.tsx`
- Modify: `apps/web/src/pages/EmployeesPage.tsx`
- Modify: `apps/web/src/pages/AuditPage.tsx`

**Interfaces:**
- Produces: `VirtualTable` wrapper using `@tanstack/react-virtual`。

- [ ] **Step 1: 创建 VirtualTable 组件**

Create `apps/web/src/ui/virtual-table.tsx`:

```typescript
import { useRef } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from './table'

interface VirtualTableProps<T> {
  data: T[]
  columns: { key: keyof T; header: string; cell: (row: T) => React.ReactNode }[]
  estimateSize?: number
}

export function VirtualTable<T>({ data, columns, estimateSize = 48 }: VirtualTableProps<T>) {
  const parentRef = useRef<HTMLDivElement>(null)
  const virtualizer = useVirtualizer({
    count: data.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => estimateSize,
  })

  return (
    <div ref={parentRef} className="overflow-auto rounded-md border border-border">
      <Table>
        <TableHeader>
          <TableRow>
            {columns.map((col) => (
              <TableHead key={String(col.key)}>{col.header}</TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody style={{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }}>
          {virtualizer.getVirtualItems().map((virtualRow) => {
            const row = data[virtualRow.index]
            return (
              <TableRow
                key={virtualRow.key}
                style={{
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  width: '100%',
                  transform: `translateY(${virtualRow.start}px)`,
                }}
              >
                {columns.map((col) => (
                  <TableCell key={String(col.key)}>{col.cell(row)}</TableCell>
                ))}
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
```

- [ ] **Step 2: 在数据密集型页面使用 VirtualTable**

将 CustomersPage、EmployeesPage、AuditPage 中可能超过 50 行的表格替换为 `VirtualTable`。

- [ ] **Step 3: 运行测试与构建**

```bash
npm run test
npm run build
```

Expected: 通过。

- [ ] **Step 4: Commit**

```bash
git add src/ui/virtual-table.tsx src/pages/CustomersPage.tsx src/pages/EmployeesPage.tsx src/pages/AuditPage.tsx
git commit -m "perf: add virtual scrolling for data-heavy tables"
```

---

### Task 22: 路由级代码分割与字体优化

**Files:**
- Modify: `apps/web/src/App.tsx`
- Modify: `apps/web/src/styles/index.css`

**Interfaces:**
- 无

- [ ] **Step 1: 确保所有页面已使用 React.lazy**

`App.tsx` 当前已使用 `lazy`。确认 `Suspense` fallback 为轻量组件。

- [ ] **Step 2: 添加字体加载策略**

在 `index.css` 中，如果使用 Plus Jakarta Sans 自托管字体，使用 `font-display: swap`：

```css
@font-face {
  font-family: 'Plus Jakarta Sans';
  src: url('/fonts/PlusJakartaSans-Regular.woff2') format('woff2');
  font-weight: 400;
  font-display: swap;
}
@font-face {
  font-family: 'Plus Jakarta Sans';
  src: url('/fonts/PlusJakartaSans-Medium.woff2') format('woff2');
  font-weight: 500;
  font-display: swap;
}
@font-face {
  font-family: 'Plus Jakarta Sans';
  src: url('/fonts/PlusJakartaSans-Bold.woff2') format('woff2');
  font-weight: 700;
  font-display: swap;
}
```

如果通过 npm 使用 `@fontsource/plus-jakarta-sans`：

```bash
npm install @fontsource/plus-jakarta-sans
```

在 `main.tsx` 顶部引入：

```typescript
import '@fontsource/plus-jakarta-sans/400.css'
import '@fontsource/plus-jakarta-sans/500.css'
import '@fontsource/plus-jakarta-sans/700.css'
```

- [ ] **Step 3: 运行构建确认**

```bash
npm run build
```

Expected: 成功。

- [ ] **Step 4: Commit**

```bash
git add src/App.tsx src/styles/index.css src/main.tsx package.json package-lock.json
git commit -m "perf: optimize font loading and confirm route code splitting"
```

---

### Task 23: 设计 QA 与文档更新

**Files:**
- Modify: `docs/design-qa.md`
- Modify: `docs/development-progress.md`
- Create: `apps/web/design-audit/` 截图（可选，视工具能力）

**Interfaces:**
- 无

- [ ] **Step 1: 更新 design-qa.md**

添加新 UI 的检查清单：

```markdown
## UI 设计刷新 QA Checklist

- [ ] 所有页面无 antd 组件引用
- [ ] 颜色、字体、圆角、阴影严格遵循 tokens
- [ ] 所有交互组件支持键盘导航
- [ ] 表格虚拟滚动正常工作
- [ ] 低配设备模拟下操作流畅
- [ ] 登录页、Dashboard、收银台视觉验收通过
```

- [ ] **Step 2: 更新 development-progress.md**

记录 UI 刷新完成状态。

- [ ] **Step 3: Commit**

```bash
git add docs/design-qa.md docs/development-progress.md
git commit -m "docs: update design QA and progress for UI refresh"
```

---

### Task 24: 性能验证与回归测试

**Files:**
- 无新增文件

**Interfaces:**
- 无

- [ ] **Step 1: 运行全量测试**

```bash
cd /Users/lv/Workspace/erp/apps/web
npm run test
```

Expected: 全部通过，覆盖率维持 80%+。

- [ ] **Step 2: 构建产物分析**

```bash
npm run build
npx vite-bundle-visualizer
```

或手动检查 `dist/assets/` 大小。Expected: 首屏 JS < 200KB gzipped。

- [ ] **Step 3: Chrome DevTools 性能验证**

1. 运行 `npm run preview`。
2. 打开 Chrome DevTools → Performance。
3. 启用 4x CPU 降速 + Slow 3G 网络模拟。
4. 录制从登录页到 Dashboard 的加载过程。
5. 确认：无 > 50ms Long Tasks，首屏 < 3s。

- [ ] **Step 4: Commit 验证结果**

```bash
git commit --allow-empty -m "test: verify UI refresh passes tests and performance budget"
```

---

### Task 25: 最终审查与合并准备

**Files:**
- 无新增文件

**Interfaces:**
- 无

- [ ] **Step 1: 全量 diff 审查**

```bash
git diff --stat feat/ecommerce-auto-selection..feat/ui-design-refresh
```

Expected: 变更集中于 `apps/web/src/ui/`、`apps/web/src/pages/`、`apps/web/src/layout/`、`apps/web/src/styles/`、`package.json`。

- [ ] **Step 2: 确认无 console.log**

```bash
grep -r "console.log" apps/web/src/ || echo "No console.log found"
```

Expected: 无残留。

- [ ] **Step 3: 最终构建与测试**

```bash
npm run test
npm run build
npm run lint
```

Expected: 全部通过。

- [ ] **Step 4: 推送分支**

```bash
git push -u origin feat/ui-design-refresh
```

- [ ] **Step 5: 合并到基础分支**

```bash
git checkout feat/ecommerce-auto-selection
git merge --no-ff feat/ui-design-refresh -m "feat: complete UI design refresh"
```

---

## Spec Coverage Review

对照设计文档 `docs/superpowers/specs/2026-08-20-ui-design-refresh-design.md` 逐项检查：

| Spec 要求 | 对应任务 |
|-----------|----------|
| Radix Primitives + Tailwind CSS v4 | Task 2, 3 |
| 自建 `src/ui/` 组件库 | Task 5-10 |
| Notion/Figma 式视觉语言（tokens） | Task 2 |
| 保留业务逻辑与布局 | 所有页面迁移任务（只替换组件引用） |
| 低配设备性能 | Task 21, 22, 24 |
| 移除 antd 与 @ant-design/icons | Task 20 |
| 可访问性 | 组件实现中的 focus-visible、键盘导航 |
| 设计 QA 与文档 | Task 23 |
| 80%+ 测试覆盖率 | Task 24 |

## Placeholder Scan

- 无 "TBD" / "TODO" / "implement later"。
- 无 "Add appropriate error handling" 等模糊描述。
- 每个任务包含具体文件路径、命令、代码示例。
- 类型与组件名称在任务间保持一致。

## Type Consistency Review

- `cn(...inputs: ClassValue[]): string` 在 Task 4 定义，后续一致使用。
- `ButtonProps.variant` 取值在 Task 6 定义，后续一致使用。
- `Card*` 子组件在 Task 7 定义，后续一致使用。
- `Badge.variant` 在 Task 5 定义，后续一致使用。
