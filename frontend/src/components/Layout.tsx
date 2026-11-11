import { Link, NavLink, Outlet } from 'react-router'
import { useUploads } from '../lib/uploads'
import { EI_REPO } from './links'

function Logo() {
  return (
    <Link to="/" className="flex items-center gap-3 text-text no-underline hover:text-text">
      <svg width="36" height="20" viewBox="0 0 36 20" aria-hidden="true" fill="none">
        <rect x="1" y="4" width="34" height="12" rx="2" stroke="#3A4452" strokeWidth="2" />
        <rect x="4" y="7" width="20" height="6" rx="1" className="fill-accent" />
        <path d="M12 7v6M18 7v6" stroke="#0B0E12" strokeWidth="1.5" />
      </svg>
      <span className="font-display text-xl font-bold tracking-[-0.01em]">
        breakbar<span className="text-accent">-logs</span>
      </span>
    </Link>
  )
}

const navItem =
  'rounded-lg px-3.5 py-2.5 text-[15px] font-medium text-muted no-underline hover:bg-raised hover:text-text aria-[current=page]:bg-raised aria-[current=page]:text-text'

function Header() {
  const { rows } = useUploads()
  const active = rows.filter((r) => r.state === 'pending' || r.state === 'uploading' || r.state === 'queued' || r.state === 'running').length
  return (
    <header className="border-b border-line bg-bg-deep">
      <div className="mx-auto flex max-w-[1200px] flex-wrap items-center justify-between gap-4 px-4 py-4 sm:px-8">
        <Logo />
        <nav aria-label="Hauptnavigation" className="flex flex-wrap items-center gap-1">
          <NavLink to="/" end className={navItem}>
            Hochladen
          </NavLink>
          <NavLink to="/uploads" className={navItem}>
            Meine Uploads
            {active > 0 && (
              <span className="ml-2 rounded-full bg-accent-soft px-2 py-0.5 text-xs font-semibold text-accent" aria-label={`${active} in Arbeit`}>
                {active}
              </span>
            )}
          </NavLink>
          <Link to="/#api" className={navItem}>
            API
          </Link>
          <Link to="/#about" className={navItem}>
            Über
          </Link>
          <a
            href="https://breakbar.cc"
            className="ml-3 rounded-lg border border-[#2A3340] px-3.5 py-2.5 text-[15px] text-text-2 no-underline hover:text-text"
          >
            breakbar.cc ↗
          </a>
        </nav>
      </div>
    </header>
  )
}

function Footer() {
  return (
    <footer className="border-t border-line bg-bg-deep">
      <div className="mx-auto flex max-w-[1200px] flex-wrap justify-between gap-x-6 gap-y-3 px-4 py-6 text-sm text-faint sm:px-8">
        <p className="m-0 max-w-[720px]">
          Guild Wars 2 © ArenaNet, LLC. breakbar-logs ist ein Fan-Projekt und steht in keiner Verbindung zu ArenaNet oder NCSOFT.
          Auswertung: <a href={EI_REPO}>GW2 Elite Insights</a>.
        </p>
        <nav aria-label="Rechtliches" className="flex gap-5">
          <Link to="/impressum" className="text-[#A9B3C0]">
            Impressum
          </Link>
          <Link to="/datenschutz" className="text-[#A9B3C0]">
            Datenschutz
          </Link>
          <a href="https://breakbar.cc" className="text-[#A9B3C0]">
            breakbar.cc
          </a>
        </nav>
      </div>
    </footer>
  )
}

export function Layout() {
  return (
    <div className="flex min-h-screen flex-col bg-bg text-text">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-10 focus:rounded-lg focus:bg-raised focus:px-4 focus:py-2">
        Zum Inhalt springen
      </a>
      <Header />
      <main id="main" className="flex-1">
        <Outlet />
      </main>
      <Footer />
    </div>
  )
}
