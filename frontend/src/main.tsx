import { StrictMode, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Link, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import { UploadsProvider } from './lib/uploads'
import { IMPRESSUM_URL } from './components/links'
import { PrivacyPage } from './pages/PrivacyPage'
import { UploadPage } from './pages/UploadPage'
import { UploadsPage } from './pages/UploadsPage'
import './styles.css'

/** Old /impressum links: the Impressum is maintained on the main site. */
function ExternalRedirect({ to }: { to: string }) {
  useEffect(() => window.location.replace(to), [to])
  return (
    <p className="mx-auto max-w-[800px] px-4 py-16 text-muted sm:px-8">
      Weiterleitung … <a href={to}>Weiter zum Impressum</a>
    </p>
  )
}

function NotFound() {
  return (
    <div className="mx-auto flex max-w-[800px] flex-col gap-4 px-4 py-16 sm:px-8">
      <h1 className="m-0 font-display text-4xl font-bold">Seite nicht gefunden</h1>
      <p className="m-0 text-muted">
        Zurück zur <Link to="/">Startseite</Link>.
      </p>
    </div>
  )
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      {/* Above the routes: uploads keep running while the user switches pages. */}
      <UploadsProvider>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<UploadPage />} />
            <Route path="uploads" element={<UploadsPage />} />
            <Route path="impressum" element={<ExternalRedirect to={IMPRESSUM_URL} />} />
            <Route path="datenschutz" element={<PrivacyPage />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </UploadsProvider>
    </BrowserRouter>
  </StrictMode>,
)
