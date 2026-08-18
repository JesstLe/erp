interface BrandLogoProps {
  className?: string
  label?: string
}

export function BrandLogo({ className, label }: BrandLogoProps) {
  return <img className={className} src="/favicon.svg" alt={label ?? ''} aria-hidden={label ? undefined : true} />
}
