// Phase 7: SignalR pushes, in-app notifications and team workload against http://localhost:5080.
// Uses the same SignalR client as the Angular app. Run: npm install && npm run realtime
import * as signalR from "@microsoft/signalr";

const BASE = "http://localhost:5080";
const results = [];
const check = (name, ok, detail = "") => {
  results.push(ok);
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail !== "" ? `  -> ${typeof detail === "string" ? detail : JSON.stringify(detail)}` : ""}`);
};

async function call(method, path, token, body) {
  const res = await fetch(BASE + path, {
    method,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  return [res.status, text ? JSON.parse(text) : null];
}
const login = async (email) => (await call("POST", "/api/auth/dev-login", null, { email }))[1].accessToken;

// One hub connection per user, recording every message with the time it arrived.
async function listen(name, token) {
  const received = [];
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${BASE}/hubs/claims`, { accessTokenFactory: () => token })
    .configureLogging(signalR.LogLevel.None)
    .build();
  connection.on("ClaimUpdated", (m) => received.push({ type: "ClaimUpdated", at: Date.now(), ...m }));
  connection.on("ExposureUpdated", (m) => received.push({ type: "ExposureUpdated", at: Date.now(), ...m }));
  await connection.start();
  return { name, connection, received };
}

// Waits until `predicate` matches a message received by `client`, or times out.
async function waitFor(client, predicate, timeoutMs = 8000) {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) {
    const hit = client.received.find(predicate);
    if (hit) return hit;
    await new Promise((r) => setTimeout(r, 50));
  }
  return null;
}

const yesterday = new Date(Date.now() - 86400000).toISOString().slice(0, 10);
const tokens = Object.fromEntries(await Promise.all(
  ["claimant.my", "claimant.sg", "officer.my", "officer.sg", "manager.my"].map(async (u) => [u, await login(`${u}@demo.local`)])));

// ---------------------------------------------------------------- connection auth
try {
  const anon = new signalR.HubConnectionBuilder().withUrl(`${BASE}/hubs/claims`).configureLogging(signalR.LogLevel.None).build();
  await anon.start();
  check("hub without token is refused", false, "connected");
} catch (e) {
  check("hub without token is refused", /401/.test(String(e)), String(e).slice(0, 80));
}

const clients = Object.fromEntries(await Promise.all(Object.entries(tokens).map(async ([n, t]) => [n, await listen(n, t)])));
check("all five users connected", Object.values(clients).every((c) => c.connection.state === "Connected"));

// ---------------------------------------------------------------- FNOL -> pushes
const t0 = Date.now();
const [s, created] = await call("POST", "/api/claims", tokens["claimant.my"], {
  policyNumber: "POL-MY-M-0001", type: "Motor", incidentDate: yesterday, incidentLocation: "Jalan Ampang",
  description: "Realtime smoke test", estimatedLossAmount: 2500, motor: { vehicleRegistration: "RT 1", thirdPartyInvolved: false },
});
check("FNOL accepted", s === 201, created?.claimNumber);
const id = created.id;
const isThis = (status) => (m) => m.type === "ClaimUpdated" && m.claimId === id && (status === undefined || m.status === status);

const own = await waitFor(clients["claimant.my"], isThis("Submitted"));
check("claimant gets ClaimUpdated(Submitted)", !!own, own ? `${own.at - t0} ms after the 201 request started` : "none");
check("MY officer gets it (staff:MY)", !!(await waitFor(clients["officer.my"], isThis("Submitted"))));
check("MY manager gets it", !!(await waitFor(clients["manager.my"], isThis("Submitted"))));
const exp = await waitFor(clients["manager.my"], (m) => m.type === "ExposureUpdated" && m.market === "MY" && m.claimType === "Motor");
check("MY manager gets ExposureUpdated (MY/MYR/Motor)", !!exp, exp && { market: exp.market, currency: exp.currency });

// ---------------------------------------------------------------- officer actions -> claimant
await call("POST", `/api/claims/${id}/pick-up`, tokens["officer.my"]);
check("claimant gets UnderReview after pick-up", !!(await waitFor(clients["claimant.my"], isThis("UnderReview"))));
await call("PUT", `/api/claims/${id}/reserve`, tokens["officer.my"], { amount: 3000, reason: "Quote" });
const reservePush = await waitFor(clients["officer.my"], (m) => isThis()(m) && m.eventType === "ReserveAdjusted");
check("reserve change pushed to staff with status null (unchanged)", !!reservePush && reservePush.status === null);
await call("POST", `/api/claims/${id}/information-requests`, tokens["officer.my"], { question: "Photos of the damage, please." });
check("claimant gets AwaitingInformation", !!(await waitFor(clients["claimant.my"], isThis("AwaitingInformation"))));

// ---------------------------------------------------------------- isolation
await new Promise((r) => setTimeout(r, 1000));
check("other claimant (SG) received nothing about this claim", !clients["claimant.sg"].received.some(isThis()));
check("SG officer received nothing about this MY claim", !clients["officer.sg"].received.some(isThis()));
check("officer receives no ExposureUpdated (managers only)", !clients["officer.my"].received.some((m) => m.type === "ExposureUpdated"));

// ---------------------------------------------------------------- notifications
let [, mine] = await call("GET", "/api/notifications", tokens["claimant.my"]);
const forClaim = mine.items.filter((n) => n.claimId === id);
check("claimant has 3 notifications for the claim (received, reviewing, info needed)", forClaim.length === 3, forClaim.map((n) => n.message));
check("no claimant notification mentions the reserve", !forClaim.some((n) => /3,000|3000|reserve/i.test(n.message)));

const detail = (await call("GET", `/api/claims/${id}`, tokens["claimant.my"]))[1];
await call("POST", `/api/claims/${id}/information-requests/${detail.openInformationRequest.id}/response`, tokens["claimant.my"], { response: "Uploaded." });
await waitFor(clients["officer.my"], (m) => isThis()(m) && m.eventType === "InformationProvided");
await new Promise((r) => setTimeout(r, 300));
const officerNotes = (await call("GET", "/api/notifications", tokens["officer.my"]))[1].items.filter((n) => n.claimId === id);
check("assigned officer notified that the claimant answered", officerNotes.length === 1, officerNotes[0]?.message);

const target = forClaim[0];
let [code] = await call("POST", `/api/notifications/${target.id}/read`, tokens["claimant.sg"]);
check("marking someone else's notification -> 404", code === 404, code);
[code] = await call("POST", `/api/notifications/${target.id}/read`, tokens["claimant.my"]);
const afterOne = (await call("GET", "/api/notifications", tokens["claimant.my"]))[1];
check("mark one read -> 204, unread count drops by 1", code === 204 && afterOne.unreadCount === mine.unreadCount - 1, `${mine.unreadCount} -> ${afterOne.unreadCount}`);
[code] = await call("POST", "/api/notifications/read-all", tokens["claimant.my"]);
check("read-all -> unread 0", code === 204 && (await call("GET", "/api/notifications", tokens["claimant.my"]))[1].unreadCount === 0);

// ---------------------------------------------------------------- team workload
const [ws, team] = await call("GET", "/api/team/workload", tokens["officer.my"]);
const me = team?.officers.find((o) => o.name === "Hafiz Ismail");
const [, queue] = await call("GET", "/api/work-queue?view=mine", tokens["officer.my"]);
check("team workload: MY staff only", ws === 200 && team.market === "MY" && team.officers.length === 2, team?.officers.map((o) => `${o.name} ${o.role}`));
check("officer's open count matches their work queue", me && me.openClaims === queue.length, `${me?.openClaims} vs ${queue.length}`);
check("open by status adds up", me && me.underReview + me.awaitingClaimant + me.approvedAwaitingPayment === me.openClaims, me);
check("claimant cannot read team workload -> 403", (await call("GET", "/api/team/workload", tokens["claimant.my"]))[0] === 403);

await Promise.all(Object.values(clients).map((c) => c.connection.stop()));
console.log(`\n${results.filter(Boolean).length}/${results.length} checks passed`);
process.exit(results.every(Boolean) ? 0 : 1);
