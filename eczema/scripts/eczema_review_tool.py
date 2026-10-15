#!/usr/bin/env python
"""
eczema_review_tool.py - tiny local box editor for the PrecisionSkin eczema review.

Python standard library only (no installs).  Python 3.8 or newer.

Usage (Windows, in a terminal):
    python eczema_review_tool.py "C:\\Users\\Bautista\\Documents\\PrecisionSkin_EczemaReview\\Bautista"

The folder is ONE reviewer's folder created by the drafting notebook; it must contain
    images\\   the photos
    labels\\   YOLO .txt files with the draft boxes (class 0 = eczema)
It opens your web browser on http://127.0.0.1:8765 (this computer only; nothing is uploaded anywhere).

What a reviewer does, for every image:
    * keep the boxes that sit on visible eczema; delete wrong ones; draw boxes the draft missed;
    * then SAVE (S).  An image only counts as reviewed once it has been saved.
    * no eczema visible / not a face  ->  press X (saves an empty label: the image is left out).

Keys:  S or Enter = save + next   X = no eczema (save empty) + next   A / Left = previous   D / Right = next (no save)
       Delete / Backspace = delete the selected box   Ctrl+Z = undo   N = next UNREVIEWED image
Mouse: drag on empty space = new box | click a box = select | drag a selected box = move | drag a corner = resize
"""
import argparse
import json
import mimetypes
import os
import sys
import threading
import time
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlparse

IMG_EXT = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


class Store:
    def __init__(self, root):
        self.root = os.path.abspath(root)
        self.images_dir = os.path.join(self.root, "images")
        self.labels_dir = os.path.join(self.root, "labels")
        if not os.path.isdir(self.images_dir):
            sys.exit("No 'images' folder in: " + self.root)
        os.makedirs(self.labels_dir, exist_ok=True)
        self.meta_path = os.path.join(os.path.dirname(self.root), "_drafts", "meta.json")
        self.lock = threading.Lock()

    @property
    def drafted_at(self):
        """When the drafting notebook finished writing the drafts (re-read every time, so re-drafting is picked up)."""
        try:
            with open(self.meta_path, "r", encoding="utf-8") as fh:
                return float(json.load(fh)["drafted_at"])
        except Exception:
            return None

    def names(self):
        return sorted(f for f in os.listdir(self.images_dir) if os.path.splitext(f)[1].lower() in IMG_EXT)

    def label_path(self, name):
        return os.path.join(self.labels_dir, os.path.splitext(name)[0] + ".txt")

    def read_boxes(self, name):
        p = self.label_path(name)
        out = []
        if os.path.exists(p):
            with open(p, "r", encoding="utf-8") as fh:
                for ln in fh:
                    t = ln.split()
                    if len(t) >= 5:
                        try:
                            out.append([float(v) for v in t[-4:]])
                        except ValueError:
                            pass
        return out

    def is_reviewed(self, name):
        p = self.label_path(name)
        if not os.path.exists(p):
            return False
        ref = self.drafted_at
        if ref is None:
            ref = os.path.getmtime(os.path.join(self.images_dir, name))
        return os.path.getmtime(p) > ref + 1

    def listing(self):
        res = []
        for n in self.names():
            res.append({"name": n, "reviewed": self.is_reviewed(n), "n": len(self.read_boxes(n))})
        return res

    def write_boxes(self, name, boxes):
        clean = []
        for b in boxes:
            cx, cy, w, h = [min(1.0, max(0.0, float(v))) for v in b]
            if w >= 0.003 and h >= 0.003:
                clean.append((cx, cy, w, h))
        p = self.label_path(name)
        tmp = p + ".tmp"
        with self.lock:
            with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
                for cx, cy, w, h in clean:
                    fh.write("0 %.6f %.6f %.6f %.6f\n" % (cx, cy, w, h))
            os.replace(tmp, p)
            # make sure the save time is clearly after the drafting time, even on coarse file systems
            now = time.time()
            os.utime(p, (now, now))
        return len(clean)


STORE = None

PAGE = r"""<!doctype html>
<html><head><meta charset="utf-8"><title>PrecisionSkin eczema review</title>
<style>
 body{margin:0;font-family:Segoe UI,Arial,sans-serif;background:#1a2328;color:#e9eef0}
 #top{display:flex;gap:14px;align-items:center;padding:8px 14px;background:#0f161a;flex-wrap:wrap}
 #top b{font-size:15px}
 button{background:#2b3a42;color:#e9eef0;border:1px solid #4a5d68;border-radius:6px;padding:6px 12px;font-size:13px;cursor:pointer}
 button:hover{background:#38505c} button.primary{background:#2f7d4f;border-color:#3f9a64} button.warn{background:#7a4b1f;border-color:#a56a2f}
 #bar{height:8px;background:#2b3a42;margin:0 14px 0 14px;border-radius:4px;overflow:hidden}
 #fill{height:100%;width:0;background:#3f9a64}
 #stage{padding:10px 14px;text-align:center}
 #wrap{position:relative;display:inline-block;line-height:0}
 #img{max-width:96vw;max-height:76vh;display:block;user-select:none;-webkit-user-drag:none}
 #cv{position:absolute;left:0;top:0;cursor:crosshair}
 #status{font-size:13px;margin-left:auto} .ok{color:#7fe3a2} .no{color:#f5c26b}
 #help{font-size:12px;color:#9fb0b8;padding:0 14px 8px 14px}
 kbd{background:#2b3a42;border:1px solid #4a5d68;border-radius:4px;padding:0 5px;font-size:11px}
</style></head>
<body>
<div id="top">
  <b id="title">Loading...</b>
  <span id="count"></span>
  <button id="prev">&larr; Prev (A)</button>
  <button id="next">Next (D) &rarr;</button>
  <button id="nextun">Next unreviewed (N)</button>
  <button id="del">Delete box (Del)</button>
  <button id="undo">Undo (Ctrl+Z)</button>
  <button id="none" class="warn">No eczema / skip (X)</button>
  <button id="save" class="primary">Save + next (S)</button>
  <span id="status"></span>
</div>
<div id="bar"><div id="fill"></div></div>
<div id="stage"><div id="wrap"><img id="img" alt=""><canvas id="cv"></canvas></div></div>
<div id="help">
  Keep boxes only on <b>visible eczema</b>. Drag on empty space = new box &middot; click a box = select &middot; drag it = move &middot; drag a corner = resize.
  An image counts as reviewed only after you <b>save</b> it (<kbd>S</kbd>), even if you changed nothing. If you cannot see eczema, press <kbd>X</kbd>.
</div>
<script>
let items=[], idx=0, boxes=[], sel=-1, dirty=false, drag=null, undoStack=[];
const img=document.getElementById('img'), cv=document.getElementById('cv'), ctx=cv.getContext('2d');
const $=id=>document.getElementById(id);

function api(path, opt){ return fetch(path, opt).then(r=>{ if(!r.ok) throw new Error(path+' '+r.status); return r.json(); }); }
function fit(){ cv.width=img.clientWidth; cv.height=img.clientHeight; cv.style.width=img.clientWidth+'px'; cv.style.height=img.clientHeight+'px'; draw(); }
function xyxy(b){ return [b[0]-b[2]/2, b[1]-b[3]/2, b[0]+b[2]/2, b[1]+b[3]/2]; }
function fromXyxy(a){ const x0=Math.min(a[0],a[2]), x1=Math.max(a[0],a[2]), y0=Math.min(a[1],a[3]), y1=Math.max(a[1],a[3]); return [(x0+x1)/2,(y0+y1)/2,x1-x0,y1-y0]; }
function clamp(v){ return Math.min(1, Math.max(0, v)); }
function pos(e){ const r=cv.getBoundingClientRect(); return [clamp((e.clientX-r.left)/r.width), clamp((e.clientY-r.top)/r.height)]; }

function draw(){
  ctx.clearRect(0,0,cv.width,cv.height);
  boxes.forEach((b,i)=>{
    const [x0,y0,x1,y1]=xyxy(b); const W=cv.width,H=cv.height;
    ctx.lineWidth = i===sel ? 3 : 2; ctx.strokeStyle = i===sel ? '#ffe14d' : '#ff9a1f';
    ctx.fillStyle = i===sel ? 'rgba(255,225,77,0.18)' : 'rgba(255,154,31,0.10)';
    ctx.fillRect(x0*W,y0*H,(x1-x0)*W,(y1-y0)*H); ctx.strokeRect(x0*W,y0*H,(x1-x0)*W,(y1-y0)*H);
    if(i===sel){ ctx.fillStyle='#ffe14d'; [[x0,y0],[x1,y0],[x0,y1],[x1,y1]].forEach(p=>ctx.fillRect(p[0]*W-5,p[1]*H-5,10,10)); }
  });
  if(drag && drag.type==='new'){
    const W=cv.width,H=cv.height; ctx.setLineDash([6,4]); ctx.strokeStyle='#7fe3a2'; ctx.lineWidth=2;
    ctx.strokeRect(Math.min(drag.x0,drag.x1)*W, Math.min(drag.y0,drag.y1)*H, Math.abs(drag.x1-drag.x0)*W, Math.abs(drag.y1-drag.y0)*H); ctx.setLineDash([]);
  }
}
function pushUndo(){ undoStack.push(JSON.stringify(boxes)); if(undoStack.length>60) undoStack.shift(); }
function doUndo(){ if(!undoStack.length) return; boxes=JSON.parse(undoStack.pop()); sel=-1; dirty=true; draw(); updateStatus(); }

function hitHandle(i,x,y){
  const a=xyxy(boxes[i]); const t=11/cv.width, u=11/cv.height;
  const c={tl:[a[0],a[1]],tr:[a[2],a[1]],bl:[a[0],a[3]],br:[a[2],a[3]]};
  for(const k in c){ if(Math.abs(x-c[k][0])<=t && Math.abs(y-c[k][1])<=u) return k; }
  return null;
}
function hitBox(x,y){
  let best=-1, bestA=1e9;
  boxes.forEach((b,i)=>{ const a=xyxy(b); if(x>=a[0]&&x<=a[2]&&y>=a[1]&&y<=a[3]){ const ar=b[2]*b[3]; if(ar<bestA){bestA=ar;best=i;} } });
  return best;
}
cv.addEventListener('mousedown',e=>{
  const [x,y]=pos(e);
  if(sel>=0){ const h=hitHandle(sel,x,y); if(h){ pushUndo(); drag={type:'resize',h:h,orig:xyxy(boxes[sel])}; return; } }
  const hit=hitBox(x,y);
  if(hit>=0){ sel=hit; pushUndo(); drag={type:'move',sx:x,sy:y,orig:boxes[hit].slice()}; draw(); return; }
  sel=-1; drag={type:'new',x0:x,y0:y,x1:x,y1:y}; draw();
});
window.addEventListener('mousemove',e=>{
  if(!drag) return; const [x,y]=pos(e);
  if(drag.type==='new'){ drag.x1=x; drag.y1=y; }
  else if(drag.type==='move'){ const o=drag.orig; let cx=o[0]+(x-drag.sx), cy=o[1]+(y-drag.sy); cx=Math.min(1-o[2]/2,Math.max(o[2]/2,cx)); cy=Math.min(1-o[3]/2,Math.max(o[3]/2,cy)); boxes[sel]=[cx,cy,o[2],o[3]]; dirty=true; }
  else if(drag.type==='resize'){ const a=drag.orig.slice(); if(drag.h[1]==='l'||drag.h==='tl'||drag.h==='bl'){}
    if(drag.h==='tl'){a[0]=x;a[1]=y;} if(drag.h==='tr'){a[2]=x;a[1]=y;} if(drag.h==='bl'){a[0]=x;a[3]=y;} if(drag.h==='br'){a[2]=x;a[3]=y;}
    boxes[sel]=fromXyxy(a); dirty=true; }
  draw();
});
window.addEventListener('mouseup',e=>{
  if(!drag) return;
  if(drag.type==='new'){ const w=Math.abs(drag.x1-drag.x0), h=Math.abs(drag.y1-drag.y0);
    if(w*cv.width>8 && h*cv.height>8){ pushUndo(); boxes.push(fromXyxy([drag.x0,drag.y0,drag.x1,drag.y1])); sel=boxes.length-1; dirty=true; } }
  drag=null; draw(); updateStatus();
});

function reviewedCount(){ return items.filter(i=>i.reviewed).length; }
function updateStatus(){
  const it=items[idx]; if(!it) return;
  $('title').textContent=it.name;
  $('count').textContent=(idx+1)+' / '+items.length+'   |   reviewed '+reviewedCount()+' / '+items.length;
  $('fill').style.width=(100*reviewedCount()/Math.max(1,items.length))+'%';
  $('status').innerHTML=(dirty?'<span class="no">unsaved changes</span> &middot; ':'')+(it.reviewed?'<span class="ok">&#10003; saved</span>':'<span class="no">not reviewed yet</span>')+' &middot; '+boxes.length+' box'+(boxes.length===1?'':'es');
}
function load(i){
  if(i<0||i>=items.length) return;
  idx=i; sel=-1; dirty=false; undoStack=[];
  api('/api/labels/'+encodeURIComponent(items[i].name)).then(d=>{ boxes=d.boxes; img.onload=()=>{fit();updateStatus();}; img.src='/api/image/'+encodeURIComponent(items[i].name)+'?t='+Date.now(); });
}
function guard(){ return !dirty || confirm('This image has unsaved changes. Leave without saving?'); }
function go(i){ if(guard()) load(i); }
function nextUnreviewed(){ for(let k=1;k<=items.length;k++){ const j=(idx+k)%items.length; if(!items[j].reviewed){ go(j); return; } } alert('Every image has been reviewed. Well done!'); }
function save(empty){
  const payload = empty ? [] : boxes;
  api('/api/labels/'+encodeURIComponent(items[idx].name),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({boxes:payload})})
   .then(r=>{ items[idx].reviewed=true; items[idx].n=r.saved; boxes=payload.slice(); dirty=false; updateStatus();
     let nxt=-1; for(let k=1;k<=items.length;k++){ const j=(idx+k)%items.length; if(!items[j].reviewed){nxt=j;break;} }
     if(nxt>=0) load(nxt); else { updateStatus(); alert('Every image has been reviewed. Well done!'); } })
   .catch(err=>alert('Could not save: '+err.message));
}
$('prev').onclick=()=>go(idx-1); $('next').onclick=()=>go(idx+1); $('nextun').onclick=nextUnreviewed;
$('del').onclick=()=>{ if(sel>=0){ pushUndo(); boxes.splice(sel,1); sel=-1; dirty=true; draw(); updateStatus(); } };
$('undo').onclick=doUndo; $('save').onclick=()=>save(false); $('none').onclick=()=>save(true);
window.addEventListener('keydown',e=>{
  if(e.ctrlKey&&e.key.toLowerCase()==='z'){ e.preventDefault(); doUndo(); return; }
  const k=e.key.toLowerCase();
  if(k==='s'||k==='enter'){ e.preventDefault(); save(false); }
  else if(k==='x'){ save(true); }
  else if(k==='a'||k==='arrowleft'){ go(idx-1); }
  else if(k==='d'||k==='arrowright'){ go(idx+1); }
  else if(k==='n'){ nextUnreviewed(); }
  else if(k==='delete'||k==='backspace'){ e.preventDefault(); $('del').onclick(); }
});
window.addEventListener('resize',fit);
api('/api/list').then(l=>{ items=l; if(!items.length){ $('title').textContent='No images found'; return; }
  let first=items.findIndex(i=>!i.reviewed); load(first>=0?first:0); });
</script></body></html>
"""


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass

    def _send(self, code, body, ctype):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _json(self, obj, code=200):
        self._send(code, json.dumps(obj).encode("utf-8"), "application/json")

    def _name(self, path, prefix):
        n = unquote(path[len(prefix):])
        return n if n in STORE.names() else None

    def do_GET(self):
        p = urlparse(self.path).path
        if p == "/":
            self._send(200, PAGE.encode("utf-8"), "text/html; charset=utf-8")
        elif p == "/api/list":
            self._json(STORE.listing())
        elif p.startswith("/api/labels/"):
            n = self._name(p, "/api/labels/")
            self._json({"boxes": STORE.read_boxes(n)}) if n else self._json({"error": "unknown image"}, 404)
        elif p.startswith("/api/image/"):
            n = self._name(p, "/api/image/")
            if not n:
                self._json({"error": "unknown image"}, 404)
                return
            with open(os.path.join(STORE.images_dir, n), "rb") as fh:
                data = fh.read()
            self._send(200, data, mimetypes.guess_type(n)[0] or "application/octet-stream")
        else:
            self._json({"error": "not found"}, 404)

    def do_POST(self):
        p = urlparse(self.path).path
        if not p.startswith("/api/labels/"):
            self._json({"error": "not found"}, 404)
            return
        n = self._name(p, "/api/labels/")
        if not n:
            self._json({"error": "unknown image"}, 404)
            return
        length = int(self.headers.get("Content-Length", 0))
        try:
            data = json.loads(self.rfile.read(length).decode("utf-8"))
            boxes = [[float(v) for v in b[:4]] for b in data["boxes"]]
        except Exception as exc:
            self._json({"error": "bad request: %s" % exc}, 400)
            return
        self._json({"ok": True, "saved": STORE.write_boxes(n, boxes)})


def main():
    global STORE, PAGE
    ap = argparse.ArgumentParser(description="Local eczema box review tool (stdlib only).")
    ap.add_argument("folder", help="one reviewer's folder containing images\\ and labels\\")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--no-browser", action="store_true")
    ap.add_argument("--label", default="eczema", help="condition name shown on the page (default: eczema), e.g. --label acne")
    a = ap.parse_args()
    PAGE = PAGE.replace("eczema", a.label.lower()).replace("Eczema", a.label.capitalize())
    STORE = Store(a.folder)
    srv = ThreadingHTTPServer(("127.0.0.1", a.port), Handler)
    url = "http://127.0.0.1:%d" % a.port
    n = len(STORE.names())
    done = sum(1 for x in STORE.listing() if x["reviewed"])
    print("Review folder : %s" % STORE.root)
    print("Images        : %d  (already reviewed: %d)" % (n, done))
    print("Open          : %s   (this computer only; nothing is uploaded)" % url)
    print("Stop          : press Ctrl+C in this window")
    if not a.no_browser:
        threading.Timer(0.6, lambda: webbrowser.open(url)).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped. Your saved labels are in: %s" % STORE.labels_dir)


if __name__ == "__main__":
    main()
