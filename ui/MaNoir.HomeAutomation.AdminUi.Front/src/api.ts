export interface HomeAutomationServiceInfo {
  service: string;
  pluginId: string;
}

const apiBaseUrl = (
  import.meta.env.VITE_HOME_AUTOMATION_API_URL
  ?? (import.meta.env.DEV ? '/api' : '')
).replace(/\/$/, '');

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(`API ${response.status}`);
  }

  return response.json() as Promise<T>;
}

export async function getHomeAutomationServiceInfo(signal?: AbortSignal): Promise<HomeAutomationServiceInfo> {
  return getJson<HomeAutomationServiceInfo>('/', signal);
}

export async function checkHomeAutomationHealth(signal?: AbortSignal): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/health`, { signal });

  if (!response.ok) {
    throw new Error(`API health ${response.status}`);
  }
}