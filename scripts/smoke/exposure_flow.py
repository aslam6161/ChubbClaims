"""Phase 6: exposure is updated from Kafka events shortly after a claim change (eventual consistency).

Submits an FNOL as claimant.my and polls GET /api/reports/exposure as manager.my until the MY/Motor
row reflects it. Prints the observed lag. Usage: python exposure_flow.py [snapshot-file]
With a snapshot file argument it only writes the current exposure rows to that file (for replay tests).
"""
import json
import sys
import time
import urllib.error
import urllib.request
from datetime import date, timedelta

BASE = "http://localhost:5080"


def call(method, path, token=None, body=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read()
        return e.code, (json.loads(raw) if raw else None)


def login(email):
    return call("POST", "/api/auth/dev-login", body={"email": email})[1]["accessToken"]


manager = login("manager.my@demo.local")


def exposure_rows():
    s, b = call("GET", "/api/reports/exposure", manager)
    assert s == 200, (s, b)
    return {(r["market"], r["currency"], r["claimType"]): (r["openClaims"], r["outstandingReserve"]) for r in b["rows"]}, b


if len(sys.argv) > 1:
    rows, _ = exposure_rows()
    json.dump({"|".join(k): v for k, v in sorted(rows.items())}, open(sys.argv[1], "w"), indent=1)
    print(f"snapshot of {len(rows)} rows written to {sys.argv[1]}")
    sys.exit(0)

results = []


def check(name, ok, detail=""):
    results.append(ok)
    print(f"{'PASS' if ok else 'FAIL'}  {name}" + (f"  -> {detail}" if detail != "" else ""))


s, _ = call("GET", "/api/reports/exposure", login("officer.my@demo.local"))
check("officer cannot read exposure -> 403", s == 403, s)

def settled_rows():
    """Exposure is eventually consistent: events from earlier requests (e.g. another smoke script that
    just ran) may still be in flight. Take the baseline only once two reads 1.5 s apart agree."""
    previous, _ = exposure_rows()
    while True:
        time.sleep(1.5)
        current, report = exposure_rows()
        if current == previous:
            return current, report
        previous = current


before, report = settled_rows()
check("totals are per currency (6 currencies, no cross-currency sum)",
      sorted(t["currency"] for t in report["totalsByCurrency"]) == ["AUD", "HKD", "IDR", "MYR", "SGD", "THB"],
      [t["currency"] for t in report["totalsByCurrency"]])

claimant = login("claimant.my@demo.local")
key = ("MY", "MYR", "Motor")
open_before, out_before = before[key]
t0 = time.perf_counter()
s, b = call("POST", "/api/claims", claimant, {
    "policyNumber": "POL-MY-M-0001", "type": "Motor", "incidentDate": str(date.today() - timedelta(days=1)),
    "incidentLocation": "Jalan Ampang", "description": "Exposure smoke test", "estimatedLossAmount": 4321.5,
    "motor": {"vehicleRegistration": "EXP 1", "thirdPartyInvolved": False},
})
check("FNOL accepted (201) immediately, before exposure changes", s == 201, b and b["claimNumber"])

lag = None
for _ in range(100):
    rows, _ = exposure_rows()
    if rows[key] == (open_before + 1, out_before + 4321.5):
        lag = time.perf_counter() - t0
        break
    time.sleep(0.1)
check("exposure MY/MYR/Motor updated via Kafka (+1 open, +4321.50)", lag is not None,
      f"visible after {lag:.2f}s" if lag else f"still {rows[key]}")

print(f"\n{sum(results)}/{len(results)} checks passed")
