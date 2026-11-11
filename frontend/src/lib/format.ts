const LOG_EXTENSIONS = ['.evtc', '.zevtc', '.evtc.zip']

/** Same rule as the server: .evtc, .zevtc or .evtc.zip. */
export function isLogFile(name: string): boolean {
  const lower = name.toLowerCase()
  return LOG_EXTENSIONS.some((ext) => lower.endsWith(ext))
}

const sizeFormat = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 1 })

/** "2,4 MB", "512 KB", "800 B". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${sizeFormat.format(bytes / 1024)} KB`
  return `${sizeFormat.format(bytes / (1024 * 1024))} MB`
}

/** Fight duration: "3:12", "1:02:05". */
export function formatDuration(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const ss = String(s).padStart(2, '0')
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`
}

/** Absolute URL for sharing; report URLs from the API may be relative in development. */
export function absoluteUrl(url: string): string {
  return new URL(url, window.location.origin).toString()
}
