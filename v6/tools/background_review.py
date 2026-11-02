"""Review detector false alarms and save the ones that are NOT lesions as `background_false_positive` regions (V6 ROI class 4).

  python background_review.py --reviewer-id R01        (Windows Python; open http://127.0.0.1:8791)

What it shows: one YOLO box (from the saved detections of the acne / hyperpigmentation / eczema detectors, at their deployed thresholds) on a photo
YOU labelled with none of the three conditions (train and validation; the label origin, direct blind or AI-assisted, is recorded in each saved note). Because the photo has no condition, such a box should be
a false alarm. You decide from the photo itself:
  B / Enter = this box is NOT a lesion (saved as background_false_positive, verified, role researcher, source yolo_proposal_reviewed)
  X / Space = skip (nothing saved)
  L         = it IS a real lesion: skip it and log the image in background_review_flags.csv (your image label may be wrong)
  U         = undo the last save
Targets for the ROI step: at least 60 in train and 20 in validation (the page shows your progress). Writes are atomic and the region file is backed up at start-up.
IMPORTANT: finish this BEFORE starting a new notebook run: the region file is part of a run's identity.
"""
import argparse, collections, csv, datetime, json, os, shutil, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

ap = argparse.ArgumentParser()
ap.add_argument("--reviewer-id", required=True)
ap.add_argument("--port", type=int, default=8791)
ap.add_argument("--regions", default=r"\\wsl.localhost\Ubuntu\home\bautista\PrecisionSkinV1\v6\annotations\region_annotations.csv")
ap.add_argument("--annotations", default=r"\\wsl.localhost\Ubuntu\home\bautista\PrecisionSkinV1\v6\annotations\multilabel_annotations.csv")
ap.add_argument("--manifest", default=r"\\wsl.localhost\Ubuntu\home\bautista\PrecisionSkinV1\metadata\metadata\split_manifest.csv")
ap.add_argument("--processed-root", default=r"\\wsl.localhost\Ubuntu\home\bautista\PrecisionSkinV1\dataset_processed\processed")
ap.add_argument("--originals", default=r"C:\Users\Bautista\Downloads\PrecisionSkin\Classes")
ap.add_argument("--detections", default=r"C:\Users\Bautista\source\repos\SkinDevApp\v6\astra_runs\open_run01\yolo\detections_ce6ae3186e771bfe.jsonl")
ap.add_argument("--flags", default=r"C:\Users\Bautista\source\repos\SkinDevApp\v6\annotations\background_review_flags.csv")
ap.add_argument("--target-train", type=int, default=60)
ap.add_argument("--target-val", type=int, default=20)
args = ap.parse_args()
COLS = ["region_id", "image_id", "x0", "y0", "x1", "y1", "label", "exhaustive", "review_status", "reviewer_id", "reviewer_role", "annotation_source", "reviewed_at", "notes"]
LOCK = threading.Lock()

man = {r["image_id"]: r for r in csv.DictReader(open(args.manifest, encoding="utf-8"))}
ann = {r["image_id"]: r for r in csv.DictReader(open(args.annotations, encoding="utf-8"))}
C3 = ("acne", "hyperpigmentation", "eczema")
clean = {i for i, a in ann.items() if a["split"] in ("train", "validation") and a["review_status"] == "verified" and all(a[c] == "absent" for c in C3)}
origin = lambda i: "AI-assisted label, reviewed" if ("ai-proposed" in ann[i]["notes"] or "assisted" in ann[i]["notes"] or ann[i]["blinded_to_ai"] != "yes") else "direct blind label"
regions = list(csv.DictReader(open(args.regions, encoding="utf-8")))
backup = args.regions + ".bak_" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S"); shutil.copy2(args.regions, backup)
by_img = collections.defaultdict(list)
for r in regions: by_img[r["image_id"]].append((float(r["x0"]), float(r["y0"]), float(r["x1"]), float(r["y1"])))


def iou(a, b):
    iw, ih = max(0, min(a[2], b[2]) - max(a[0], b[0])), max(0, min(a[3], b[3]) - max(a[1], b[1])); i = iw * ih
    return i / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - i + 1e-12)


# candidates: deployed-threshold boxes on clean photos, at most 2 per photo, round-robin over the three detectors, highest confidence first
cand = collections.defaultdict(list)
for line in open(args.detections, encoding="utf-8"):
    try: d = json.loads(line)
    except Exception: continue
    iid = d["image_id"]
    if iid not in clean: continue
    for cond, blk in d["detectors"].items():
        for b in blk["boxes"]:
            if not b.get("deployed"): continue
            box = (max(0, b["x0"] / b["frame_w"]), max(0, b["y0"] / b["frame_h"]), min(1, b["x1"] / b["frame_w"]), min(1, b["y1"] / b["frame_h"]))
            if box[2] <= box[0] or box[3] <= box[1] or any(iou(box, g) > 0.2 for g in by_img.get(iid, [])): continue
            cand[(ann[iid]["split"], cond)].append((b["conf"], iid, cond, box))
queue = []
for split in ("train", "validation"):
    lists = {c: sorted(cand[(split, c)], reverse=True) for c in ("Acne", "Hyperpigmentation", "Eczema")}; per = collections.Counter(); seen = []
    ordered = []
    while any(lists.values()):
        for c in list(lists):
            while lists[c]:
                conf, iid, cond, box = lists[c].pop(0)
                if per[iid] >= 2 or any(s[0] == iid and iou(box, s[1]) > 0.5 for s in seen): continue
                per[iid] += 1; seen.append((iid, box)); ordered.append(dict(image_id=iid, split=split, cond=cond, conf=round(conf, 3), box=box, origin=origin(iid))); break
    queue.append(ordered)
tr, va = queue; mixed = []
while tr or va:
    for _ in range(3):
        if tr: mixed.append(tr.pop(0))
    if va: mixed.append(va.pop(0))
DONE, SKIPPED, LAST = set(), set(), []


def key(c): return (c["image_id"], tuple(round(v, 4) for v in c["box"]))


def image_path(iid):
    m = man[iid]; cls, fn = m["label_class"], m["filename"]
    for p in (os.path.join(args.originals, cls, fn), os.path.join(args.processed_root, m["split"], cls, fn)):
        if os.path.isfile(p): return p


def counts():
    c = collections.Counter(); ids = {r["image_id"] for r in regions if r["label"] == "background_false_positive"}
    for r in regions:
        if r["label"] == "background_false_positive": c[ann[r["image_id"]]["split"]] += 1
    return dict(train=c["train"], validation=c["validation"], target_train=args.target_train, target_validation=args.target_val)


def write():
    tmp = args.regions + ".tmp"
    with open(tmp, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=COLS); w.writeheader(); w.writerows(regions)
    os.replace(tmp, args.regions)


PAGE = r"""<!doctype html><meta charset=utf-8><title>Background review</title><meta name=viewport content="width=device-width,initial-scale=1">
<style>body{font:15px system-ui,sans-serif;margin:0;padding:14px;background:#f6f6f4;color:#222}
#wrap{display:flex;gap:18px;flex-wrap:wrap}#imgbox{position:relative;display:inline-block;line-height:0}#imgbox img{max-height:72vh;max-width:62vw;border-radius:6px}
#bx{position:absolute;border:3px solid #e22;box-sizing:border-box}#crop{width:300px;border:2px solid #888;border-radius:6px;background-repeat:no-repeat;background-color:#ddd}
button{padding:9px 16px;border:1px solid #888;border-radius:6px;background:#fff;cursor:pointer;font-size:15px;margin:3px}.go{background:#2a8a3e;color:#fff;border-color:#2a8a3e}
.muted{color:#666}.bar{height:10px;background:#ddd;border-radius:5px;margin:4px 0 10px}.bar i{display:block;height:10px;background:#2a8a3e;border-radius:5px}</style>
<div id=prog></div><div id=wrap><div><div id=imgbox><img id=im><div id=bx></div></div><div id=info class=muted></div></div>
<div><div class=muted>zoomed view of the red box</div><div id=crop></div><p>
<button class=go onclick="act('save')">B &middot; NOT a lesion (background)</button><button onclick="act('skip')">X &middot; skip</button>
<button onclick="act('flag')">L &middot; it IS a lesion</button><button onclick="act('undo')">U &middot; undo</button></p>
<p class=muted>Decide from the photo only. The red box came from a detector; a saved box teaches the ROI model that this kind of look is NOT a lesion. When unsure, skip.</p></div></div>
<script>
let cur=null;
function bar(n,t,l){return `<div>${l}: <b>${n}</b> / ${t}</div><div class=bar><i style="width:${Math.min(100,100*n/t)}%"></i></div>`}
function draw(){const im=document.getElementById("im"),b=cur.box,bx=document.getElementById("bx");
 bx.style.left=100*b[0]+"%";bx.style.top=100*b[1]+"%";bx.style.width=100*(b[2]-b[0])+"%";bx.style.height=100*(b[3]-b[1])+"%";
 const w=b[2]-b[0],h=b[3]-b[1],m=0.6*Math.max(w,h),cx=Math.max(0,b[0]-m),cy=Math.max(0,b[1]-m),cw=Math.min(1-cx,w+2*m),ch=Math.min(1-cy,h+2*m);
 const c=document.getElementById("crop");c.style.backgroundImage=`url(${im.src})`;c.style.backgroundSize=`${100/cw}% ${100/ch}%`;
 c.style.backgroundPosition=`${(1-cw)>0?100*cx/(1-cw):0}% ${(1-ch)>0?100*cy/(1-ch):0}%`;c.style.aspectRatio=`${cw*im.naturalWidth}/${ch*im.naturalHeight}`}
async function load(){const r=await (await fetch("/api/next")).json();const p=r.progress;
 document.getElementById("prog").innerHTML=bar(p.train,p.target_train,"train background boxes")+bar(p.validation,p.target_validation,"validation background boxes");
 cur=r.item;if(!cur){document.getElementById("info").textContent="No more candidates.";return}
 const im=document.getElementById("im");im.onload=draw;im.src="/img/"+cur.image_id+"?"+Date.now();
 document.getElementById("info").textContent=`${cur.split} | ${cur.cond} detector, confidence ${cur.conf} | ${r.remaining} candidates left`}
async function act(a){if(!cur&&a!="undo")return;const r=await (await fetch("/api/"+a,{method:"POST",body:JSON.stringify(cur||{})})).json();if(r.error)alert(r.error);load()}
document.addEventListener("keydown",e=>{const k=e.key.toLowerCase();if(k=="b"||k=="enter"){e.preventDefault();act("save")}else if(k=="x"||k==" "){e.preventDefault();act("skip")}else if(k=="l")act("flag");else if(k=="u")act("undo")});
load();</script>"""


class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass

    def send(self, code, body, ct="application/json"):
        b = body if isinstance(body, bytes) else body.encode()
        self.send_response(code); self.send_header("Content-Type", ct); self.send_header("Content-Length", str(len(b))); self.send_header("Cache-Control", "no-store"); self.end_headers(); self.wfile.write(b)

    def do_GET(self):
        p = urlparse(self.path).path
        if p == "/": return self.send(200, PAGE, "text/html; charset=utf-8")
        if p == "/api/next":
            with LOCK:
                rest = [c for c in mixed if key(c) not in DONE and key(c) not in SKIPPED]
                return self.send(200, json.dumps(dict(item=rest[0] if rest else None, remaining=len(rest), progress=counts())))
        if p.startswith("/img/"):
            iid = p[5:]; path = image_path(iid) if iid in man else None
            if not path: return self.send(404, "{}")
            return self.send(200, open(path, "rb").read(), "image/png" if path.lower().endswith(".png") else "image/jpeg")
        self.send(404, "{}")

    def do_POST(self):
        n = int(self.headers.get("Content-Length") or 0); c = json.loads(self.rfile.read(n) or b"{}"); p = urlparse(self.path).path
        with LOCK:
            if p == "/api/skip": SKIPPED.add(key(c)); return self.send(200, "{}")
            if p == "/api/flag":
                new = not os.path.exists(args.flags)
                with open(args.flags, "a", newline="", encoding="utf-8") as f:
                    w = csv.writer(f)
                    if new: w.writerow(["image_id", "split", "detector", "conf", "note", "flagged_at"])
                    w.writerow([c["image_id"], c["split"], c["cond"], c["conf"], "reviewer says this box is a real lesion although the image is labelled with no condition", datetime.datetime.now().isoformat(timespec="seconds")])
                SKIPPED.add(key(c)); return self.send(200, "{}")
            if p == "/api/save":
                k = key(c)
                if k in DONE: return self.send(200, json.dumps(dict(error="already saved")))
                nb = sum(1 for r in regions if r["label"] == "background_false_positive") + 1
                regions.append(dict(region_id="%s-back-%06d" % (args.reviewer_id, nb), image_id=c["image_id"], x0="%.5f" % c["box"][0], y0="%.5f" % c["box"][1], x1="%.5f" % c["box"][2], y1="%.5f" % c["box"][3],
                                    label="background_false_positive", exhaustive="", review_status="verified", reviewer_id=args.reviewer_id, reviewer_role="researcher", annotation_source="yolo_proposal_reviewed",
                                    reviewed_at=datetime.datetime.now().isoformat(timespec="seconds"),
                                    notes="YOLO %s box (conf %s) reviewed: not a lesion; photo labelled with no target condition (%s)" % (c["cond"], c["conf"], c.get("origin", "label origin not recorded"))))
                DONE.add(k); LAST.append((k, regions[-1]["region_id"])); write(); return self.send(200, "{}")
            if p == "/api/undo":
                if not LAST: return self.send(200, json.dumps(dict(error="nothing to undo in this session")))
                k, rid = LAST.pop(); regions[:] = [r for r in regions if r["region_id"] != rid]; DONE.discard(k); write(); return self.send(200, "{}")
        self.send(404, "{}")


if __name__ == "__main__":
    print("clean photos:", len(clean), "| candidate boxes:", len(mixed), dict(collections.Counter(c["split"] for c in mixed)), "| backup:", backup, flush=True)
    print("open http://127.0.0.1:%d   (Ctrl+C to stop)" % args.port)
    ThreadingHTTPServer(("127.0.0.1", args.port), H).serve_forever()
