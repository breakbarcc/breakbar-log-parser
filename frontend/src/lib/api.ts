// Client for the breakbar API (docs/PLAN.md 4a). Same origin; the anonymous identity is an HttpOnly cookie.

export type JobStatus = 'queued' | 'running' | 'done' | 'failed' | 'cancelled'

export interface Job {
  id: string
  fileName: string
  fileBytes: number | null
  status: JobStatus
  position: number | null
  batchId: string | null
  anonymized: boolean
  reportId: string | null
  reportUrl: string | null
  jsonUrl: string | null
  encounterId: string | null
  logName: string | null
  encounterSuccess: boolean | null
  durationMs: number | null
  error: string | null
  createdAt: string
  finishedAt: string | null
}

/** Largest upload the server accepts (Uploads:MaxUploadBytes). */
export const MAX_UPLOAD_BYTES = 200 * 1024 * 1024

export class UploadError extends Error {
  constructor(
    message: string,
    /** HTTP status, 0 for network errors. */
    readonly status: number,
  ) {
    super(message)
  }

  /** Worth trying again later (rate limit, server busy, network). */
  get retryable(): boolean {
    return this.status === 0 || this.status === 429 || this.status === 503
  }
}

export async function fetchJobs(): Promise<Job[]> {
  const response = await fetch('/api/jobs', { credentials: 'same-origin' })
  if (!response.ok) throw new Error(`GET /api/jobs: ${response.status}`)
  return (await response.json()) as Job[]
}

export async function cancelJob(id: string): Promise<boolean> {
  const response = await fetch(`/api/jobs/${id}`, { method: 'DELETE', credentials: 'same-origin' })
  return response.ok
}

/** Message shown to the user for a failed upload. The server's details are English; the UI is German. */
export function uploadErrorMessage(status: number): string {
  switch (status) {
    case 0:
      return 'Verbindung fehlgeschlagen'
    case 400:
      return 'Kein gültiges Logformat (.evtc, .zevtc, .evtc.zip)'
    case 413:
      return 'Log zu groß für diesen Server'
    case 422:
      return 'Keine gültige arcdps-Logdatei'
    case 429:
      return 'Zu viele Uploads – bitte kurz warten'
    case 503:
      return 'Server gerade ausgelastet – bitte später erneut versuchen'
    default:
      return `Upload fehlgeschlagen (HTTP ${status})`
  }
}

export interface UploadHandle {
  promise: Promise<Job>
  abort: () => void
}

/**
 * Uploads one file as raw body (POST /api/uploads). XMLHttpRequest instead of fetch,
 * because only XHR reports upload progress.
 */
export function uploadFile(
  file: File,
  options: { anonymize: boolean; batch: string },
  onProgress: (fraction: number) => void,
): UploadHandle {
  const xhr = new XMLHttpRequest()
  const params = new URLSearchParams({ name: file.name, batch: options.batch })
  if (options.anonymize) params.set('anonymize', 'true')

  const promise = new Promise<Job>((resolve, reject) => {
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress(e.loaded / e.total)
    }
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText) as Job)
      } else {
        reject(new UploadError(uploadErrorMessage(xhr.status), xhr.status))
      }
    }
    xhr.onerror = () => reject(new UploadError(uploadErrorMessage(0), 0))
    xhr.onabort = () => reject(new UploadError('Abgebrochen', -1))
  })

  xhr.open('POST', `/api/uploads?${params.toString()}`)
  xhr.setRequestHeader('Content-Type', 'application/octet-stream')
  xhr.send(file)
  return { promise, abort: () => xhr.abort() }
}
