import { useCallback, useEffect, useId, useRef } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { FolderIcon, GitHubIcon, HeartIcon, UploadIcon } from '../components/icons'
import { EI_REPO } from '../components/links'
import { useUploads } from '../lib/uploads'
import { useAnonymizeSetting, useFileDrop } from '../lib/useFileDrop'

const steps = [
  { n: '01', title: 'Hochladen', text: 'Wähle eine oder mehrere Logdateien aus deinem arcdps-Ordner – auch gleich einen ganzen Raid-Abend.' },
  { n: '02', title: 'Verarbeiten', text: 'Elite Insights analysiert jeden Kampf: Schaden, Boons, Mechaniken, Zeitleiste.' },
  { n: '03', title: 'Teilen', text: 'Jeder Log bekommt einen eigenen Link auf reports.breakbar.cc – ideal für Discord und Gildenchat.' },
]

export function UploadPage() {
  const { addFiles } = useUploads()
  const navigate = useNavigate()
  const location = useLocation()
  const inputRef = useRef<HTMLInputElement>(null)
  const anonymizeId = useId()
  const [anonymize, setAnonymize] = useAnonymizeSetting()

  const start = useCallback(
    (files: File[]) => {
      if (files.length === 0) return
      addFiles(files, anonymize)
      void navigate('/uploads')
    },
    [addFiles, anonymize, navigate],
  )
  const dragging = useFileDrop(start)

  // Header links "API" and "Über" point to sections of this page.
  useEffect(() => {
    if (location.hash) document.getElementById(location.hash.slice(1))?.scrollIntoView({ behavior: 'smooth' })
  }, [location.hash])

  return (
    <>
      <section className="mx-auto flex max-w-[1200px] flex-col gap-4 px-4 pb-8 pt-12 sm:px-8 sm:pt-[72px]">
        <p className="m-0 font-mono text-[13px] uppercase tracking-[0.08em] text-accent">arcdps · .evtc · .zevtc</p>
        <h1 className="m-0 max-w-[760px] font-display text-4xl font-bold leading-[1.05] tracking-[-0.02em] sm:text-[56px]">
          Logs hochladen.
          <br />
          Bericht teilen.
        </h1>
        <p className="m-0 max-w-[640px] text-lg text-muted">
          Zieh deine arcdps-Logs hierher – breakbar-logs wertet sie mit Elite Insights aus und gibt dir für jeden Kampf einen teilbaren Link zum Bericht.
        </p>
      </section>

      <section aria-labelledby="upload-title" className="mx-auto max-w-[1200px] px-4 pt-4 sm:px-8">
        <h2 id="upload-title" className="sr-only">
          Dateien hochladen
        </h2>
        <div
          className={`flex flex-col items-center gap-5 rounded-2xl border-2 border-dashed px-8 py-16 text-center transition-colors ${
            dragging ? 'border-accent bg-accent-soft' : 'border-line-strong bg-drop'
          }`}
        >
          <div className="flex size-[72px] items-center justify-center rounded-2xl bg-[#16222A] text-accent">
            <UploadIcon size={36} strokeWidth={1.8} />
          </div>
          <div className="flex flex-col gap-1">
            <p className="m-0 font-display text-2xl font-semibold">{dragging ? 'Loslassen zum Hochladen' : 'Dateien hierher ziehen'}</p>
            <p className="m-0 text-muted">eine oder mehrere auf einmal</p>
          </div>
          <button
            type="button"
            onClick={() => inputRef.current?.click()}
            className="inline-flex min-h-12 cursor-pointer items-center gap-2.5 rounded-[10px] border-0 bg-accent px-6 font-semibold text-accent-ink hover:brightness-110"
          >
            <FolderIcon size={18} />
            Dateien auswählen
          </button>
          <input
            ref={inputRef}
            type="file"
            multiple
            accept=".evtc,.zevtc,.zip"
            className="hidden"
            onChange={(e) => {
              start([...(e.target.files ?? [])])
              e.target.value = ''
            }}
          />
          <p className="m-0 text-sm text-faint">.evtc, .zevtc oder .evtc.zip · max. 200 MB pro Datei</p>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-x-8 gap-y-3 rounded-xl border border-line bg-panel px-5 py-4">
          <span className="text-sm font-semibold text-text-2">Optionen</span>
          <label htmlFor={anonymizeId} className="flex min-h-11 cursor-pointer items-center gap-2.5 text-[15px] text-text-2">
            <input
              id={anonymizeId}
              type="checkbox"
              checked={anonymize}
              onChange={(e) => setAnonymize(e.target.checked)}
              className="size-[18px] accent-accent"
            />
            Spielernamen anonymisieren
          </label>
          <span className="text-sm text-faint">Berichte sind für jeden sichtbar, der den Link hat.</span>
        </div>
      </section>

      <section aria-labelledby="how-title" className="mx-auto flex max-w-[1200px] flex-col gap-6 px-4 pt-[72px] sm:px-8">
        <h2 id="how-title" className="m-0 font-display text-[28px] font-semibold">
          So funktioniert's
        </h2>
        <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(260px,1fr))]">
          {steps.map((s) => (
            <div key={s.n} className="flex flex-col gap-2 rounded-xl border border-line bg-card p-6">
              <span className="font-mono text-[13px] text-accent">{s.n}</span>
              <h3 className="m-0 text-lg font-semibold">{s.title}</h3>
              <p className="m-0 text-[15px] text-muted">{s.text}</p>
            </div>
          ))}
        </div>
      </section>

      <section id="api" aria-labelledby="api-title" className="mx-auto flex max-w-[1200px] scroll-mt-6 flex-col gap-4 px-4 pt-[72px] sm:px-8">
        <h2 id="api-title" className="m-0 font-display text-[28px] font-semibold">
          API
        </h2>
        <p className="m-0 max-w-[760px] text-muted">
          Logs lassen sich auch per Skript hochladen. Jede Datei wird als Request-Body gesendet; die Antwort enthält den Job, dessen
          Status du abfragen kannst. Ein Cookie-Speicher (<code className="font-mono text-text-2">-c/-b</code>) hält deine Uploads zusammen.
        </p>
        <pre className="m-0 overflow-x-auto rounded-xl border border-line bg-panel p-5 font-mono text-[13px] leading-relaxed text-text-2">
          {`curl -c jar -b jar --data-binary @fight.zevtc \\
  "https://logs.breakbar.cc/api/uploads?name=fight.zevtc"

curl -b jar https://logs.breakbar.cc/api/jobs          # Status, reportUrl
curl -b jar -N https://logs.breakbar.cc/api/events     # Live-Updates (SSE)`}
        </pre>
        <p className="m-0 text-sm text-faint">
          Optionen: <code className="font-mono">anonymize=true</code>, <code className="font-mono">batch=&lt;id&gt;</code>. Für Dienste mit vielen
          Uploads gibt es API-Keys auf Anfrage.
        </p>
      </section>

      <section id="about" aria-labelledby="ei-title" className="mx-auto max-w-[1200px] scroll-mt-6 px-4 pb-20 pt-12 sm:px-8">
        <div className="flex flex-wrap items-center justify-between gap-6 rounded-2xl border border-[#23413F] bg-[#0F1A1C] px-8 py-7">
          <div className="flex min-w-0 flex-[1_1_520px] items-start gap-5">
            <HeartIcon size={40} strokeWidth={1.6} className="flex-none text-accent" />
            <div className="flex flex-col gap-1.5">
              <h2 id="ei-title" className="m-0 font-display text-[22px] font-semibold">
                Angetrieben von GW2 Elite Insights
              </h2>
              <p className="m-0 max-w-[680px] text-[15px] text-[#A9B3C0]">
                Die gesamte Auswertung übernimmt der Open-Source-Parser Elite Insights von baaron4 und vielen Mitwirkenden. breakbar-logs ist
                nur die Bühne – die Arbeit steckt dort. Danke!
              </p>
            </div>
          </div>
          <a
            href={EI_REPO}
            className="inline-flex min-h-12 items-center gap-2.5 rounded-[10px] border border-[#2E5A57] px-5 font-medium text-text no-underline hover:text-text hover:brightness-125"
          >
            <GitHubIcon size={18} strokeWidth={1.8} />
            Elite Insights auf GitHub
          </a>
        </div>
      </section>
    </>
  )
}
