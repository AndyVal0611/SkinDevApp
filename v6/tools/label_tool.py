"""Blinded image-label tool for the V6 multilabel annotation file (standard library only; run it where the data lives, i.e. WSL).

Shows ONE image at a time with no model output, no folder/class name and no file name. You decide Acne, Hyperpigmentation and Eczema
independently (present / absent / unknown) and the answer is written straight into multilabel_annotations.csv using the protocol in
annotations/ANNOTATION_PROTOCOL.md. A progress panel compares your counts with the notebook's minimums.

  python3 label_tool.py --reviewer-id R01 --role researcher [--splits validation,train] [--port 8765]

Then open http://127.0.0.1:8765 . Nothing leaves your machine (the server binds to 127.0.0.1 only).

Rules the tool follows (so the notebook accepts the rows):
  * Normal is not a label: it is all three conditions "absent". "unknown" means the image cannot show it and is never negative.
  * status is `verified` when all three are decided, `partial` when one or two are, otherwise the row is left `unverified`.
  * The annotation file is backed up once at start-up (<name>.bak_<time>) and every save is an atomic replace.
  * The notebook treats annotation files as read-only during a run: after labelling, start a NEW RUN_ID.
  * role=researcher labels are accepted for TRAINING. They are accepted for validation/evaluation only if the config sets
    EVAL_REQUIRES_DERMATOLOGIST=false (a development run); use --role dermatologist only for an actual dermatologist.
"""
import argparse, csv, datetime, glob, json, os, shutil, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

HOME = os.path.expanduser("~")
COND = ["acne", "hyperpigmentation", "eczema"]
VALUES = {"present", "absent", "unknown"}
# Provisional minimums from the example configuration (v6_dataset_config.example.json).
MIN_VERIFIED = {"train": 300, "validation": 150, "fusion": 250, "test": 250}
MIN_POS = {"train": 40, "validation": 20, "fusion": 30, "test": 30}
MIN_NEG = {"train": 40, "validation": 20, "fusion": 30, "test": 30}

ap = argparse.ArgumentParser()
ap.add_argument("--reviewer-id", required=True, help="pseudonymous code, never a name")
ap.add_argument("--role", choices=["researcher", "dermatologist"], default="researcher")
ap.add_argument("--splits", default="validation,train", help="comma list, labelled in this order")
ap.add_argument("--annotations", default=os.path.join(HOME, "PrecisionSkinV1/v6/annotations/multilabel_annotations.csv"))
ap.add_argument("--manifest", default=os.path.join(HOME, "PrecisionSkinV1/metadata/metadata/split_manifest.csv"))
ap.add_argument("--processed-root", default=os.path.join(HOME, "PrecisionSkinV1/dataset_processed/processed"))
ap.add_argument("--original-root", default="", help="folder with <Class>/<file> full-size images (auto-discovered under /mnt/c/Users/*/Downloads/PrecisionSkin/Classes)")
ap.add_argument("--port", type=int, default=8790)
ap.add_argument("--boost", default="", choices=["", "acne", "hyperpigmentation", "eczema"],
                help="TRAIN split only: mix in extra images from this source folder (every 3rd image) to reach a rare condition's minimum sooner. "
                     "The folder name is only a weak hint, never a label; leave it off for validation/test, which must stay a random sample.")
ap.add_argument("--assist", action="store_true",
                help="TRAIN split only: show suggestions from label_assist.py (a model fitted on YOUR earlier labels). Rows you saw a suggestion for are saved "
                     "with blinded_to_ai=no, so they can never become evaluation labels. Never active for validation/fusion/test.")
ap.add_argument("--proposals", default="",
                help="REVIEW MODE: CSV of AI-proposed labels (image_id,acne,hyperpigmentation,eczema,confidence,note). Only those images are offered, lowest confidence first, "
                     "with the proposal pre-selected. You confirm or correct each one; the saved row is marked blinded_to_ai=no and notes 'ai-proposed ... accepted|corrected'.")
ap.add_argument("--revisit", default="",
                help="RE-LABEL MODE: CSV with an image_id column (for example background_review_flags.csv). Only those photos are offered, even if already verified, with their "
                     "current labels pre-selected. A saved row gets blinded_to_ai=no and the note 'relabelled after background review'.")
ap.add_argument("--venv-python", default=os.path.join(HOME, "PrecisionSkinV1/.venv/bin/python"))
args = ap.parse_args()

if not args.original_root:
    found = sorted(glob.glob("/mnt/c/Users/*/Downloads/PrecisionSkin/Classes"))
    args.original_root = found[0] if found else ""
SPLITS = [s.strip() for s in args.splits.split(",") if s.strip()]
LOCK = threading.Lock()

# ---- data -------------------------------------------------------------------------------------------------------------------------
with open(args.manifest, newline="", encoding="utf-8") as f:
    MANIFEST = {r["image_id"]: (r["split"], r["label_class"], r["filename"]) for r in csv.DictReader(f)}


def read_rows():
    with open(args.annotations, newline="", encoding="utf-8") as f:
        rd = csv.DictReader(f)
        return list(rd), rd.fieldnames


def write_rows(rows, fields):
    tmp = args.annotations + ".tmp"
    with open(tmp, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)
    os.replace(tmp, args.annotations)


PROPOSALS = {}
if args.proposals:
    with open(args.proposals, newline="", encoding="utf-8") as f:
        PROPOSALS = {r["image_id"]: r for r in csv.DictReader(f)}
REVISIT_IDS = []
if args.revisit:
    with open(args.revisit, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            if r["image_id"] not in REVISIT_IDS: REVISIT_IDS.append(r["image_id"])
REVISITED = set()
ROWS, FIELDS = read_rows()
BY_ID = {r["image_id"]: r for r in ROWS}
backup = args.annotations + ".bak_" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
shutil.copy2(args.annotations, backup)
LAST = []  # image_ids saved in this session, for undo
ASSIST_DIR = os.path.join(os.path.dirname(args.annotations), "label_assist")
SUG_PATH = os.path.join(ASSIST_DIR, "suggestions_train.csv")
SUG, SUG_MTIME = {}, 0.0
ASSIST_STATE = {"running": False, "msg": ""}


def suggestions_for(row):
    """Suggestions exist only for train rows, only with --assist, and only for conditions that have enough labels."""
    global SUG, SUG_MTIME
    if not args.assist or row["split"] != "train" or not os.path.isfile(SUG_PATH):
        return None
    m = os.path.getmtime(SUG_PATH)
    if m != SUG_MTIME:
        with open(SUG_PATH, newline="", encoding="utf-8") as f:
            SUG = {r["image_id"]: {c: (float(r["p_" + c]) if r["p_" + c] else None) for c in COND} for r in csv.DictReader(f)}
        SUG_MTIME = m
    return SUG.get(row["image_id"])


def run_assist():
    import subprocess
    ASSIST_STATE.update(running=True, msg="starting (the first run embeds every train image: a few minutes)")
    try:
        r = subprocess.run([args.venv_python, os.path.join(os.path.dirname(os.path.abspath(__file__)), "label_assist.py"), "--annotations", args.annotations],
                           capture_output=True, text=True, stdin=subprocess.DEVNULL, timeout=3600)
        tail = [l for l in (r.stdout + r.stderr).splitlines() if l.strip()][-5:]
        ASSIST_STATE.update(msg=" | ".join(tail))
    except Exception as e:
        ASSIST_STATE.update(msg="failed: " + str(e)[:200])
    ASSIST_STATE["running"] = False


PATH_CACHE = {}


def image_path(image_id):
    if image_id in PATH_CACHE:
        return PATH_CACHE[image_id]
    m = MANIFEST.get(image_id)
    found = None
    if m:
        split, cls, fn = m
        for p in ([os.path.join(args.original_root, cls, fn)] if args.original_root else []) + [os.path.join(args.processed_root, split, cls, fn)]:
            if os.path.isfile(p):
                found = p
                break
    PATH_CACHE[image_id] = found
    return found


CONF_RANK = {"low": 0, "medium": 1, "high": 2}


def queue():
    if args.revisit:
        return [BY_ID[i] for i in REVISIT_IDS if i in BY_ID and i not in REVISITED]
    if args.proposals:
        todo = [r for r in ROWS if r["image_id"] in PROPOSALS and r["review_status"] in ("", "unverified")]
        todo.sort(key=lambda r: (CONF_RANK.get(PROPOSALS[r["image_id"]].get("confidence", ""), 0), int(r["review_rank"] or 0)))
        return todo
    todo = [r for r in ROWS if r["split"] in SPLITS and r["review_status"] in ("", "unverified")]
    todo.sort(key=lambda r: (SPLITS.index(r["split"]), int(r["review_rank"] or 0)))
    if args.boost:
        train = [r for r in todo if r["split"] == "train"]
        hot = [r for r in train if MANIFEST.get(r["image_id"], ("", "", ""))[1].lower() == args.boost]
        hot_ids = {r["image_id"] for r in hot}
        rest = [r for r in train if r["image_id"] not in hot_ids]
        mixed, i, j = [], 0, 0
        while i < len(hot) or j < len(rest):
            if i < len(hot) and (len(mixed) % 3 == 0 or j >= len(rest)):
                mixed.append(hot[i]); i += 1
            else:
                mixed.append(rest[j]); j += 1
        todo = [r for sp in SPLITS for r in (mixed if sp == "train" else [x for x in todo if x["split"] == sp])]
    return todo


def progress():
    out = {}
    for s in ["train", "validation", "fusion", "test"]:
        rows = [r for r in ROWS if r["split"] == s]
        done = [r for r in rows if r["review_status"] in ("verified", "partial")]
        full = [r for r in done if r["review_status"] == "verified"]
        conds = {c: dict(present=sum(r[c] == "present" for r in done), absent=sum(r[c] == "absent" for r in done)) for c in COND}
        out[s] = dict(total=len(rows), labelled=len(done), verified=len(full), min_verified=MIN_VERIFIED[s], min_pos=MIN_POS[s], min_neg=MIN_NEG[s], conditions=conds)
    return out


# ---- web --------------------------------------------------------------------------------------------------------------------------
PAGE = r"""<!doctype html><meta charset=utf-8><title>V6 label tool</title><meta name=viewport content="width=device-width,initial-scale=1">
<style>
body{font:15px system-ui,sans-serif;margin:0;display:flex;gap:24px;padding:16px;background:#f6f6f4;color:#222}
#left{flex:1;min-width:320px}#right{width:380px}
img{max-width:100%;max-height:70vh;background:#ddd;border-radius:6px}
.row{display:flex;gap:8px;align-items:center;margin:8px 0}.row b{width:150px}
button{padding:8px 14px;border:1px solid #888;border-radius:6px;background:#fff;cursor:pointer;font-size:15px}
button.sel{background:#1d6fd6;color:#fff;border-color:#1d6fd6}
.act button{background:#2a8a3e;color:#fff;border-color:#2a8a3e}.act button.alt{background:#fff;color:#222;border-color:#888}
table{border-collapse:collapse;width:100%;font-size:13px}td,th{border-bottom:1px solid #ccc;padding:3px 6px;text-align:right}th:first-child,td:first-child{text-align:left}
.ok{color:#1b7a2e;font-weight:600}.no{color:#a33}.muted{color:#666}
</style>
<div id=left><div id=info class=muted></div><img id=im>
<div id=ctl></div>
<div id=assist class=muted></div>
<div class=act><button onclick=allAbsent()>Rest absent &amp; save (0)</button> <button onclick=save()>Save &amp; next (S)</button> <button class=alt onclick=skip()>Skip (X)</button> <button class=alt onclick=undo()>Undo last</button></div>
<p class=muted><b>Keys</b> (present / absent / unknown): Acne <b>1 2 3</b>, Hyperpigmentation <b>4 5 6</b>, Eczema <b>7 8 9</b>. <b>0</b> = every condition still unknown becomes absent. <b>S</b> or Enter = save &amp; next. <b>X</b> or Space = skip. <b>G</b> = apply model suggestions (train, with --assist). Use 0 only when you can really judge the others as absent; otherwise set them to unknown (3/6/9) first. Normal = all three absent. If the image cannot show a condition, choose unknown, never absent.</p></div>
<div id=right><h3>Progress vs minimums</h3><div id=prog></div></div>
<script>
const C=["acne","hyperpigmentation","eczema"],V=["present","absent","unknown"];let cur=null,sel={};
function hint(c){const p=cur&&cur.suggest&&cur.suggest[c];if(p==null)return"";const v=p>=0.5?"present":"absent";return` <span class=muted>suggests <b>${v}</b> (${p.toFixed(2)})</span>`}
function draw(){document.getElementById("ctl").innerHTML=C.map(c=>`<div class=row><b>${c}</b>`+V.map(v=>`<button class="${sel[c]==v?'sel':''}" onclick="pick('${c}','${v}')">${v}</button>`).join("")+hint(c)+`</div>`).join("")}
function assistBar(){const a=document.getElementById("assist");
 if(cur&&cur.revisit){a.innerHTML=`<b>Current labels are pre-selected.</b> A detector box on this photo was flagged as a real lesion. Look at the photo again and change any condition that is wrong, then press S to save.`;return}
 if(cur&&cur.proposal){a.innerHTML=`<b>AI-proposed labels are pre-selected</b> (confidence: ${cur.confidence}; ${cur.note}). Look at the image yourself, change anything wrong, then press Enter to confirm. Low-confidence images come first.`;return}
 if(!cur||!cur.assist_on){a.innerHTML="";return}
 a.innerHTML=(cur.suggest?"Model suggestions are shown (train only). Your answer is what counts: check each image yourself. Key <b>g</b> = apply them.":"No suggestions for this image yet (a condition needs about 8 present and 8 absent labels first).")+' <button onclick="refreshAssist()">Update suggestions</button> <span id=astat></span>'}
async function refreshAssist(){await post("/api/assist-refresh");pollAssist()}
async function pollAssist(){const s=await (await fetch("/api/assist-status")).json();const e=document.getElementById("astat");if(e)e.textContent=(s.running?"working: ":"")+s.msg;if(s.running)setTimeout(pollAssist,3000)}
function applyHints(){if(!cur||!cur.suggest)return;C.forEach(c=>{const p=cur.suggest[c];if(p!=null)sel[c]=p>=0.5?"present":"absent"});draw()}
function pick(c,v){sel[c]=v;draw()}
const pre=[];async function load(){const r=await (await fetch("/api/next")).json();cur=r.item;(r.upcoming||[]).forEach(id=>{const i=new Image();i.src="/img/"+id;pre.push(i);if(pre.length>12)pre.shift()});sel={acne:"unknown",hyperpigmentation:"unknown",eczema:"unknown"};if(cur&&cur.proposal)sel=Object.assign({},cur.proposal);
 document.getElementById("im").src=cur?"/img/"+cur.image_id+"?"+Date.now():"";
 document.getElementById("info").textContent=cur?`${cur.split} · ${r.remaining} left in the queue · image ${cur.image_id}`:"Queue empty for the chosen splits.";
 draw();assistBar();prog(r.progress)}
function prog(p){let h="<table><tr><th>split</th><th>verified</th><th>min</th></tr>";
 for(const s in p){const x=p[s];h+=`<tr><td>${s}</td><td class="${x.verified>=x.min_verified?'ok':'no'}">${x.verified}</td><td>${x.min_verified}</td></tr>`;
  h+=`<tr><td colspan=3 class=muted style="text-align:left">`+Object.entries(x.conditions).map(([c,n])=>`${c.slice(0,4)} +${n.present}/−${n.absent} (need ${x.min_pos}/${x.min_neg})`).join("<br>")+`</td></tr>`}
 document.getElementById("prog").innerHTML=h+"</table><p class=muted>Counts are verified+partial rows already saved. Unknown never counts as absent.</p>"}
async function post(u,b){const r=await fetch(u,{method:"POST",body:JSON.stringify(b||{})});return r.json()}
async function save(){if(!cur)return;if(C.every(c=>sel[c]=="unknown")){alert("All three are unknown: use Skip instead.");return}
 const r=await post("/api/save",{image_id:cur.image_id,labels:sel,assisted:!!((cur.assist_on&&cur.suggest)||cur.proposal)});if(r.error)alert(r.error);load()}
async function allAbsent(){if(!cur)return;C.forEach(c=>{if(sel[c]=="unknown")sel[c]="absent"});await save()}
async function skip(){if(!cur)return;await post("/api/skip",{image_id:cur.image_id});load()}
async function undo(){const r=await post("/api/undo");if(r.error)alert(r.error);load()}
document.addEventListener("keydown",e=>{if(e.ctrlKey||e.metaKey||e.altKey)return;const k=e.key.toLowerCase();const m={"1":["acne","present"],"2":["acne","absent"],"3":["acne","unknown"],"4":["hyperpigmentation","present"],"5":["hyperpigmentation","absent"],"6":["hyperpigmentation","unknown"],"7":["eczema","present"],"8":["eczema","absent"],"9":["eczema","unknown"]};
 if(m[k]){pick(...m[k])}else if(k=="g"){applyHints()}else if(k=="0"){allAbsent()}else if(k=="s"||k=="enter"){e.preventDefault();save()}else if(k=="x"||k==" "){e.preventDefault();skip()}});
let skipped=[];load();
</script>"""
SKIPPED = set()


class H(BaseHTTPRequestHandler):
    def _send(self, code, body, ctype="application/json"):
        b = body if isinstance(body, bytes) else body.encode()
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(b)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(b)

    def log_message(self, *a):
        pass

    def do_GET(self):
        p = urlparse(self.path).path
        if p == "/":
            return self._send(200, PAGE, "text/html; charset=utf-8")
        if p == "/api/assist-status":
            return self._send(200, json.dumps(ASSIST_STATE))
        if p == "/api/next":
            with LOCK:
                q = [r for r in queue() if r["image_id"] not in SKIPPED]
                ready = []
                for r in q:  # stat files lazily: /mnt/c is slow
                    if image_path(r["image_id"]):
                        ready.append(r)
                    if len(ready) == 4:
                        break
                first = ready[0] if ready else None
                item = {"image_id": first["image_id"], "split": first["split"]} if first else None
                if first and args.revisit:
                    item["proposal"] = {c: first[c] or "unknown" for c in COND}
                    item["confidence"] = "current"; item["note"] = "currently labelled as shown; a detector box on this photo was flagged as a real lesion"; item["revisit"] = True
                if first and args.proposals and first["image_id"] in PROPOSALS:
                    pr = PROPOSALS[first["image_id"]]
                    item["proposal"] = {c: pr[c] for c in COND}
                    item["confidence"] = pr.get("confidence", "")
                    item["note"] = pr.get("note", "")
                if first and args.assist and first["split"] == "train":
                    item["suggest"] = suggestions_for(first)
                    item["assist_on"] = True
                return self._send(200, json.dumps(dict(item=item, upcoming=[r["image_id"] for r in ready[1:]], remaining=len(q), progress=progress())))
        if p.startswith("/img/"):
            iid = p[5:]
            path = image_path(iid) if iid in BY_ID else None
            if not path:
                return self._send(404, "{}")
            ct = "image/png" if path.lower().endswith(".png") else "image/jpeg"
            with open(path, "rb") as f:
                return self._send(200, f.read(), ct)
        self._send(404, "{}")

    def do_POST(self):
        n = int(self.headers.get("Content-Length") or 0)
        body = json.loads(self.rfile.read(n) or b"{}")
        p = urlparse(self.path).path
        with LOCK:
            if p == "/api/assist-refresh":
                if not args.assist:
                    return self._send(200, json.dumps(dict(error="start the tool with --assist")))
                if not ASSIST_STATE["running"]:
                    threading.Thread(target=run_assist, daemon=True).start()
                return self._send(200, "{}")
            if p == "/api/skip":
                SKIPPED.add(body.get("image_id"))
                return self._send(200, "{}")
            if p == "/api/save":
                row = BY_ID.get(body.get("image_id"))
                labels = body.get("labels", {})
                if not row or set(labels) != set(COND) or not set(labels.values()) <= VALUES:
                    return self._send(200, json.dumps(dict(error="invalid request")))
                decided = sum(v != "unknown" for v in labels.values())
                if decided == 0:
                    return self._send(200, json.dumps(dict(error="nothing decided")))
                for c in COND:
                    row[c] = labels[c]
                row["review_status"] = "verified" if decided == 3 else "partial"
                prop = PROPOSALS.get(row["image_id"]) if args.proposals else None
                shown = (bool(body.get("assisted")) and row["split"] == "train" and args.assist) or prop is not None
                row["reviewer_id"], row["reviewer_role"], row["blinded_to_ai"] = args.reviewer_id, args.role, ("no" if shown else "yes")
                if args.revisit:
                    row["blinded_to_ai"] = "no"; REVISITED.add(row["image_id"])
                    row["notes"] = ((row.get("notes") or "") + " relabelled after background review (detector box shown)").strip()
                elif prop is not None:
                    changed = any(labels[c] != prop[c] for c in COND)
                    tag = "ai-proposed(claude) " + ("corrected" if changed else "accepted") + " by " + args.reviewer_id
                    row["notes"] = ((row.get("notes") or "") + " " + tag).strip()
                elif shown and "assisted" not in (row.get("notes") or ""):
                    row["notes"] = ((row.get("notes") or "") + " assisted(model suggestion shown)").strip()
                row["reviewed_at"] = datetime.datetime.now().isoformat(timespec="seconds")
                if not row.get("image_origin"):
                    row["image_origin"] = "source_dataset"
                write_rows(ROWS, FIELDS)
                LAST.append(row["image_id"])
                return self._send(200, "{}")
            if p == "/api/undo":
                if not LAST:
                    return self._send(200, json.dumps(dict(error="nothing to undo in this session")))
                row = BY_ID[LAST.pop()]
                for c in COND:
                    row[c] = ""
                for k in ("review_status", "reviewer_id", "reviewer_role", "blinded_to_ai", "reviewed_at"):
                    row[k] = "unverified" if k == "review_status" else ""
                SKIPPED.discard(row["image_id"])
                write_rows(ROWS, FIELDS)
                return self._send(200, "{}")
        self._send(404, "{}")


if __name__ == "__main__":
    print(f"annotations : {args.annotations}\nbackup      : {backup}\noriginals   : {args.original_root or '(none, using processed 224x224)'}")
    print(f"reviewer    : {args.reviewer_id} ({args.role}), splits {SPLITS}\nopen http://127.0.0.1:{args.port}   (Ctrl+C to stop)")
    ThreadingHTTPServer(("127.0.0.1", args.port), H).serve_forever()
