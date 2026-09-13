import { StrictMode, useEffect, useState } from 'react';
import type * as React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { Button } from '@manoir-app/core-admin-ui-kit/button';
import { Card } from '@manoir-app/core-admin-ui-kit/card';
import { DefaultAdminShell } from '@manoir-app/core-admin-ui-kit/default-admin-shell';
import { PageHeader } from '@manoir-app/core-admin-ui-kit/page-header';
import { SidebarNav } from '@manoir-app/core-admin-ui-kit/sidebar-nav';
import { ShellHeader } from '@manoir-app/core-admin-ui-kit/shell-header';
import { StatusDot } from '@manoir-app/core-admin-ui-kit/status-dot';
import { ToggleSwitch } from '@manoir-app/core-admin-ui-kit/toggle-switch';
import { checkHomeAutomationHealth, getHomeAutomationServiceInfo } from './api';
import '../../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/styles/tokens.css';
import '../../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/styles/base.css';
import manoirLogo from '../../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Front/src/assets/logo.svg';
import './app.css';

type PageId = 'overview' | 'rooms' | 'devices' | 'people' | 'scenes' | 'integrations' | 'automations' | 'dashboards' | 'entities' | 'logs' | 'settings';

const pages: Record<PageId, { path: string; title: string; description: string }> = {
  overview: { path: '/space/overview', title: "Vue d'ensemble", description: "Le tableau de bord de votre espace sera bientôt disponible ici." },
  rooms: { path: '/space/rooms', title: 'Zones et pièces', description: 'La gestion de vos zones et pièces sera bientôt disponible ici.' },
  devices: { path: '/space/devices', title: 'Appareils', description: 'La liste et le pilotage de vos appareils seront bientôt disponibles ici.' },
  people: { path: '/space/people', title: 'Personnes', description: 'La gestion des personnes et de leur présence sera bientôt disponible ici.' },
  scenes: { path: '/space/scenes', title: 'Scènes', description: 'La gestion de vos scènes sera bientôt disponible ici.' },
  integrations: { path: '/configuration/integrations', title: 'Intégrations', description: 'La configuration des intégrations sera bientôt disponible ici.' },
  automations: { path: '/configuration/automations', title: 'Automatisations', description: 'La création et la gestion des automatisations seront bientôt disponibles ici.' },
  dashboards: { path: '/configuration/dashboards', title: 'Dashboards', description: 'La personnalisation de vos dashboards sera bientôt disponible ici.' },
  entities: { path: '/configuration/entities', title: 'Entités', description: 'La gestion des entités sera bientôt disponible ici.' },
  logs: { path: '/administration/logs', title: 'Journaux', description: 'La consultation des journaux sera bientôt disponible ici.' },
  settings: { path: '/administration/settings', title: 'Réglages', description: "Les réglages de l'application seront bientôt disponibles ici." },
};

function createNavigationSections(activePath: string, navigate: ReturnType<typeof useNavigate>) {
  const navigateInternally = (path: string) => (event: React.MouseEvent<HTMLElement>) => {
    event.preventDefault();
    navigate(path);
  };

  return [
  {
    id: 'space',
    title: 'Espace',
    items: [
      { id: 'overview', label: "Vue d'ensemble", href: pages.overview.path, active: activePath === pages.overview.path, onClick: navigateInternally(pages.overview.path) },
      { id: 'rooms', label: 'Zones et pièces', href: pages.rooms.path, active: activePath === pages.rooms.path, onClick: navigateInternally(pages.rooms.path) },
      { id: 'devices', label: 'Appareils', href: pages.devices.path, active: activePath === pages.devices.path, onClick: navigateInternally(pages.devices.path) },
      { id: 'people', label: 'Personnes', href: pages.people.path, active: activePath === pages.people.path, onClick: navigateInternally(pages.people.path) },
      { id: 'scenes', label: 'Scènes', href: pages.scenes.path, active: activePath === pages.scenes.path, onClick: navigateInternally(pages.scenes.path) },
    ],
  },
  {
    id: 'configuration',
    title: 'Configuration',
    items: [
      { id: 'integrations', label: 'Intégrations', href: pages.integrations.path, active: activePath === pages.integrations.path, onClick: navigateInternally(pages.integrations.path) },
      { id: 'automations', label: 'Automatisations', href: pages.automations.path, active: activePath === pages.automations.path, onClick: navigateInternally(pages.automations.path) },
      { id: 'dashboards', label: 'Dashboards', href: pages.dashboards.path, active: activePath === pages.dashboards.path, onClick: navigateInternally(pages.dashboards.path) },
      { id: 'entities', label: 'Entités', href: pages.entities.path, active: activePath === pages.entities.path, onClick: navigateInternally(pages.entities.path) },
    ],
  },
  {
    id: 'administration',
    title: 'Administration',
    items: [
      { id: 'logs', label: 'Journaux', href: pages.logs.path, active: activePath === pages.logs.path, onClick: navigateInternally(pages.logs.path) },
      { id: 'settings', label: 'Réglages', href: pages.settings.path, active: activePath === pages.settings.path, onClick: navigateInternally(pages.settings.path) },
    ],
  },
  ];
}

function SummaryCard({ detail, label, value }: { detail: string; label: string; value: string }) {
  return (
    <Card className="ha-summary-card">
      <div className="ha-summary-label">{label}</div>
      <div className="ha-summary-value">{value}</div>
      <div className="ha-summary-detail">{detail}</div>
    </Card>
  );
}

function ZoneCard({ active, devices, name, temperature }: { active: boolean; devices: number; name: string; temperature: number }) {
  return (
    <Card className={`ha-zone-card${active ? ' ha-zone-card-active' : ''}`}>
      <div className="ha-zone-card-header">
        <span className="ha-zone-glyph" aria-hidden="true">{active ? '◒' : '⌂'}</span>
        <Button aria-label={`Actions pour ${name}`} className="ha-quiet-icon-button" size="sm" variant="quiet">•••</Button>
      </div>
      <h3>{name}</h3>
      <p>{devices} appareils · {temperature}°</p>
      <div className="ha-zone-state">
        <ToggleSwitch aria-label={`Éclairage de ${name}`} checked={active} size="sm" />
        <span>{active ? 'Éclairage allumé' : 'Tout est éteint'}</span>
      </div>
    </Card>
  );
}

function HomeOverviewPage() {
  const [apiState, setApiState] = useState<'checking' | 'online' | 'offline'>('checking');
  const [serviceInfo, setServiceInfo] = useState('Connexion à l’API...');

  useEffect(() => {
    const controller = new AbortController();

    Promise.all([
      getHomeAutomationServiceInfo(controller.signal),
      checkHomeAutomationHealth(controller.signal),
    ])
      .then(([info]) => {
        setServiceInfo(info.service);
        setApiState('online');
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return;
        }

        setServiceInfo('API indisponible');
        setApiState('offline');
      });

    return () => controller.abort();
  }, []);

  const apiStatusLabel = apiState === 'checking' ? 'Vérification...' : apiState === 'online' ? 'En ligne' : 'Hors ligne';
  const apiStatusTone = apiState === 'online' ? 'success' : apiState === 'offline' ? 'danger' : 'warning';

  return (
    <div className="ha-page">
      <PageHeader
        actions={<Button size="lg">+ Ajouter un appareil</Button>}
        description="Voici ce qui se passe chez vous en ce moment."
        eyebrow="Samedi 13 septembre 2026 · 09:42"
        title="Bonjour Michael."
      />

      <section className="ha-summary-grid" aria-label="Synthèse de la maison">
        <SummaryCard detail="Ciel dégagé · Ressenti 20°" label="Température intérieure" value="21° C" />
        <SummaryCard detail="48% d'humidité · Qualité bonne" label="Confort intérieur" value="Excellent" />
        <SummaryCard detail={serviceInfo} label="État de l'API" value={apiStatusLabel} />
        <SummaryCard detail="6 exécutions aujourd'hui" label="Automatisations" value="24 appareils" />
      </section>

      <section className="ha-content-section">
        <div className="ha-section-heading">
          <div>
            <div className="ha-section-eyebrow">Votre maison</div>
            <h2>Zones principales</h2>
          </div>
          <Button size="sm" variant="secondary">Voir toutes les zones</Button>
        </div>
        <div className="ha-zone-grid">
          <ZoneCard active devices={7} name="Salon" temperature={21} />
          <ZoneCard active={false} devices={5} name="Cuisine" temperature={20} />
          <ZoneCard active={false} devices={4} name="Chambre" temperature={19} />
          <Button className="ha-add-zone" size="lg" variant="secondary"><span aria-hidden="true">+</span> Ajouter une zone</Button>
        </div>
      </section>

      <section className="ha-panels-grid">
        <Card className="ha-panel">
          <div className="ha-section-heading">
            <div>
              <div className="ha-section-eyebrow">Dernières actions</div>
              <h2>Activité récente</h2>
            </div>
            <StatusDot label={apiStatusLabel} tone={apiStatusTone} />
          </div>
          <div className="ha-activity-list">
            <div><strong>Scène « Réveil » exécutée</strong><span>Salon · il y a 18 min</span><time>09:24</time></div>
            <div><strong>Michael est arrivé</strong><span>Présence · il y a 42 min</span><time>09:00</time></div>
            <div><strong>Arrosage automatique terminé</strong><span>Jardin · hier</span><time>18:30</time></div>
          </div>
        </Card>

        <Card className="ha-panel">
          <div className="ha-section-heading">
            <div>
              <div className="ha-section-eyebrow">Accès rapide</div>
              <h2>Scènes</h2>
            </div>
            <Button size="sm" variant="secondary">Gérer</Button>
          </div>
          <div className="ha-scenes-grid">
            <Button variant="secondary"><strong>☼ Réveil</strong><span>Tout préparer</span></Button>
            <Button variant="secondary"><strong>☾ Bonne nuit</strong><span>Fermer la maison</span></Button>
            <Button variant="secondary"><strong>❋ Absence</strong><span>Économiser l'énergie</span></Button>
            <Button variant="secondary"><strong>+ Nouvelle scène</strong></Button>
          </div>
        </Card>
      </section>
    </div>
  );
}

function PlaceholderPage({ page }: { page: (typeof pages)[PageId] }) {
  return (
    <div className="ha-page ha-placeholder-page">
      <PageHeader description={page.description} eyebrow="En préparation" title={page.title} variant="page" />
      <Card className="ha-placeholder-card">
        <div className="ha-section-eyebrow">Bientôt disponible</div>
        <p>Cette page est prête pour accueillir ses premiers contrôles.</p>
      </Card>
    </div>
  );
}

function RoutedApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const navigationSections = createNavigationSections(location.pathname, navigate);

  return (
    <DefaultAdminShell
      className="ha-admin-shell"
      contentPadding="compact"
      logoutLabel="Se déconnecter"
      navigationAriaLabel="Navigation principale"
      navigationItems={[]}
      onLogout={() => undefined}
      serverLabel="Serveur courant"
      serverMeta="MaNoir 0.1.0"
      serverName="Espace principal"
      serverStatus="En ligne"
      serverStatusTone="success"
      sidebarBrand="MaNoir"
      sidebarEyebrow="Espace authentifié"
      sidebarNavigation={<SidebarNav aria-label="Navigation principale" className="front-auth-sidebar-nav" sections={navigationSections} />}
      topBarActions={<><Button aria-label="Rechercher" size="sm" variant="quiet">⌕</Button><Button aria-label="Notifications" size="sm" variant="quiet">♧</Button></>}
      topBarBrand="MaNoir"
      topBarLogo={<img alt="Logo MaNoir" src={manoirLogo} />}
      topBarMeta="dimanche 13 septembre 2026 · localhost"
      topBarNavigation={<ShellHeader aria-label="Contexte espace" className="front-domain-shell-header" compactBreakpoint={760} items={[{ id: 'space', label: 'Espace principal', active: true }]} />}
      userLabel="Administrateur"
      userValue="Michael"
    >
      <Routes>
        <Route element={<Navigate replace to={pages.overview.path} />} path="/" />
        <Route element={<HomeOverviewPage />} path={pages.overview.path} />
        <Route element={<PlaceholderPage page={pages.rooms} />} path={pages.rooms.path} />
        <Route element={<PlaceholderPage page={pages.devices} />} path={pages.devices.path} />
        <Route element={<PlaceholderPage page={pages.people} />} path={pages.people.path} />
        <Route element={<PlaceholderPage page={pages.scenes} />} path={pages.scenes.path} />
        <Route element={<PlaceholderPage page={pages.integrations} />} path={pages.integrations.path} />
        <Route element={<PlaceholderPage page={pages.automations} />} path={pages.automations.path} />
        <Route element={<PlaceholderPage page={pages.dashboards} />} path={pages.dashboards.path} />
        <Route element={<PlaceholderPage page={pages.entities} />} path={pages.entities.path} />
        <Route element={<PlaceholderPage page={pages.logs} />} path={pages.logs.path} />
        <Route element={<PlaceholderPage page={pages.settings} />} path={pages.settings.path} />
        <Route element={<Navigate replace to={pages.overview.path} />} path="*" />
      </Routes>
    </DefaultAdminShell>
  );
}

function App() {
  return <BrowserRouter><RoutedApp /></BrowserRouter>;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
