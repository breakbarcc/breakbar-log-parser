import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Link, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import { UploadsProvider } from './lib/uploads'
import { LegalPage } from './pages/LegalPage'
import { UploadPage } from './pages/UploadPage'
import { UploadsPage } from './pages/UploadsPage'
import './styles.css'

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
            <Route path="impressum" element={<LegalPage title="Impressum" />} />
            <Route path="datenschutz" element={<LegalPage title="Datenschutz" />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </UploadsProvider>
    </BrowserRouter>
  </StrictMode>,
)
