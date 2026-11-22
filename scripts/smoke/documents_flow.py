"""Photos and documents on a claim, against http://localhost:5080: upload, read back, refusals, who may see them.
Any state: it reports its own claim first. Prints one line per check."""
import http.client
import json
import struct
import time
import urllib.error
import urllib.request
import uuid
import zlib
from datetime import date, timedelta

BASE = "http://localhost:5080"
results = []


def request(method, path, token=None, data=None, content_type="application/json"):
    req = urllib.request.Request(BASE + path, method=method, data=data)
    if data is not None:
        req.add_header("Content-Type", content_type)
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, r.read(), dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read(), dict(e.headers)


def call(method, path, token=None, body=None):
    s, raw, h = request(method, path, token, json.dumps(body).encode() if body is not None else None)
    return s, (json.loads(raw) if raw else None), h


def upload(claim_id, token, file_name, content, field="file"):
    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"{field}\"; filename=\"{file_name}\"\r\n"
            f"Content-Type: application/octet-stream\r\n\r\n").encode() + content + f"\r\n--{boundary}--\r\n".encode()
    s, raw, h = request("POST", f"/api/claims/{claim_id}/documents", token, body, f"multipart/form-data; boundary={boundary}")
    return s, (json.loads(raw) if raw else None), h


def check(name, ok, detail=""):
    results.append(ok)
    print(f"{'PASS' if ok else 'FAIL'}  {name}" + (f"  -> {detail}" if detail else ""))


def login(email):
    s, b, _ = call("POST", "/api/auth/dev-login", body={"email": email})
    assert s == 200, (s, b)
    return b["accessToken"]


def png(width=4, height=3):
    """A real (tiny, orange) PNG, so a browser could show it too."""
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    rows = b"".join(b"\x00" + b"\xf9\x73\x16" * width for _ in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


PDF = b"%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"

my = login("claimant.my@demo.local")
sg = login("claimant.sg@demo.local")
officer = login("officer.my@demo.local")
sg_officer = login("officer.sg@demo.local")

yesterday = (date.today() - timedelta(days=1)).isoformat()
s, claim, _ = call("POST", "/api/claims", my, {
    "policyNumber": "POL-MY-M-0001", "type": "Motor", "incidentDate": yesterday, "incidentLocation": "Petaling Jaya",
    "description": "Documents smoke test", "estimatedLossAmount": 1500,
    "motor": {"vehicleRegistration": "DOC 1", "thirdPartyInvolved": False, "policeReportNumber": None}})
assert s == 201, (s, claim)
cid = claim["id"]

# ------------------------------------------------------------ upload and read back
photo = png()
s, doc, h = upload(cid, my, "front bumper.png", photo)
check("claimant uploads a photo -> 201 with the detected type and a Location",
      s == 201 and doc["contentType"] == "image/png" and doc["fileName"] == "front bumper.png" and doc["sizeBytes"] == len(photo)
      and doc["uploadedByRole"] == "Claimant" and f"/documents/{doc['id']}/content" in h.get("Location", ""), (s, doc))

s, raw, h = request("GET", f"/api/claims/{cid}/documents/{doc['id']}/content", my)
check("read back: the same bytes, its type, inline, nosniff, private cache",
      s == 200 and raw == photo and h.get("Content-Type") == "image/png" and h.get("X-Content-Type-Options") == "nosniff"
      and h.get("Content-Disposition", "").startswith("inline") and "private" in h.get("Cache-Control", ""),
      (s, h.get("Content-Type"), h.get("Content-Disposition")))

s, pdf_doc, _ = upload(cid, my, "police-report.jpg", PDF)
check("a PDF named .jpg is stored as a PDF (the bytes decide, not the name)", s == 201 and pdf_doc["contentType"] == "application/pdf", (s, pdf_doc))

# ------------------------------------------------------------ refusals
s, b, _ = upload(cid, my, "holiday.jpg", b"MZ\x90\x00\x03 this is a program, not a photo")
check("an executable renamed .jpg -> 422", s == 422 and "JPEG, PNG or WebP" in b["detail"], (s, b and b.get("detail")))
s, b, _ = upload(cid, my, "logo.svg", b'<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>')
check("an SVG (can carry script) -> 422", s == 422, s)
s, b, _ = upload(cid, my, "empty.jpg", b"")
check("an empty file -> 422 or 400", s in (400, 422), (s, b and b.get("detail")))
# Headers only: the API must refuse an 11 MB upload from its declared length, before reading it.
conn = http.client.HTTPConnection("localhost", 5080, timeout=10)
conn.putrequest("POST", f"/api/claims/{cid}/documents")
conn.putheader("Authorization", f"Bearer {my}")
conn.putheader("Content-Type", "multipart/form-data; boundary=x")
conn.putheader("Content-Length", str(11 * 1024 * 1024))
conn.endheaders()
r = conn.getresponse()
b = json.loads(r.read() or b"null")
conn.close()
check("11 MB -> 413 straight away, with a plain message",
      r.status == 413 and b and "at most 10 MB" in (b.get("detail") or ""), (r.status, b))
s, b, _ = upload(cid, my, "x.png", photo, field="wrong")
check("no file field -> 400 validation problem", s == 400 and b and "errors" in b, (s, b and b.get("errors")))

# ------------------------------------------------------------ who may see and attach
s, _, _ = request("GET", f"/api/claims/{cid}/documents/{doc['id']}/content", sg)
check("another claimant reading it -> 404 (not 403)", s == 404, s)
s, _, _ = upload(cid, sg, "mine.png", photo)
check("another claimant attaching -> 404", s == 404, s)
s, _, _ = request("GET", f"/api/claims/{cid}/documents/{doc['id']}/content", sg_officer)
check("an officer from another market -> 404", s == 404, s)
s, raw, _ = request("GET", f"/api/claims/{cid}/documents/{doc['id']}/content", officer)
check("an officer in the claim's market reads it", s == 200 and raw == photo, s)
s, _, _ = request("GET", f"/api/claims/{cid}/documents/{uuid.uuid4()}/content", my)
check("an unknown document id -> 404", s == 404, s)

s, team_doc, _ = upload(cid, officer, "assessor-note.pdf", PDF)
check("staff in the market can attach too", s == 201 and team_doc["uploadedByRole"] == "ClaimsOfficer", (s, team_doc))

s, detail, _ = call("GET", f"/api/claims/{cid}", my)
check("claim detail lists the documents, oldest first",
      [d["fileName"] for d in detail["documents"]] == ["front bumper.png", "police-report.jpg", "assessor-note.pdf"],
      [d["fileName"] for d in detail["documents"]])

# ------------------------------------------------------------ the other side is told (Kafka -> notifier)
message = None
for _ in range(30):
    s, notes, _ = call("GET", "/api/notifications", my)
    message = next((n["message"] for n in notes["items"] if n["claimId"] == cid and "added a document" in n["message"]), None)
    if message:
        break
    time.sleep(0.5)
check("the claimant is told the claims team added a document", message == f"The claims team added a document to your claim {claim['claimNumber']}: assessor-note.pdf", message)

# ------------------------------------------------------------ a closed claim takes no more
s, mine, _ = call("GET", "/api/claims/mine?pageSize=100", my)
closed = next((c for c in mine["items"] if c["status"] in ("Settled", "Rejected")), None)
if closed:
    s, b, _ = upload(closed["id"], my, "late.png", photo)
    check("a closed claim -> 409 with the reason", s == 409 and "no more documents" in b["detail"], (s, b and b.get("detail")))

print(f"\n{sum(results)}/{len(results)} checks passed")
