/**
 * The running API, for setting up and tidying test data quickly. The behaviour under test always
 * goes through the browser; this only prepares the claims a test needs.
 */
export const API_URL = process.env['E2E_API_URL'] ?? 'http://127.0.0.1:5080';

/** Seeded demo users, signed in as `${user}@demo.local`. */
export type DemoUser = 'claimant.my' | 'claimant.sg' | 'officer.my' | 'officer.sg' | 'manager.my';

export interface ApiResult<T = any> {
  status: number;
  body: T;
}

export interface NewClaim {
  id: string;
  claimNumber: string;
}

const tokens = new Map<DemoUser, Promise<string>>();

export function token(user: DemoUser): Promise<string> {
  if (!tokens.has(user)) {
    tokens.set(
      user,
      call('POST', '/api/auth/dev-login', undefined, { email: `${user}@demo.local` }).then((r) => {
        if (r.status !== 200) throw new Error(`dev-login for ${user} returned ${r.status}`);
        return r.body.accessToken as string;
      }),
    );
  }
  return tokens.get(user)!;
}

export async function call<T = any>(
  method: string,
  path: string,
  user?: DemoUser,
  body?: unknown,
): Promise<ApiResult<T>> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (user) headers['Authorization'] = `Bearer ${await token(user)}`;
  const response = await fetch(`${API_URL}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

async function expectStatus(result: Promise<ApiResult>, expected: number, what: string) {
  const r = await result;
  if (r.status !== expected)
    throw new Error(`${what}: expected ${expected}, got ${r.status} ${JSON.stringify(r.body)}`);
  return r;
}

/** A local calendar day as yyyy-mm-dd: 1 is yesterday, -1 tomorrow (not UTC, which can be a day off). */
export function isoDay(daysAgo: number): string {
  const d = new Date();
  d.setDate(d.getDate() - daysAgo);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export interface SubmitOptions {
  estimate?: number;
  description?: string;
  location?: string;
}

/** A motor claim on claimant.my's active policy. */
export async function submitClaim(options: SubmitOptions = {}): Promise<NewClaim> {
  const r = await expectStatus(
    call('POST', '/api/claims', 'claimant.my', {
      policyNumber: 'POL-MY-M-0001',
      type: 'Motor',
      incidentDate: isoDay(1),
      incidentLocation: options.location ?? 'Jalan Ampang, Kuala Lumpur',
      description: options.description ?? 'Browser test',
      estimatedLossAmount: options.estimate ?? 1500,
      motor: { vehicleRegistration: 'E2E 1', thirdPartyInvolved: false, policeReportNumber: null },
    }),
    201,
    'FNOL',
  );
  return { id: r.body.id, claimNumber: r.body.claimNumber };
}

export const pickUp = (id: string, user: DemoUser = 'officer.my') =>
  expectStatus(call('POST', `/api/claims/${id}/pick-up`, user), 204, 'pick up');

export const requestInformation = (id: string, question: string) =>
  expectStatus(
    call('POST', `/api/claims/${id}/information-requests`, 'officer.my', { question }),
    204,
    'request information',
  );

export const approve = (id: string, settlementAmount: number) =>
  expectStatus(
    call('POST', `/api/claims/${id}/approve`, 'officer.my', { settlementAmount }),
    204,
    'approve',
  );

export async function detail(id: string, user: DemoUser = 'officer.my') {
  return (await expectStatus(call('GET', `/api/claims/${id}`, user), 200, 'claim detail')).body;
}

/**
 * Closes a claim a test opened, so repeated runs don't fill the queues: rejects an open one,
 * settles an approved one. Best effort: a claim already closed is left as it is.
 */
export async function closeClaim(id: string): Promise<void> {
  let status: string = (await detail(id)).status;
  if (status === 'Submitted') {
    await call('POST', `/api/claims/${id}/pick-up`, 'officer.my');
    status = 'UnderReview';
  }
  if (status === 'AwaitingInformation') {
    const request = (await detail(id, 'claimant.my')).openInformationRequest;
    await call(
      'POST',
      `/api/claims/${id}/information-requests/${request.id}/response`,
      'claimant.my',
      {
        response: 'Closing the browser test claim.',
      },
    );
    status = 'UnderReview';
  }
  for (const user of ['officer.my', 'manager.my'] as const) {
    const path = status === 'Approved' ? `/api/claims/${id}/settle` : `/api/claims/${id}/reject`;
    if (status !== 'UnderReview' && status !== 'Approved') return;
    const r = await call('POST', path, user, { reason: 'Browser test clean-up' });
    if (r.status === 204) return; // otherwise it's assigned to the other staff member
  }
}

/**
 * Read models fed by Kafka are eventually consistent: events from the previous test (its clean-up,
 * say) may still be arriving. Returns the value once two reads a moment apart agree.
 */
export async function settled<T>(read: () => Promise<T>, gapMs = 1200): Promise<T> {
  let previous = JSON.stringify(await read());
  for (let i = 0; i < 20; i++) {
    await new Promise((r) => setTimeout(r, gapMs));
    const current = await read();
    if (JSON.stringify(current) === previous) return current;
    previous = JSON.stringify(current);
  }
  throw new Error('The read model kept changing.');
}
