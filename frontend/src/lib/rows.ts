import type { Job, JobStatus } from './api'

/** A file in the browser that is not (yet) a server job: waiting to upload, uploading, or rejected. */
export interface LocalUpload {
  localId: string
  fileName: string
  size: number
  state: 'pending' | 'uploading' | 'error'
  /** 0..1 while uploading. */
  progress: number
  error?: string
  retryable?: boolean
  anonymize: boolean
  batch: string
  file: File
}

export type RowState = 'pending' | 'uploading' | 'upload-error' | JobStatus

export interface Row {
  key: string
  fileName: string
  size: number | null
  state: RowState
  local?: LocalUpload
  job?: Job
}

const statusRank: Record<JobStatus, number> = { queued: 0, running: 1, done: 2, failed: 2, cancelled: 2 }

/**
 * Merges a job update into the list. Updates can arrive out of order (upload response vs. SSE),
 * so a job never moves back to an earlier status.
 */
export function upsertJob(jobs: Job[], incoming: Job): Job[] {
  const index = jobs.findIndex((j) => j.id === incoming.id)
  if (index < 0) return [incoming, ...jobs]
  const current = jobs[index]!
  if (statusRank[incoming.status] < statusRank[current.status]) return jobs
  const next = jobs.slice()
  next[index] = incoming
  return next
}

/** Local uploads first (in the order they were added), then server jobs, newest first. */
export function buildRows(locals: LocalUpload[], jobs: Job[], dismissed: ReadonlySet<string>): Row[] {
  const localRows: Row[] = locals.map((l) => ({
    key: l.localId,
    fileName: l.fileName,
    size: l.size,
    state: l.state === 'error' ? 'upload-error' : l.state,
    local: l,
  }))
  const jobRows: Row[] = jobs
    .filter((j) => !dismissed.has(j.id))
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    .map((j) => ({ key: j.id, fileName: j.fileName, size: j.fileBytes ?? null, state: j.status, job: j }))
  return [...localRows, ...jobRows]
}

export interface Summary {
  total: number
  done: number
  working: number
  failed: number
}

export function summarize(rows: Row[]): Summary {
  let done = 0
  let working = 0
  let failed = 0
  for (const row of rows) {
    if (row.state === 'done') done++
    else if (row.state === 'failed' || row.state === 'upload-error') failed++
    else if (row.state !== 'cancelled') working++
  }
  return { total: rows.length, done, working, failed }
}

/** "6 Dateien · 3 fertig · 2 in Arbeit · 1 Fehler" */
export function summaryText(s: Summary): string {
  const files = s.total === 1 ? '1 Datei' : `${s.total} Dateien`
  return `${files} · ${s.done} fertig · ${s.working} in Arbeit · ${s.failed} Fehler`
}
