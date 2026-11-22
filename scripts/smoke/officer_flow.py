"""Drives the Phase 5 officer flow against http://localhost:5080 (fresh seed) and prints one line per check.

Includes a concurrency test: two staff members pick up the same claim at the same instant;
exactly one must win and the other must get 409.
"""
import json
import threading
import urllib.error
import urllib.request
from collections import Counter
from datetime import date, timedelta

BASE = "http://localhost:5080"
results = []


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


def check(name, ok, detail=""):
    results.append(ok)
    print(f"{'PASS' if ok else 'FAIL'}  {name}" + (f"  -> {detail}" if detail != "" else ""))


def login(email):
    return call("POST", "/api/auth/dev-login", body={"email": email})[1]["accessToken"]


claimant = login("claimant.my@demo.local")
officer = login("officer.my@demo.local")
manager = login("manager.my@demo.local")
sg_officer = login("officer.sg@demo.local")
officer_id = call("GET", "/api/me", officer)[1]["id"]


def new_fnol():
    body = {
        "policyNumber": "POL-MY-M-0001", "type": "Motor", "incidentDate": str(date.today() - timedelta(days=1)),
        "incidentLocation": "Jalan Ampang", "description": "Smoke test", "estimatedLossAmount": 10000,
        "motor": {"vehicleRegistration": "WXY 1", "thirdPartyInvolved": False},
    }
    s, b = call("POST", "/api/claims", claimant, body)
    assert s == 201, (s, b)
    return b["id"]


# ---------------------------------------------------------------- work queue
s, page = call("GET", "/api/work-queue?view=unassigned", officer)
b = page["items"] if page else []
check("MY unassigned queue: only MY, only unassigned", s == 200 and b and all(r["market"] == "MY" and r["assignedOfficerId"] is None for r in b),
      [r["claimNumber"] for r in b or []])
check("queue sorted by SLA, overdue flagged first", b[0]["isOverdue"] and [r["slaDueAt"] for r in b] == sorted(r["slaDueAt"] for r in b),
      [(r["claimNumber"], r["isOverdue"]) for r in b])
check("queue rows carry age, waiting-on and outstanding reserve",
      all(isinstance(r["ageDays"], int) and r["waitingOn"] == "Officer" and r["outstandingReserve"] > 0 and r["currency"] == "MYR" for r in b))

s, page = call("GET", "/api/work-queue?view=mine", officer)
b = page["items"] if page else []
statuses = sorted(r["status"] for r in b)
check("MY officer 'mine': open claims only (no Settled)", s == 200 and "Settled" not in statuses and len(b) == 3, statuses)
awaiting = [r for r in b if r["status"] == "AwaitingInformation"]
check("awaiting-information row waits on the claimant", awaiting and awaiting[0]["waitingOn"] == "Claimant")

s, page = call("GET", "/api/work-queue?view=mine&status=Settled", officer)
b = page["items"] if page else []
check("status filter can show closed claims", s == 200 and len(b) == 1 and b[0]["outstandingReserve"] == 0, b and b[0]["claimNumber"])

s, page = call("GET", "/api/work-queue?view=unassigned", sg_officer)
b = page["items"] if page else []
check("SG officer sees only SG", s == 200 and all(r["market"] == "SG" for r in b), [r["claimNumber"] for r in b])

s, _ = call("GET", "/api/work-queue", claimant)
check("claimant cannot read the work queue -> 403", s == 403, s)
s, b = call("GET", "/api/work-queue?view=everything", officer)
check("unknown view -> 400", s == 400, s)

# Paging and search (server-side: only one page is ever sent).
s, everything = call("GET", "/api/work-queue?view=mine&pageSize=100", officer)
s, p1 = call("GET", "/api/work-queue?view=mine&pageSize=2&page=1", officer)
s, p2 = call("GET", "/api/work-queue?view=mine&pageSize=2&page=2", officer)
numbers = lambda page: [r["claimNumber"] for r in page["items"]]
check("pages chain in order with no overlap; total counts the whole queue",
      numbers(p1) + numbers(p2) == numbers(everything)[:4] and p1["totalCount"] == len(everything["items"]),
      numbers(p1) + numbers(p2))
s, unassigned = call("GET", "/api/work-queue?view=unassigned", officer)
first = unassigned["items"][0]["claimNumber"]
s, found = call("GET", f"/api/work-queue?view=unassigned&search={first[-6:]}", officer)
check("search finds a claim by number; overdue count still covers the whole queue",
      numbers(found) == [first] and found["overdueCount"] == unassigned["overdueCount"] == 1,
      (numbers(found), found["overdueCount"]))
for q in ["pageSize=101", "page=0", "page=2147483647"]:
    s, _ = call("GET", f"/api/work-queue?{q}", officer)
    check(f"{q} -> 400 (never a 500)", s == 400, s)

# ---------------------------------------------------------------- full lifecycle
cid = new_fnol()
s, b = call("POST", f"/api/claims/{cid}/approve", officer, {"settlementAmount": 9000})
check("approve before pick-up -> 409", s == 409, b and b.get("detail"))
s, _ = call("POST", f"/api/claims/{cid}/pick-up", sg_officer)
check("SG officer picking up MY claim -> 404", s == 404, s)
s, _ = call("POST", f"/api/claims/{cid}/pick-up", claimant)
check("claimant picking up -> 403", s == 403, s)
s, _ = call("POST", f"/api/claims/{cid}/pick-up", officer)
check("officer picks up -> 204", s == 204, s)
s, page = call("GET", "/api/work-queue?view=mine", officer)
b = page["items"]
check("claim now in officer's queue", any(r["id"] == cid for r in b))

s, b = call("PUT", f"/api/claims/{cid}/reserve", manager, {"amount": 12000, "reason": "Quote"})
check("manager (not assigned) adjusting reserve -> 403", s == 403, b and b.get("detail"))
s, b = call("PUT", f"/api/claims/{cid}/reserve", officer, {"amount": -5, "reason": "Typo"})
check("negative reserve -> 422", s == 422, b and b.get("detail"))
s, b = call("PUT", f"/api/claims/{cid}/reserve", officer, {"reason": "No amount"})
check("missing amount -> 400", s == 400, b and list(b.get("errors", {}).keys()))
s, _ = call("PUT", f"/api/claims/{cid}/reserve", officer, {"amount": 12500, "reason": "Workshop quote"})
check("officer adjusts reserve -> 204", s == 204, s)

s, _ = call("POST", f"/api/claims/{cid}/information-requests", officer, {"question": "Please upload photos."})
check("request information -> 204", s == 204, s)
s, b = call("POST", f"/api/claims/{cid}/information-requests", officer, {"question": "Another?"})
check("second open request -> 409", s == 409, b and b.get("detail"))
detail = call("GET", f"/api/claims/{cid}", claimant)[1]
s, _ = call("POST", f"/api/claims/{cid}/information-requests/{detail['openInformationRequest']['id']}/response", claimant, {"response": "Uploaded."})
check("claimant answers -> 204", s == 204, s)

s, _ = call("POST", f"/api/claims/{cid}/notes", manager, {"text": "Checked with workshop."})
check("manager adds a note -> 204", s == 204, s)
s, _ = call("POST", f"/api/claims/{cid}/settle", officer)
check("settle before approval -> 409", s == 409, s)
s, b = call("POST", f"/api/claims/{cid}/approve", officer, {"settlementAmount": 0})
check("zero settlement -> 422", s == 422, b and b.get("detail"))
s, _ = call("POST", f"/api/claims/{cid}/approve", officer, {"settlementAmount": 11800, "note": "Agreed"})
check("approve -> 204", s == 204, s)
s, _ = call("POST", f"/api/claims/{cid}/settle", officer)
check("settle -> 204", s == 204, s)

staff_view = call("GET", f"/api/claims/{cid}", officer)[1]
check("settled: paid, outstanding 0, full history",
      staff_view["status"] == "Settled" and staff_view["paidAmount"] == 11800 and staff_view["outstandingReserve"] == 0
      and [h["to"] for h in staff_view["statusHistory"]] == ["Submitted", "UnderReview", "AwaitingInformation", "UnderReview", "Approved", "Settled"]
      and len(staff_view["notes"]) == 2,
      [h["to"] for h in staff_view["statusHistory"]])
claimant_view = call("GET", f"/api/claims/{cid}", claimant)[1]
check("claimant sees the decision, not internal notes", claimant_view["settlementAmount"] == 11800 and claimant_view["notes"] is None)

rid = new_fnol()
call("POST", f"/api/claims/{rid}/pick-up", officer)
s, b = call("POST", f"/api/claims/{rid}/reject", officer, {"reason": ""})
check("reject without reason -> 400", s == 400, s)
s, _ = call("POST", f"/api/claims/{rid}/reject", officer, {"reason": "Excluded: racing"})
check("reject -> 204, outstanding released", s == 204 and call("GET", f"/api/claims/{rid}", officer)[1]["outstandingReserve"] == 0, s)

# ---------------------------------------------------------------- concurrent pick-up
outcomes = Counter()
winners_ok = True
for _ in range(10):
    claim_id = new_fnol()
    barrier = threading.Barrier(2)
    responses = {}

    def pick(name, token):
        barrier.wait()
        responses[name] = call("POST", f"/api/claims/{claim_id}/pick-up", token)

    threads = [threading.Thread(target=pick, args=(n, t)) for n, t in (("officer", officer), ("manager", manager))]
    for t in threads: t.start()
    for t in threads: t.join()

    codes = sorted(r[0] for r in responses.values())
    winners_ok &= codes == [204, 409]
    loser = next(r for r in responses.values() if r[0] == 409)
    outcomes["RowVersion conflict" if "changed by someone else" in (loser[1] or {}).get("detail", "") else "status already UnderReview"] += 1

check("10 simultaneous pick-ups: always exactly one 204 and one 409", winners_ok, dict(outcomes))

print(f"\n{sum(results)}/{len(results)} checks passed")
