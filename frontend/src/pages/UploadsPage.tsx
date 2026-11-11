import { useCallback, useState } from 'react'
import { Link } from 'react-router'
import { CheckIcon, CopyIcon, ExternalIcon, LinkIcon, PlusIcon, UploadIcon } from '../components/icons'
import { EI_REPO } from '../components/links'
import { absoluteUrl, formatBytes, formatDuration } from '../lib/format'
import { summarize, summaryText, type Row, type RowState } from '../lib/rows'
import { useUploads } from '../lib/uploads'
import { useAnonymizeSetting, useFileDrop } from '../lib/useFileDrop'

const pill: Record<RowState, { label: string; className: string }> = {
  pending: { label: 'Wartet auf Upload', className: 'bg-info-soft text-info' },
  uploading: { label: 'Upload', className: 'bg-info-soft text-info' },
  'upload-error': { label: 'Fehler', className: 'bg-bad-soft text-bad' },
  queued: { label: 'Warteschlange', className: 'bg-line text-text-2' },
  running: { label: 'Wird verarbeitet', className: 'bg-warn-soft text-warn' },
  done: { label: 'Fertig', className: 'bg-accent-soft text-accent' },
  failed: { label: 'Fehler', className: 'bg-bad-soft text-bad' },
  cancelled: { label: 'Abgebrochen', className: 'bg-line text-faint' },
}

// Table from md upwards; on phones every row becomes a stacked card.
const grid = 'md:grid md:grid-cols-[minmax(0,2.2fr)_minmax(0,1.2fr)_minmax(0,2.4fr)_minmax(0,1.5fr)] md:gap-6'
const iconButton =
  'inline-flex size-11 cursor-pointer items-center justify-center rounded-[10px] border border-line-strong bg-raised text-text-2 hover:text-text'
const textButton =
  'min-h-11 cursor-pointer rounded-[10px] border border-line-strong bg-transparent px-4 text-[15px] text-text-2 hover:text-text'

function useCopy(): [string | null, (key: string, text: string) => void] {
  const [copied, setCopied] = useState<string | null>(null)
  const copy = useCallback((key: string, text: string) => {
    void navigator.clipboard.writeText(text).then(() => {
      setCopied(key)
      setTimeout(() => setCopied((current) => (current === key ? null : current)), 2000)
    })
  }, [])
  return [copied, copy]
}

function StatusCell({ row }: { row: Row }) {
  const look = pill[row.state]
  const progress = row.local?.state === 'uploading' ? Math.round(row.local.progress * 100) : null
  return (
    <div className="flex flex-col gap-2">
      <span className={`inline-flex items-center gap-1.5 self-start rounded-full px-2.5 py-1 text-[13px] font-semibold ${look.className}`}>
        <span className="size-[7px] rounded-full bg-current" />
        {look.label}
      </span>
      {progress !== null && (
        <div role="progressbar" aria-label="Upload" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100} className="h-1.5 overflow-hidden rounded-sm bg-line">
          <div className="h-1.5 bg-info transition-[width]" style={{ width: `${progress}%` }} />
        </div>
      )}
      {row.state === 'running' && (
        <div role="progressbar" aria-label="Wird verarbeitet" className="h-1.5 overflow-hidden rounded-sm bg-line">
          <div className="animate-indeterminate h-1.5 w-2/5 bg-warn" />
        </div>
      )}
    </div>
  )
}

function ResultCell({ row }: { row: Row }) {
  const job = row.job
  if (row.state === 'done' && job) {
    const success = job.encounterSuccess === true
    return (
      <div className="flex flex-col gap-0.5">
        <span className="font-semibold">{job.logName ?? job.encounterId ?? 'Unbekannter Kampf'}</span>
        <span className="text-sm text-muted">
          <span className={`font-semibold ${success ? 'text-ok' : 'text-bad'}`}>{success ? 'Erfolg' : 'Fehlschlag'}</span>
          {job.durationMs != null && <> · {formatDuration(job.durationMs)}</>}
          {job.anonymized && <> · anonymisiert</>}
        </span>
      </div>
    )
  }
  const hint = (() => {
    switch (row.state) {
      case 'pending':
        return 'Wartet auf einen freien Upload-Platz …'
      case 'uploading':
        return `Wird hochgeladen · ${Math.round((row.local?.progress ?? 0) * 100)} %`
      case 'queued':
        return job?.position != null ? `In der Warteschlange · ${job.position === 0 ? 'als Nächstes dran' : `${job.position} vor dir`}` : 'In der Warteschlange'
      case 'running':
        return 'Elite Insights wertet den Kampf aus …'
      case 'upload-error':
        return row.local?.error
      case 'failed':
        return job?.error ?? 'Verarbeitung fehlgeschlagen'
      case 'cancelled':
        return 'Abgebrochen'
      default:
        return null
    }
  })()
  const isError = row.state === 'upload-error' || row.state === 'failed'
  return <span className={`text-sm ${isError ? 'text-bad' : 'text-faint'}`}>{hint}</span>
}

function ActionsCell({ row, copied, copy }: { row: Row; copied: string | null; copy: (key: string, text: string) => void }) {
  const { dismiss, retry, cancel } = useUploads()
  const job = row.job
  if (row.state === 'done' && job?.reportUrl) {
    const url = absoluteUrl(job.reportUrl)
    return (
      <div className="flex items-center gap-2">
        <button type="button" aria-label={`Link zu ${row.fileName} kopieren`} className={iconButton} onClick={() => copy(row.key, url)}>
          {copied === row.key ? <CheckIcon className="text-accent" /> : <LinkIcon />}
        </button>
        <a
          href={url}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex min-h-11 items-center gap-1.5 rounded-[10px] border border-[#2E5A57] bg-[#10201F] px-4 text-[15px] font-medium text-text no-underline hover:text-text hover:brightness-125"
        >
          Öffnen
          <ExternalIcon size={14} />
        </a>
      </div>
    )
  }
  if (row.state === 'queued' && job) {
    return (
      <button type="button" className={textButton} onClick={() => void cancel(job.id)}>
        Abbrechen
      </button>
    )
  }
  if (row.state === 'upload-error' && row.local?.retryable) {
    return (
      <div className="flex gap-2">
        <button type="button" className={textButton} onClick={() => retry(row.local!.localId)}>
          Erneut versuchen
        </button>
        <button type="button" className={textButton} onClick={() => dismiss(row)}>
          Entfernen
        </button>
      </div>
    )
  }
  if (row.state === 'upload-error' || row.state === 'failed' || row.state === 'cancelled') {
    return (
      <button type="button" className={textButton} onClick={() => dismiss(row)}>
        Entfernen
      </button>
    )
  }
  return null
}

export function UploadsPage() {
  const { rows, loaded, addFiles } = useUploads()
  const [anonymize] = useAnonymizeSetting()
  const onFiles = useCallback((files: File[]) => addFiles(files, anonymize), [addFiles, anonymize])
  const dragging = useFileDrop(onFiles)
  const [copied, copy] = useCopy()

  const reportUrls = rows.flatMap((r) => (r.state === 'done' && r.job?.reportUrl ? [absoluteUrl(r.job.reportUrl)] : []))

  return (
    <div className="mx-auto flex w-full max-w-[1200px] flex-col gap-6 px-4 pb-20 pt-12 sm:px-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex flex-col gap-1.5">
          <h1 className="m-0 font-display text-4xl font-bold leading-[1.1] tracking-[-0.015em]">Deine Uploads</h1>
          <p className="m-0 text-muted" aria-live="polite">
            {loaded ? summaryText(summarize(rows)) : 'Lade …'}
          </p>
        </div>
        <div className="flex flex-wrap gap-3">
          <button
            type="button"
            disabled={reportUrls.length === 0}
            onClick={() => copy('all', reportUrls.join('\n'))}
            className="inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-[10px] border border-line-strong bg-raised px-[18px] font-medium text-text disabled:cursor-not-allowed disabled:opacity-50"
          >
            {copied === 'all' ? <CheckIcon className="text-accent" /> : <CopyIcon />}
            {copied === 'all' ? 'Kopiert' : 'Alle Links kopieren'}
          </button>
          <Link
            to="/"
            className="inline-flex min-h-11 items-center gap-2 rounded-[10px] bg-accent px-[18px] font-semibold text-accent-ink no-underline hover:text-accent-ink hover:brightness-110"
          >
            <PlusIcon strokeWidth={2.2} />
            Weitere Dateien
          </Link>
        </div>
      </div>

      <div
        className={`flex flex-wrap items-center gap-x-4 gap-y-3 rounded-xl border-2 border-dashed px-6 py-[18px] text-[15px] transition-colors ${
          dragging ? 'border-accent bg-accent-soft text-text' : 'border-line-strong bg-drop text-muted'
        }`}
      >
        <UploadIcon size={22} strokeWidth={1.8} className="text-accent" />
        <span>Weitere Logs einfach auf diese Seite ziehen – sie werden an die Liste angehängt.</span>
      </div>

      <div className="overflow-x-auto rounded-[14px] border border-line bg-panel">
        <div className="md:min-w-[760px]" role="table" aria-label="Uploads">
          <div role="row" className={`hidden ${grid} border-b border-line px-6 py-3.5 text-[13px] font-semibold uppercase tracking-[0.04em] text-faint`}>
            <span role="columnheader">Datei</span>
            <span role="columnheader">Status</span>
            <span role="columnheader">Ergebnis</span>
            <span role="columnheader" className="text-right">
              Bericht
            </span>
          </div>
          {loaded && rows.length === 0 && (
            <p className="m-0 px-6 py-10 text-center text-muted">
              Noch keine Uploads. <Link to="/">Logs hochladen</Link>
            </p>
          )}
          {rows.map((row) => (
            <div role="row" key={row.key} className={`flex flex-col gap-3 ${grid} md:items-center border-b border-line-soft px-5 py-[18px] last:border-b-0 md:px-6`}>
              <div role="cell" className="flex min-w-0 flex-col gap-0.5">
                <span className="truncate font-mono text-sm" title={row.fileName}>
                  {row.fileName}
                </span>
                {row.size !== null && <span className="text-[13px] text-faint">{formatBytes(row.size)}</span>}
              </div>
              <div role="cell">
                <StatusCell row={row} />
              </div>
              <div role="cell" className="min-w-0">
                <ResultCell row={row} />
              </div>
              <div role="cell" className="flex items-center gap-2 md:justify-end">
                <ActionsCell row={row} copied={copied} copy={copy} />
              </div>
            </div>
          ))}
        </div>
      </div>

      <p className="m-0 text-sm text-faint">
        Berichte liegen unter <span className="font-mono text-text-2">reports.breakbar.cc/r/…</span> und sind für jeden sichtbar, der den Link hat.
        Erzeugt mit <a href={EI_REPO}>GW2 Elite Insights</a>.
      </p>
    </div>
  )
}
