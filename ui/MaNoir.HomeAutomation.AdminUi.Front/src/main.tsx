import { StrictMode, useEffect, useState } from 'react';
import type * as React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import {
  Button,
  Card,
  DefaultAdminShell,
  InlineTabs,
  PageHeader,
  SidebarNav,
  StatusPill,
} from '@manoir-app/core-admin-ui-kit';
import { checkHomeAutomationHealth, getHomeAutomationServiceInfo } from './api';
import { getRouterBasePath } from './runtimeConfig';
import { Icon, MoonIcon, SunIcon, type IconKind } from './icons';
import { useShellPreferences } from './shellPreferences';
import '@manoir-app/core-admin-ui-kit/styles.css';
import './app.css';

type PageId = 'overview' | 'rooms' | 'devices' | 'people' | 'scenes' | 'integrations' | 'automations' | 'dashboards' | 'entities' | 'logs' | 'settings';
type SectionId = 'space' | 'configuration' | 'administration';

const pages: Record<PageId, { path: string; title: string; description: string }> = {
  overview: { path: '/space/overview', title: "Vue d'ensemble", description: "L'état du service Maison." },
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

// One rail entry per section; the pages of the active section become pill tabs above the content.
const sections: { id: SectionId; title: string; icon: IconKind; align?: 'end'; pages: PageId[] }[] = [
  { id: 'space', title: 'Espace', icon: 'grid', pages: ['overview', 'rooms', 'devices', 'people', 'scenes'] },
  { id: 'configuration', title: 'Configuration', icon: 'sliders', pages: ['integrations', 'automations', 'dashboards', 'entities'] },
  { id: 'administration', title: 'Administration', icon: 'server', align: 'end', pages: ['logs', 'settings'] },
];

function HomeOverviewPage() {
  const [apiState, setApiState] = useState<'checking' | 'online' | 'offline'>('checking');
  const [serviceInfo, setServiceInfo] = useState<string | null>(null);

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

        setApiState('offline');
      });

    return () => controller.abort();
  }, []);

  const apiStatusLabel = apiState === 'checking' ? 'Vérification…' : apiState === 'online' ? 'En ligne' : 'Hors ligne';
  const apiStatusTone = apiState === 'online' ? 'success' : apiState === 'offline' ? 'danger' : 'neutral';

  return (
    <div className="ha-page">
      <PageHeader description={pages.overview.description} title="Maison" variant="page" />
      <Card className="ha-card">
        <h2 className="ha-card-title">Service</h2>
        <dl className="ha-facts">
          <div className="ha-fact">
            <dt>API</dt>
            <dd><StatusPill tone={apiStatusTone}>{apiStatusLabel}</StatusPill></dd>
          </div>
          <div className="ha-fact">
            <dt>Service</dt>
            <dd>{serviceInfo ?? '—'}</dd>
          </div>
        </dl>
      </Card>
    </div>
  );
}

function PlaceholderPage({ page }: { page: (typeof pages)[PageId] }) {
  return (
    <div className="ha-page">
      <PageHeader description={page.description} title={page.title} variant="page" />
    </div>
  );
}

function RoutedApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const { theme, setTheme, sidebarCollapsed, setSidebarCollapsed } = useShellPreferences();

  const navigateInternally = (path: string) => (event: React.MouseEvent<HTMLElement>) => {
    event.preventDefault();
    navigate(path);
  };

  const activeSection = sections.find((section) => section.pages.some((id) => pages[id].path === location.pathname));
  const entries = sections.map((section) => {
    const firstPath = pages[section.pages[0]].path;
    return {
      id: section.id,
      label: section.title,
      icon: <Icon kind={section.icon} />,
      active: section.id === activeSection?.id,
      href: firstPath,
      onClick: navigateInternally(firstPath),
      align: section.align,
    };
  });
  const sidebarSections = [
    { id: 'sections', items: entries.filter((entry) => entry.align !== 'end') },
    { id: 'sections-end', align: 'end' as const, items: entries.filter((entry) => entry.align === 'end') },
  ].filter((section) => section.items.length > 0);

  return (
    <DefaultAdminShell
      className="ha-admin-shell"
      contentPadding="compact"
      logoutLabel="Se déconnecter"
      navigationAriaLabel="Navigation principale"
      navigationItems={[]}
      onLogout={() => undefined}
      onSidebarCollapsedChange={setSidebarCollapsed}
      serverLabel="Serveur courant"
      serverMeta="MaNoir 0.1.0"
      serverName="Espace principal"
      serverStatus="En ligne"
      serverStatusTone="success"
      sidebarBrand="MaNoir"
      sidebarCollapsed={sidebarCollapsed}
      sidebarEyebrow="Espace authentifié"
      sidebarFooterActions={(
        <button
          aria-label="Changer de thème"
          className="ha-icon-action"
          onClick={() => setTheme((current) => (current === 'dark' ? 'light' : 'dark'))}
          title="Changer de thème"
          type="button"
        >
          {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
        </button>
      )}
      sidebarNavigation={<SidebarNav aria-label="Navigation principale" className="ha-sidebar-nav" collapsed={sidebarCollapsed} sections={sidebarSections} />}
      sidebarToggleLabel="Réduire ou agrandir le menu"
      topBarBrand="MaNoir"
      topBarLogo="M"
      userLabel="Administrateur"
      userValue="Michael"
    >
      {activeSection && activeSection.pages.length > 1 ? (
        <div className="ha-section-tabs">
          <InlineTabs
            aria-label={activeSection.title}
            items={activeSection.pages.map((id) => ({
              id,
              label: pages[id].title,
              href: pages[id].path,
              onSelect: () => navigate(pages[id].path),
            }))}
            onValueChange={() => {}}
            value={activeSection.pages.find((id) => pages[id].path === location.pathname) ?? ''}
            variant="pill"
          />
        </div>
      ) : null}
      <Routes>
        <Route element={<Navigate replace to={pages.overview.path} />} path="/" />
        <Route element={<HomeOverviewPage />} path={pages.overview.path} />
        {(Object.keys(pages) as PageId[]).filter((id) => id !== 'overview').map((id) => (
          <Route element={<PlaceholderPage page={pages[id]} />} key={id} path={pages[id].path} />
        ))}
        <Route element={<Navigate replace to={pages.overview.path} />} path="*" />
      </Routes>
    </DefaultAdminShell>
  );
}

function App() {
  return <BrowserRouter basename={getRouterBasePath()}><RoutedApp /></BrowserRouter>;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
