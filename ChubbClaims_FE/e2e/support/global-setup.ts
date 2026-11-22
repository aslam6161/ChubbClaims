import { API_URL } from './api';

/** Fails fast, with what to do, when the API isn't running (the web app is started by the config). */
export default async function globalSetup() {
  try {
    await fetch(`${API_URL}/health`, { signal: AbortSignal.timeout(3000) });
  } catch {
    throw new Error(
      `The API isn't running at ${API_URL}. Start the stack first: \`docker compose up -d\` ` +
        `from the repository root (or set E2E_API_URL).`,
    );
  }
}
