import iconUrl from '@/assets/koshi-icon.png'
import logoUrl from '@/assets/koshi-logo.png'

export function KoshiLogo({ className = 'h-8' }: { className?: string }) {
  return <img src={logoUrl} alt="KOSHI" className={`w-auto select-none ${className}`} draggable={false} />
}

export function KoshiIcon({ className = 'size-8' }: { className?: string }) {
  return <img src={iconUrl} alt="" aria-hidden="true" className={`select-none ${className}`} draggable={false} />
}
