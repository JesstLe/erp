import { useLayoutEffect, useRef } from 'react'

export function useCashierWorkspaceHeight(visible = true) {
  const ref = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const element = ref.current
    if (!visible || !element) return
    const content = element.closest('.app-content, .classic-content')
    const resize = () => {
      const bottomPadding = content ? parseFloat(getComputedStyle(content).paddingBottom) || 0 : 0
      const viewportHeight = window.visualViewport?.height ?? window.innerHeight
      const top = element.getBoundingClientRect().top + window.scrollY
      const mobileNavigation = element.closest('.classic-shell') && window.innerWidth <= 640 ? 58 : 0
      const height = Math.max(240, viewportHeight - top - bottomPadding - mobileNavigation)
      element.style.setProperty('--cashier-workspace-height', `${height}px`)
    }
    resize()
    window.addEventListener('resize', resize)
    window.visualViewport?.addEventListener('resize', resize)
    const observer = new ResizeObserver(resize)
    if (content) observer.observe(content)
    return () => {
      observer.disconnect()
      window.removeEventListener('resize', resize)
      window.visualViewport?.removeEventListener('resize', resize)
    }
  }, [visible])
  return ref
}
