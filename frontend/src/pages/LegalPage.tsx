// Impressum and Datenschutz are legally required for a public German website.
// The texts must come from the operator; until then these pages show a clear placeholder.

export function LegalPage({ title }: { title: string }) {
  return (
    <div className="mx-auto flex w-full max-w-[800px] flex-col gap-4 px-4 pb-20 pt-12 sm:px-8">
      <h1 className="m-0 font-display text-4xl font-bold">{title}</h1>
      <p className="m-0 rounded-xl border border-dashed border-line-strong bg-panel p-6 text-muted">
        Dieser Text wird vom Betreiber noch ergänzt.
      </p>
    </div>
  )
}
