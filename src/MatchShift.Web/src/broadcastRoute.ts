import type { Lang } from './languages'

export type BroadcastRoute = 'broadcast' | 'overlay'

export function broadcastRoute(pathname: string, base: string): BroadcastRoute | null {
  const prefix = base.replace(/\/+$/, '')
  const path = pathname.replace(/\/+$/, '')
  if (path === `${prefix}/broadcast`) return 'broadcast'
  if (path === `${prefix}/overlay`) return 'overlay'
  return null
}

export function broadcastHref(route: BroadcastRoute, base: string, search: string, session: string, lang: Lang): string {
  const current = new URLSearchParams(search)
  const query = new URLSearchParams({ session, lang })
  for (const key of ['demo', 'minute', 'match']) {
    const value = current.get(key)
    if (value !== null) query.set(key, value)
  }
  return `${base.replace(/\/+$/, '')}/${route}?${query}`
}
