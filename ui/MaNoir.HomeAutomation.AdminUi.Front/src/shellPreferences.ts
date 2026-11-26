import { useEffect, useState } from 'react';

const themeKey = 'manoir.admin.theme';
const sidebarKey = 'manoir.admin.sidebarCollapsed';

export type Theme = 'light' | 'dark';

function readTheme(): Theme {
  try {
    const stored = window.localStorage.getItem(themeKey);
    if (stored === 'light' || stored === 'dark') {
      return stored;
    }
  } catch {
    // Storage can be unavailable (private window, blocked site data).
  }

  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function readSidebarCollapsed() {
  try {
    return window.localStorage.getItem(sidebarKey) !== '0';
  } catch {
    return true;
  }
}

/** Theme and sidebar state shared with the other MaNoir admin apps (same origin, same keys). */
export function useShellPreferences() {
  const [theme, setTheme] = useState<Theme>(readTheme);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(readSidebarCollapsed);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      window.localStorage.setItem(themeKey, theme);
    } catch {
      // Ignore storage failures.
    }
  }, [theme]);

  useEffect(() => {
    try {
      window.localStorage.setItem(sidebarKey, sidebarCollapsed ? '1' : '0');
    } catch {
      // Ignore storage failures.
    }
  }, [sidebarCollapsed]);

  return { theme, setTheme, sidebarCollapsed, setSidebarCollapsed };
}
