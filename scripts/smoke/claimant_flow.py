"""Drives the Phase 4 claimant flow against http://localhost:5080 and prints one line per check."""
import json
import urllib.request
import urllib.error
from datetime import date, timedelta

BASE = "http://localhost:5080"
results = []


def call(method, path, token=None, body=None, headers=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else None), dict(r.headers)
    except urllib.error.HTTPError as e:
        raw = e.read()
        return e.code, (json.loads(raw) if raw else None), dict(e.headers)


def check(name, ok, detail=""):
    results.append(ok)
    print(f"{'PASS' if ok else 'FAIL'}  {name}" + (f"  -> {detail}" if detail else ""))


def login(email):
    s, b, _ = call("POST", "/api/auth/dev-login", body={"email": email})
    assert s == 200, (s, b)
    return b["accessToken"]


s, b, _ = call("GET", "/api/claims/mine")
check("no token -> 401 ProblemDetails", s == 401 and b and b.get("status") == 401, s)

s, b, _ = call("GET", "/api/auth/dev-users")
check("dev-users lists seeded users", s == 200 and len(b) == 9, f"{len(b or [])} users")

s, b, _ = call("POST", "/api/auth/dev-login", body={"email": "nobody@demo.local"})
check("dev-login unknown email -> 404", s == 404, s)

my = login("claimant.my@demo.local")
s, b, _ = call("GET", "/api/me", my)
check("GET /api/me", s == 200 and b["role"] == "Claimant" and b["market"] is None, b and b["email"])

s, b, _ = call("GET", "/api/policies/mine", my)
check("my policies (motor, property, expired)", s == 200 and len(b) == 3, [p["policyNumber"] for p in b])

today = date.today()
fnol = {
    "policyNumber": "POL-MY-M-0001", "type": "Motor", "incidentDate": str(today - timedelta(days=1)),
    "incidentLocation": "Jalan Ampang, KL", "description": "Rear-ended at a traffic light",
    "estimatedLossAmount": 7300.50,
    "motor": {"vehicleRegistration": "WXY 1234", "thirdPartyInvolved": True, "policeReportNumber": "PR-1"},
}
s, b, h = call("POST", "/api/claims", my, fnol, {"X-Correlation-Id": "phase4-fnol-1"})
check("FNOL -> 201 with claim number and Location", s == 201 and b["claimNumber"].startswith("CLM-MY-") and "Location" in h, b)
new_id = b["id"] if s == 201 else None

s, b, _ = call("POST", "/api/claims", my, {**fnol, "policyNumber": "POL-MY-M-0000"})
check("FNOL on expired policy -> 422", s == 422, b and b.get("detail"))

s, b, _ = call("POST", "/api/claims", my, {**fnol, "policyNumber": "POL-SG-M-0001"})
check("FNOL on someone else's policy -> 404", s == 404, b and b.get("detail"))

s, b, _ = call("POST", "/api/claims", my, {**fnol, "incidentDate": str(today + timedelta(days=2))})
check("FNOL future incident -> 422", s == 422, b and b.get("detail"))

s, b, _ = call("POST", "/api/claims", my, {**fnol, "motor": None})
check("FNOL motor without vehicle details -> 422", s == 422, b and b.get("detail"))

s, b, _ = call("POST", "/api/claims", my, {"policyNumber": "POL-MY-M-0001"})
check("FNOL missing fields -> 400 with field errors", s == 400 and "errors" in b, sorted((b or {}).get("errors", {}).keys()))

s, page, _ = call("GET", "/api/claims/mine", my)
b = page["items"]
check("my claims include the new one", s == 200 and any(c["id"] == new_id for c in b), f"{len(b)} claims")
awaiting = next(c for c in b if c["status"] == "AwaitingInformation")
check("banner fields: how many wait on the claimant, and which to answer first",
      page["awaitingYouCount"] == 1 and page["oldestAwaitingYouClaimId"] == awaiting["id"], page["awaitingYouCount"])
s, kitchen, _ = call("GET", "/api/claims/mine?search=kitchen", my)
check("search matches the description (case-insensitive)", [c["claimNumber"] for c in kitchen["items"]] == [awaiting["claimNumber"]],
      [c["claimNumber"] for c in kitchen["items"]])
s, theirs, _ = call("GET", "/api/claims/mine?search=CLM-MY", login("claimant.sg@demo.local"))
check("search never reaches another claimant's claims", theirs["totalCount"] == 0, theirs["totalCount"])

s, b, _ = call("GET", f"/api/claims/{new_id}", my)
check("claimant detail hides reserve/notes", s == 200 and b["reserveAmount"] is None and b["notes"] is None, b and b["status"])

sg = login("claimant.sg@demo.local")
s, b, _ = call("GET", f"/api/claims/{new_id}", sg)
check("other claimant reading my claim -> 404 (not 403)", s == 404, s)

s, b, _ = call("GET", f"/api/claims/{awaiting['id']}", my)
req_id = b["openInformationRequest"]["id"]
s, b, _ = call("POST", f"/api/claims/{awaiting['id']}/information-requests/{req_id}/response", sg, {"response": "x"})
check("other claimant answering -> 404", s == 404, s)
s, b, _ = call("POST", f"/api/claims/{awaiting['id']}/information-requests/{req_id}/response", my, {"response": "Report and photos uploaded."})
check("owner answers info request -> 204", s == 204, s)
s, b, _ = call("GET", f"/api/claims/{awaiting['id']}", my)
check("claim back to UnderReview, request answered", b["status"] == "UnderReview" and b["openInformationRequest"] is None, b["status"])
s, b, _ = call("POST", f"/api/claims/{awaiting['id']}/information-requests/{req_id}/response", my, {"response": "again"})
check("answering twice -> 409", s == 409, b and b.get("detail"))

officer = login("officer.my@demo.local")
s, b, _ = call("GET", f"/api/claims/{new_id}", officer)
check("MY officer sees reserve on MY claim", s == 200 and b["reserveAmount"] == 7300.5, b and b["reserveAmount"])
s, b, _ = call("POST", "/api/claims", officer, fnol)
check("officer cannot submit FNOL -> 403", s == 403, s)
s, b, _ = call("GET", "/api/policies/mine", officer)
check("officer cannot list claimant policies -> 403", s == 403, s)

sg_officer = login("officer.sg@demo.local")
s, b, _ = call("GET", f"/api/claims/{new_id}", sg_officer)
check("SG officer reading MY claim -> 404", s == 404, s)

print(f"\n{sum(results)}/{len(results)} checks passed")
print("NEW_CLAIM_ID", new_id)
