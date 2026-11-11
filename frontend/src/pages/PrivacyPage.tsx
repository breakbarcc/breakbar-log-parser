import type { ReactNode } from 'react'
import { IMPRESSUM_URL } from '../components/links'

// Datenschutzerklärung für logs.breakbar.cc und reports.breakbar.cc.
// Sie beschreibt, was die Anwendung tatsächlich tut (docs/PLAN.md 4b, 6a). Wer die Anwendung ändert
// (neue Cookies, Dienste, Speicherdauer, Drittanbieter in den Berichten), muss diesen Text mitändern.

const REPORT_RETENTION_DAYS = 7
const UPDATED = '11.11.2026'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="m-0 font-display text-2xl font-semibold">{title}</h2>
      {children}
    </section>
  )
}

const P = ({ children }: { children: ReactNode }) => <p className="m-0 text-text-2">{children}</p>
const Ul = ({ children }: { children: ReactNode }) => <ul className="m-0 flex list-disc flex-col gap-1.5 pl-6 text-text-2">{children}</ul>
const Code = ({ children }: { children: ReactNode }) => <code className="font-mono text-[0.9em] text-text">{children}</code>

export function PrivacyPage() {
  return (
    <div className="mx-auto flex w-full max-w-[820px] flex-col gap-10 px-4 pb-20 pt-12 sm:px-8">
      <header className="flex flex-col gap-3">
        <h1 className="m-0 font-display text-4xl font-bold leading-[1.1] tracking-[-0.015em]">Datenschutzerklärung</h1>
        <p className="m-0 text-faint">Stand: {UPDATED}</p>
      </header>

      <Section title="Geltungsbereich">
        <P>
          Diese Erklärung gilt für breakbar-logs unter <Code>logs.breakbar.cc</Code> (Anwendung und Schnittstelle) und{' '}
          <Code>reports.breakbar.cc</Code> (erzeugte Berichte). Für die Hauptseite <Code>breakbar.cc</Code> und die anderen Tools auf
          Subdomains gelten deren eigene Datenschutzerklärungen.
        </P>
      </Section>

      <Section title="Verantwortlicher">
        <P>
          Verantwortlich im Sinne der DSGVO ist Patrick Schmidt, Fischenzstr. 39, Konstanz, E-Mail:{' '}
          <a href="mailto:info@breakbar.cc">info@breakbar.cc</a>. Weitere Angaben stehen im{' '}
          <a href={IMPRESSUM_URL}>Impressum</a>.
        </P>
      </Section>

      <Section title="Kurzfassung">
        <Ul>
          <li>Kein Benutzerkonto, kein Tracking, keine Werbung, keine Analyse-Dienste.</li>
          <li>
            Hochgeladene Logs und die daraus erzeugten Berichte werden <strong>höchstens {REPORT_RETENTION_DAYS} Tage</strong> gespeichert und
            dann automatisch gelöscht.
          </li>
          <li>Ein technisch notwendiges Cookie ordnet deine Uploads deinem Browser zu.</li>
          <li>
            <strong>Die Berichtsseiten laden Inhalte von Drittanbietern nach</strong> (u. a. Google Fonts, Cloudflare, ArenaNet). Dabei wird
            deine IP-Adresse an diese Anbieter übertragen, siehe Abschnitt „Berichtsseiten“.
          </li>
        </Ul>
      </Section>

      <Section title="Hosting und Verbindungsdaten">
        <P>
          Die Anwendung läuft auf einem gemieteten virtuellen Server (VPS). Der Hosting-Anbieter verarbeitet als Auftragsverarbeiter die für den
          Betrieb technisch notwendigen Verbindungsdaten, insbesondere deine IP-Adresse. Wir selbst schreiben keine Zugriffsprotokolle mit
          IP-Adressen.
        </P>
        <P>
          Die IP-Adresse nutzt die Anwendung nur, um die Zahl der Uploads pro Minute zu begrenzen (Schutz vor Überlastung und Missbrauch).
          Dieser Zähler liegt ausschließlich im Arbeitsspeicher und wird nicht dauerhaft gespeichert. Rechtsgrundlage ist unser berechtigtes
          Interesse an einem stabilen und sicheren Betrieb (Art. 6 Abs. 1 lit. f DSGVO).
        </P>
      </Section>

      <Section title="Hochgeladene Logs und Berichte">
        <P>
          Wenn du arcdps-Logs (<Code>.evtc</Code>, <Code>.zevtc</Code>) hochlädst, werten wir sie mit dem Open-Source-Programm Elite Insights aus
          und erzeugen einen Bericht (HTML-Seite und JSON-Datei). Rechtsgrundlage für die Verarbeitung deiner Daten ist die Erbringung des von
          dir genutzten Dienstes (Art. 6 Abs. 1 lit. b DSGVO).
        </P>
        <P>Ein Log und der Bericht enthalten üblicherweise:</P>
        <Ul>
          <li>Charakternamen und Accountnamen (z. B. <Code>Name.1234</Code>) aller Spieler im Kampf, also auch von Mitspielern,</li>
          <li>Klassen, Spezialisierungen, Gruppen und Spielwerte (Schaden, Heilung, Effekte, Ereignisse mit Zeitangaben),</li>
          <li>Angaben zum Kampf (Gegner, Ort, Dauer, Ergebnis).</li>
        </Ul>
        <P>
          Die Namen von Mitspielern verarbeiten wir auf Grundlage unseres berechtigten Interesses, gemeinsame Spielinhalte auswerten und
          teilen zu können (Art. 6 Abs. 1 lit. f DSGVO). Mit der Option „Spielernamen anonymisieren“ beim Hochladen entfernt Elite Insights
          die Namen aus dem Bericht. Bitte gib den Berichtslink nur an Personen weiter, für die er bestimmt ist.
        </P>
        <P>
          <strong>Speicherdauer:</strong> Die hochgeladene Originaldatei wird gelöscht, sobald die Auswertung abgeschlossen ist oder
          fehlschlägt. Der Bericht und die zugehörigen Verwaltungsdaten werden <strong>spätestens {REPORT_RETENTION_DAYS} Tage nach ihrer
          Erstellung</strong> automatisch gelöscht, ebenso aus Sicherungskopien. Danach ist der Link nicht mehr erreichbar.
        </P>
        <P>
          <strong>Wer kann Berichte sehen?</strong> Jeder, der den Link kennt. Die Adresse enthält eine zufällige Kennung und ist nicht zu
          erraten. Es gibt keine öffentliche Liste aller Berichte, und Suchmaschinen sind angewiesen, Berichte nicht zu indexieren. Lädt
          jemand dieselbe Datei erneut hoch, wird der bereits erzeugte Bericht wiederverwendet. Über eine Schnittstelle kann auch das Tool
          voidlog.breakbar.cc Logs zur Auswertung einreichen; für dessen eigene Datenverarbeitung gilt dessen Datenschutzerklärung.
        </P>
        <P>
          Du kannst die Löschung eines Berichts vor Ablauf der Frist verlangen: Schreibe uns den Link an{' '}
          <a href="mailto:info@breakbar.cc">info@breakbar.cc</a>.
        </P>
      </Section>

      <Section title="Verwaltungsdaten zu deinen Uploads">
        <P>
          Für die Liste „Meine Uploads“ speichern wir pro Upload: Dateiname, Zeitpunkte, Status, eventuelle Fehlermeldung, eine Prüfsumme
          (SHA-256) der Datei, die Wahl der Anonymisierung, die Kennung deines Browsers (siehe nächster Abschnitt) und Eckdaten des Kampfes
          (Gegner, Ergebnis, Dauer). Diese Daten werden zusammen mit dem Bericht nach spätestens {REPORT_RETENTION_DAYS} Tagen gelöscht.
          Rechtsgrundlage ist Art. 6 Abs. 1 lit. b DSGVO.
        </P>
      </Section>

      <Section title="Cookie und lokaler Speicher">
        <P>
          Beim ersten Besuch setzt die Anwendung das Cookie <Code>bb_uid</Code>. Es enthält eine zufällige Kennung ohne Personenbezug, ist nur
          für <Code>logs.breakbar.cc</Code> gültig (nicht für Subdomains), wird nur über HTTPS und nicht für Skripte lesbar (HttpOnly)
          gesendet und läuft nach einem Jahr ab. Es ist technisch erforderlich, damit du deine eigenen Uploads wiederfindest und niemand
          sonst sie sieht. Rechtsgrundlage ist § 25 Abs. 2 Nr. 2 TDDDG; eine Einwilligung ist dafür nicht nötig. Löschst du das Cookie,
          verlierst du den Zugriff auf deine Upload-Liste; die Berichtslinks funktionieren weiter, bis sie ablaufen.
        </P>
        <P>
          Im lokalen Speicher deines Browsers merkt sich die Seite zwei Einstellungen: ob „Spielernamen anonymisieren“ angehakt ist
          (<Code>breakbar.anonymize</Code>) und welche Einträge du aus deiner Liste entfernt hast (<Code>breakbar.dismissedJobs</Code>). Diese
          Daten verlassen deinen Browser nicht.
        </P>
        <P>
          Das Sprach-Cookie <Code>breakbar-language</Code> der Hauptseite breakbar.cc gilt für alle Subdomains und wird deshalb von deinem
          Browser auch an diese Seite gesendet. Wir lesen und verwenden es hier nicht.
        </P>
        <P>
          Diese Seiten nutzen keine Analyse-, Tracking- oder Werbe-Dienste und binden keine Inhalte von Drittanbietern ein. Auch die Schriften
          der Anwendung (Inter, Space Grotesk, JetBrains Mono) liegen auf unserem Server und werden <strong>nicht</strong> von Google Fonts geladen.
        </P>
      </Section>

      <Section title="Berichtsseiten (Elite Insights): Inhalte von Drittanbietern">
        <P>
          Die HTML-Berichte unter <Code>reports.breakbar.cc</Code> werden vom Programm Elite Insights erzeugt. Sie laden beim Öffnen Skripte,
          Stile, Schriften und Bilder von fremden Servern nach. Dabei erhalten diese Anbieter technisch zwingend deine IP-Adresse, die
          Adresse der aufgerufenen Seite und Angaben zu deinem Browser. Das betrifft:
        </P>
        <Ul>
          <li>
            <strong>Google Fonts</strong> (<Code>fonts.googleapis.com</Code>, <Code>fonts.gstatic.com</Code>; Google Ireland Limited bzw.
            Google LLC, USA): die Schriftart „Open Sans“,
          </li>
          <li>
            <strong>Cloudflare cdnjs</strong> (<Code>cdnjs.cloudflare.com</Code>; Cloudflare, Inc., USA): Design- und Skriptbibliotheken
            (Bootstrap-Theme, Popper, Pako),
          </li>
          <li>
            <strong>jsDelivr</strong> (<Code>cdn.jsdelivr.net</Code>): Bootstrap und Vue,
          </li>
          <li>
            <strong>jQuery</strong> (<Code>code.jquery.com</Code>) und <strong>Plotly</strong> (<Code>cdn.plot.ly</Code>): jQuery und die
            Diagrammbibliothek,
          </li>
          <li>
            <strong>ArenaNet</strong> (<Code>render.guildwars2.com</Code>, <Code>wiki.guildwars2.com</Code>; ArenaNet, LLC, USA): Symbole
            und Bilder zu Fertigkeiten, Effekten und Gegnern,
          </li>
          <li>
            nur bei Bedarf oder wenn eine Quelle ausfällt: <Code>i.imgur.com</Code> (Bilder, teilweise über{' '}
            <Code>external-content.duckduckgo.com</Code>), <Code>icons-gw2.darthmaim-cdn.com</Code> und <Code>assets.gw2dat.com</Code>{' '}
            (Ersatzquellen für Symbole).
          </li>
        </Ul>
        <P>
          Einige dieser Anbieter sitzen in den USA oder nutzen Server dort. Wir haben keinen Einfluss auf die Datenverarbeitung dieser
          Anbieter; es gelten deren Datenschutzerklärungen. Wir setzen die Inhalte ein, weil die Berichte ohne sie nicht funktionieren
          (berechtigtes Interesse, Art. 6 Abs. 1 lit. f DSGVO). Möchtest du diese Übertragungen vermeiden, öffne keine Berichte oder nutze
          Schutzfunktionen deines Browsers, etwa einen Inhaltsblocker. Beachte, dass die Berichte dann unter Umständen unvollständig
          dargestellt werden.
        </P>
        <P>
          Die Berichtsseite setzt selbst keine Cookies. Sie speichert im lokalen Speicher deines Browsers Anzeigeeinstellungen von Elite
          Insights (Schlüssel mit dem Präfix <Code>ei_</Code>); diese Daten verlassen deinen Browser nicht.
        </P>
      </Section>

      <Section title="Empfänger und Drittlandübermittlung">
        <P>
          Deine Daten geben wir nicht weiter, verkaufen sie nicht und verknüpfen sie nicht mit anderen Datenquellen. Empfänger sind nur der
          Hosting-Anbieter (als Auftragsverarbeiter) und beim Öffnen eines Berichts die oben genannten Anbieter von Inhalten, an die dein
          Browser unmittelbar Anfragen sendet.
        </P>
      </Section>

      <Section title="Deine Rechte">
        <P>
          Du hast das Recht auf Auskunft (Art. 15 DSGVO), Berichtigung (Art. 16), Löschung (Art. 17), Einschränkung der Verarbeitung
          (Art. 18) und Datenübertragbarkeit (Art. 20) sowie das Recht, der Verarbeitung auf Grundlage berechtigter Interessen zu
          widersprechen (Art. 21). Du kannst dich außerdem bei einer Datenschutz-Aufsichtsbehörde beschweren, etwa der für deinen Wohnort
          zuständigen. Wende dich bei Fragen oder zur Ausübung deiner Rechte an <a href="mailto:info@breakbar.cc">info@breakbar.cc</a>.
        </P>
        <P>
          Da wir Berichte nur anhand ihres Links zuordnen können, nenne uns für Anfragen bitte den Link des betroffenen Berichts.
        </P>
      </Section>

      <Section title="Änderungen">
        <P>
          Wir passen diese Erklärung an, wenn sich die Anwendung oder die Rechtslage ändert. Es gilt die jeweils hier veröffentlichte Fassung.
        </P>
      </Section>
    </div>
  )
}
