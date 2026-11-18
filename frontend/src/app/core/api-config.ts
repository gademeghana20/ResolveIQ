// Settings that can differ between "on my computer" and "on the internet" are read from public/config.js, which is
// loaded before the app starts. That file is plain text that anyone can read, so it must NEVER hold a real secret:
// only the address of the API, and (on a public demo site) the shared password of the demo accounts.
declare global {
  interface Window {
    resolveiqConfig?: {
      /** The API's web address, for example "https://my-api.example.com" (without /api). Empty means "this computer". */
      apiBaseUrl?: string;
      /** The shared password of the public demo accounts. Empty hides the demo sign-in buttons. */
      demoPassword?: string;
    };
  }
}

const LOCAL_API = 'http://localhost:5293';

const configuredBase = (typeof window === 'undefined' ? undefined : window.resolveiqConfig?.apiBaseUrl)?.trim();

// Where the ResolveIQ API lives. With no setting, it is the "http" profile of the backend (dotnet run).
export const API_URL = `${(configuredBase || LOCAL_API).replace(/\/+$/, '')}/api`;

/** The shared demo password, or an empty text when this is not a public demo site. */
export function demoPassword(): string {
  return (typeof window === 'undefined' ? '' : window.resolveiqConfig?.demoPassword)?.trim() ?? '';
}
