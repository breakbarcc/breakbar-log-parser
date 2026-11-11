import { describe, expect, it } from 'vitest'
import type { Job } from './api'
import { formatBytes, formatDuration, isLogFile } from './format'
import { buildRows, summarize, summaryText, upsertJob, type LocalUpload } from './rows'

const job = (id: string, status: Job['status'], createdAt: string): Job => ({
  id,
  fileName: `${id}.zevtc`,
  fileBytes: 1000,
  status,
  position: null,
  batchId: null,
  anonymized: false,
  reportId: null,
  reportUrl: null,
  jsonUrl: null,
  encounterId: null,
  logName: null,
  encounterSuccess: null,
  durationMs: null,
  error: null,
  createdAt,
  finishedAt: null,
})

const local = (localId: string, state: LocalUpload['state']): LocalUpload => ({
  localId,
  fileName: `${localId}.zevtc`,
  size: 10,
  state,
  progress: 0,
  anonymize: false,
  batch: 'b',
  file: undefined as unknown as File,
})

describe('upsertJob', () => {
  it('adds new jobs at the front', () => {
    expect(upsertJob([job('a', 'queued', '1')], job('b', 'queued', '2')).map((j) => j.id)).toEqual(['b', 'a'])
  })

  it('never moves a job back to an earlier status (late upload response after SSE)', () => {
    const list = [job('a', 'done', '1')]
    expect(upsertJob(list, job('a', 'queued', '1'))[0]!.status).toBe('done')
    expect(upsertJob([job('a', 'queued', '1')], job('a', 'running', '1'))[0]!.status).toBe('running')
  })
})

describe('buildRows', () => {
  it('shows local uploads first, then jobs newest first, without dismissed jobs', () => {
    const rows = buildRows(
      [local('l1', 'uploading')],
      [job('old', 'done', '2026-11-11T10:00:00Z'), job('new', 'queued', '2026-11-11T11:00:00Z'), job('hidden', 'failed', '2026-11-11T12:00:00Z')],
      new Set(['hidden']),
    )
    expect(rows.map((r) => r.key)).toEqual(['l1', 'new', 'old'])
    expect(rows[0]!.state).toBe('uploading')
  })

  it('maps local errors to upload-error', () => {
    expect(buildRows([local('x', 'error')], [], new Set())[0]!.state).toBe('upload-error')
  })
})

describe('summary', () => {
  it('counts states', () => {
    const rows = buildRows([local('l', 'error')], [job('a', 'done', '1'), job('b', 'running', '2'), job('c', 'cancelled', '3')], new Set())
    expect(summaryText(summarize(rows))).toBe('4 Dateien · 1 fertig · 1 in Arbeit · 1 Fehler')
  })
})

describe('format', () => {
  it('formats sizes and durations', () => {
    expect(formatBytes(2.4 * 1024 * 1024)).toBe('2,4 MB')
    expect(formatBytes(512 * 1024)).toBe('512 KB')
    expect(formatDuration(192_000)).toBe('3:12')
    expect(formatDuration(3_725_000)).toBe('1:02:05')
  })

  it('accepts only log files', () => {
    expect(isLogFile('a.zevtc')).toBe(true)
    expect(isLogFile('A.EVTC')).toBe(true)
    expect(isLogFile('a.evtc.zip')).toBe(true)
    expect(isLogFile('screenshot.png')).toBe(false)
    expect(isLogFile('other.zip')).toBe(false)
  })
})
