declare global {
  interface Window {
    __MANOIR_ADMIN_UI_CONFIG__?: {
      routerBasePath?: string;
      publicBasePath?: string;
    };
  }
}

export function getRouterBasePath(): string {
  const runtimeBasePath = window.__MANOIR_ADMIN_UI_CONFIG__?.routerBasePath?.trim();
  if (runtimeBasePath) {
    return runtimeBasePath;
  }

  return import.meta.env.BASE_URL;
}

export function getPublicBasePath(): string {
  const runtimeBasePath = window.__MANOIR_ADMIN_UI_CONFIG__?.publicBasePath?.trim();
  if (!runtimeBasePath || runtimeBasePath === '/') {
    return '';
  }

  return `/${runtimeBasePath.replace(/^\/+|\/+$/g, '')}`;
}

export {};