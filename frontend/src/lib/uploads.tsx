import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { cancelJob, fetchJobs, MAX_UPLOAD_BYTES, UploadError, uploadFile, type Job } from './api'
import { isLogFile } from './format'
import { buildRows, upsertJob, type LocalUpload, type Row } from './rows'

/** Parallel uploads per browser; the server parses at most 2 logs at a time anyway. */
const UPLOAD_CONCURRENCY = 3
const DISMISSED_KEY = 'breakbar.dismissedJobs'

interface UploadsContextValue {
  rows: Row[]
  loaded: boolean
  /** Queue files for upload; invalid ones show up as error rows. */
  addFiles: (files: Iterable<File>, anonymize: boolean) => void
  retry: (localId: string) => void
  /** Hide a finished/failed row (local uploads are dropped, server jobs remembered as dismissed). */
  dismiss: (row: Row) => void
  cancel: (jobId: string) => Promise<void>
}

const UploadsContext = createContext<UploadsContextValue | null>(null)

export function useUploads(): UploadsContextValue {
  const value = useContext(UploadsContext)
  if (!value) throw new Error('useUploads must be used inside <UploadsProvider>')
  return value
}

function loadDismissed(): Set<string> {
  try {
    return new Set(JSON.parse(localStorage.getItem(DISMISSED_KEY) ?? '[]') as string[])
  } catch {
    return new Set()
  }
}

function saveDismissed(ids: Set<string>) {
  try {
    localStorage.setItem(DISMISSED_KEY, JSON.stringify([...ids].slice(-1000)))
  } catch {
    // private mode etc.: dismissing just won't survive a reload
  }
}

export function UploadsProvider({ children }: { children: ReactNode }) {
  const [jobs, setJobs] = useState<Job[]>([])
  const [locals, setLocals] = useState<LocalUpload[]>([])
  const [positions, setPositions] = useState<Record<string, number>>({})
  const [dismissed, setDismissed] = useState<Set<string>>(loadDismissed)
  const [loaded, setLoaded] = useState(false)

  const localsRef = useRef<LocalUpload[]>([])
  localsRef.current = locals
  const running = useRef(0)

  // ---- Server state: initial list + live updates via Server-Sent Events ----
  const reload = useCallback(async () => {
    try {
      setJobs(await fetchJobs())
    } finally {
      setLoaded(true)
    }
  }, [])

  useEffect(() => {
    const events = new EventSource('/api/events')
    // "ready" also fires after every automatic reconnect: re-sync whatever was missed meanwhile.
    events.addEventListener('ready', () => void reload())
    events.addEventListener('job', (e) => {
      const job = JSON.parse((e as MessageEvent<string>).data) as Job
      setJobs((list) => upsertJob(list, job))
    })
    events.addEventListener('positions', (e) => {
      const data = JSON.parse((e as MessageEvent<string>).data) as { positions: Record<string, number> }
      setPositions(data.positions)
    })
    return () => events.close()
  }, [reload])

  // ---- Uploads ----
  const updateLocal = useCallback((localId: string, patch: Partial<LocalUpload>) => {
    setLocals((list) => list.map((l) => (l.localId === localId ? { ...l, ...patch } : l)))
  }, [])

  const pump = useCallback(() => {
    while (running.current < UPLOAD_CONCURRENCY) {
      const next = localsRef.current.find((l) => l.state === 'pending')
      if (!next) return
      running.current++
      // Mark synchronously in the ref so the loop does not pick the same file twice.
      localsRef.current = localsRef.current.map((l) => (l.localId === next.localId ? { ...l, state: 'uploading' } : l))
      updateLocal(next.localId, { state: 'uploading', progress: 0 })

      const { promise } = uploadFile(next.file, { anonymize: next.anonymize, batch: next.batch }, (progress) =>
        updateLocal(next.localId, { progress }),
      )
      promise
        .then((job) => {
          setJobs((list) => upsertJob(list, job))
          setLocals((list) => list.filter((l) => l.localId !== next.localId))
        })
        .catch((error: unknown) => {
          const e = error instanceof UploadError ? error : new UploadError('Upload fehlgeschlagen', 0)
          updateLocal(next.localId, { state: 'error', error: e.message, retryable: e.retryable })
        })
        .finally(() => {
          running.current--
          pump()
        })
    }
  }, [updateLocal])

  const addFiles = useCallback(
    (files: Iterable<File>, anonymize: boolean) => {
      const batch = crypto.randomUUID()
      const added = [...files].map((file): LocalUpload => {
        const base = { localId: crypto.randomUUID(), fileName: file.name, size: file.size, progress: 0, anonymize, batch, file }
        if (!isLogFile(file.name)) {
          return { ...base, state: 'error', error: 'Kein gültiges Logformat (.evtc, .zevtc, .evtc.zip)', retryable: false }
        }
        if (file.size > MAX_UPLOAD_BYTES) {
          return { ...base, state: 'error', error: 'Datei größer als 200 MB', retryable: false }
        }
        return { ...base, state: 'pending' }
      })
      localsRef.current = [...localsRef.current, ...added]
      setLocals(localsRef.current)
      pump()
    },
    [pump],
  )

  const retry = useCallback(
    (localId: string) => {
      localsRef.current = localsRef.current.map((l) => (l.localId === localId ? { ...l, state: 'pending', error: undefined } : l))
      setLocals(localsRef.current)
      pump()
    },
    [pump],
  )

  const dismiss = useCallback((row: Row) => {
    if (row.local) {
      setLocals((list) => list.filter((l) => l.localId !== row.local!.localId))
    } else if (row.job) {
      setDismissed((set) => {
        const next = new Set(set).add(row.job!.id)
        saveDismissed(next)
        return next
      })
    }
  }, [])

  const cancel = useCallback(async (jobId: string) => {
    await cancelJob(jobId) // the SSE "job" event updates the row
  }, [])

  const rows = useMemo(() => {
    const withPositions = jobs.map((j) => (j.status === 'queued' && positions[j.id] !== undefined ? { ...j, position: positions[j.id]! } : j))
    return buildRows(locals, withPositions, dismissed)
  }, [jobs, locals, positions, dismissed])

  const value = useMemo(() => ({ rows, loaded, addFiles, retry, dismiss, cancel }), [rows, loaded, addFiles, retry, dismiss, cancel])
  return <UploadsContext.Provider value={value}>{children}</UploadsContext.Provider>
}
