#!/usr/bin/env python
"""
lesion_box_tool.py - fast local box editor for hand-boxing lesions (PrecisionSkin detector test set).

Python standard library only (no installs).  Python 3.8 or newer.  Nothing is uploaded: it runs on this computer only.

Usage (Windows PowerShell, inside the SkinDevApp folder):
    py -3.13 lesion_box_tool.py "C:\\Users\\Bautista\\Documents\\PrecisionSkin_DetectorOwn\\Acne_TEST" --label acne --port 8800

The folder must contain  images\\  (the photos)  and  labels\\  (created if missing; YOLO .txt, class 0).
Your boxes are saved as YOLO labels. An image counts as REVIEWED only after you save it.

Made to be quick and consistent:
  * zoom (mouse wheel) and pan (hold SPACE and drag, or right-mouse drag)  -> tiny spots are easy to see
  * SHIFT+click = stamp a box with the SAME SIZE as your last box  -> one click per acne spot
  * E = contrast view (display only, the photo is never changed)   H = hide boxes to see the skin
  * live checks: too small / too large / overlapping / unusual size compared with your other boxes (red dashed)
  * V = review sheet (thumbnails of finished photos with their boxes) to spot mistakes
  * automatic save when you move to another photo; every action is timed in labeling_log.csv

Keys
  S / Enter   save + next unreviewed          X   no lesion visible (saves empty) + next
  A / D       previous / next photo            N   next unreviewed
  Delete      delete selected box              Tab / Shift+Tab   select next / previous box
  Ctrl+Z / Ctrl+Y   undo / redo                Arrow keys   move selected box (Shift = resize it)
  E contrast   H hide boxes   0 fit   + / -  zoom   V review sheet   G guidelines   Esc close panels
Mouse
  drag on empty space = new box | click a box = select | drag it = move | drag a handle = resize
  SHIFT+click on empty space = stamp last-size box | wheel = zoom | SPACE+drag or right-drag = pan
"""
import argparse
import csv
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

RULES = {
    "acne": "One TIGHT box per spot (papule, pustule, comedo, nodule): the whole raised/red spot, not the skin around it. "
            "Touching spots get separate boxes when you can tell them apart. Do not box scars, freckles, moles or dark marks. "
            "No visible acne: press X.",
    "hyperpigmentation": "One box per PATCH of darker skin (melasma, sun spots, post-acne marks): edge to edge around the patch, "
                         "not a big region of the face. Separate patches = separate boxes. No visible patch: press X.",
    "eczema": "One box per PATCH of eczema (red, scaly, dry or inflamed skin): around the patch, not the whole face. "
              "Separate patches = separate boxes. No visible eczema: press X.",
}


class Store:
    def __init__(self, root):
        self.root = os.path.abspath(root)
        self.images_dir = os.path.join(self.root, "images")
        self.labels_dir = os.path.join(self.root, "labels")
        if not os.path.isdir(self.images_dir):
            sys.exit("No 'images' folder in: " + self.root)
        os.makedirs(self.labels_dir, exist_ok=True)
        self.meta_path = os.path.join(os.path.dirname(self.root), "_drafts", "meta.json")
        self.log_path = os.path.join(self.root, "labeling_log.csv")
        self.lock = threading.Lock()

    @property
    def drafted_at(self):
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
        return [{"name": n, "reviewed": self.is_reviewed(n), "n": len(self.read_boxes(n))} for n in self.names()]

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
            now = time.time()
            os.utime(p, (now, now))
        return len(clean)

    def log(self, row):
        new = not os.path.exists(self.log_path)
        with self.lock:
            with open(self.log_path, "a", encoding="utf-8", newline="") as fh:
                w = csv.writer(fh)
                if new:
                    w.writerow(["timestamp", "image", "action", "n_boxes", "seconds"])
                w.writerow([time.strftime("%Y-%m-%d %H:%M:%S"), row.get("image", ""), row.get("action", ""), row.get("n", ""), row.get("seconds", "")])


STORE = None
LABEL = "lesion"
RULE = ""

PAGE = r"""<!doctype html>
<html><head><meta charset="utf-8"><title>Lesion box tool - {{LABEL}}</title>
<style>
 *{box-sizing:border-box}
 html,body{height:100%;margin:0}
 body{display:flex;flex-direction:column;font-family:Segoe UI,Arial,sans-serif;background:#12191d;color:#e9eef0;overflow:hidden}
 #top{display:flex;gap:8px;align-items:center;padding:6px 10px;background:#0b1114;flex-wrap:wrap}
 #top b{font-size:14px}
 button{background:#26343b;color:#e9eef0;border:1px solid #455862;border-radius:6px;padding:5px 10px;font-size:12px;cursor:pointer}
 button:hover{background:#34505c} button.primary{background:#2f7d4f;border-color:#3f9a64} button.warn{background:#7a4b1f;border-color:#a56a2f}
 button.on{background:#3b6ea5;border-color:#5b8fc7}
 #bar{height:6px;background:#26343b}
 #fill{height:100%;width:0;background:#3f9a64}
 #info{display:flex;gap:16px;padding:3px 10px;font-size:12px;color:#aebcc3;background:#0e1519;flex-wrap:wrap}
 #stage{flex:1;position:relative;min-height:0;background:#0c1114}
 #cv{position:absolute;left:0;top:0;cursor:crosshair}
 #warn{position:absolute;right:8px;top:8px;max-width:340px;font-size:12px;z-index:5}
 .w{background:rgba(120,30,30,.92);border:1px solid #c35;border-radius:6px;padding:4px 8px;margin-bottom:4px}
 .good{background:rgba(30,100,60,.92);border:1px solid #3a7;border-radius:6px;padding:4px 8px;margin-bottom:4px}
 #rule{position:absolute;left:8px;bottom:8px;max-width:520px;background:rgba(10,18,22,.93);border:1px solid #455862;border-radius:8px;padding:8px 12px;font-size:12px;line-height:1.45;display:none;z-index:6}
 #sheet{position:absolute;inset:0;background:rgba(8,12,15,.97);display:none;z-index:9;overflow:auto;padding:10px}
 #grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:8px}
 .card{background:#1a252b;border:1px solid #33454f;border-radius:6px;padding:4px;cursor:pointer}
 .card canvas{width:100%;display:block}
 .card div{font-size:11px;color:#aebcc3;padding:2px}
 #help{font-size:11px;color:#8fa1aa;padding:3px 10px;background:#0b1114}
 kbd{background:#26343b;border:1px solid #455862;border-radius:4px;padding:0 4px;font-size:10px}
 #status{margin-left:auto;font-size:12px} .ok{color:#7fe3a2} .no{color:#f5c26b}
</style></head>
<body>
<div id="top">
  <b>{{LABEL}}</b> <span id="title"></span>
  <button id="prev">&larr; Prev (A)</button><button id="next">Next (D) &rarr;</button><button id="nextun">Next unreviewed (N)</button>
  <button id="del">Delete (Del)</button><button id="undo">Undo</button><button id="redo">Redo</button>
  <button id="hide">Hide boxes (H)</button><button id="enh">Contrast (E)</button><button id="fit">Fit (0)</button>
  <button id="sheetb">Review sheet (V)</button><button id="ruleb">Guidelines (G)</button>
  <button id="none" class="warn">No lesion (X)</button><button id="save" class="primary">Save + next (S)</button>
  <span id="status"></span>
</div>
<div id="bar"><div id="fill"></div></div>
<div id="info"><span id="count"></span><span id="eta"></span><span id="size"></span></div>
<div id="stage"><canvas id="cv"></canvas><div id="warn"></div><div id="rule"></div><div id="sheet"><div style="margin-bottom:8px"><b>Review sheet</b> &middot; click a photo to open it &middot; <button id="sp">&lt; page</button> <button id="sn">page &gt;</button> <span id="spage"></span> <button id="sx">Close (Esc)</button></div><div id="grid"></div></div></div>
<div id="help">Wheel = zoom &middot; Space+drag / right-drag = pan &middot; drag = new box &middot; <b>Shift+click = stamp last-size box</b> &middot; click box = select/move/resize &middot; S = save+next &middot; X = no lesion &middot; E contrast &middot; H hide &middot; V review sheet</div>
<script>
const LABEL="{{LABEL}}", RULE={{RULE_JSON}};
let items=[], idx=0, boxes=[], sel=-1, dirty=false, drag=null, undo=[], redo=[];
let img=new Image(), iw=1, ih=1, view={s:1,ox:0,oy:0}, fitS=1;
let hideBoxes=false, enhance=false, lastSize=[0.03,0.03], spaceDown=false, tShow=Date.now(), recent=[], sizes=[], sheetPage=0;
const $=id=>document.getElementById(id), cv=$('cv'), ctx=cv.getContext('2d');
const HUGE = LABEL==='acne' ? 0.25 : 0.7;

function api(path,opt){ return fetch(path,opt).then(r=>{ if(!r.ok) throw new Error(path+' '+r.status); return r.json(); }); }
function logEv(action,n,secs){ fetch('/api/log',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({image:items[idx]?items[idx].name:'',action:action,n:n,seconds:secs})}).catch(()=>{}); }
function resize(){ const r=$('stage').getBoundingClientRect(); cv.width=Math.max(50,Math.floor(r.width)); cv.height=Math.max(50,Math.floor(r.height)); if(iw>1) keepView(); draw(); }
function fitView(){ fitS=Math.min(cv.width/iw, cv.height/ih)*0.98; view.s=fitS; view.ox=(cv.width-iw*view.s)/2; view.oy=(cv.height-ih*view.s)/2; }
function keepView(){ const old=fitS; fitS=Math.min(cv.width/iw, cv.height/ih)*0.98; if(Math.abs(old-view.s)<1e-9){ fitView(); } }
function toCv(nx,ny){ return [view.ox+nx*iw*view.s, view.oy+ny*ih*view.s]; }
function toImg(px,py){ return [(px-view.ox)/(iw*view.s), (py-view.oy)/(ih*view.s)]; }
function mpos(e){ const r=cv.getBoundingClientRect(); return [e.clientX-r.left, e.clientY-r.top]; }
function clamp(v){ return Math.min(1,Math.max(0,v)); }
function xyxy(b){ return [b[0]-b[2]/2,b[1]-b[3]/2,b[0]+b[2]/2,b[1]+b[3]/2]; }
function fromXyxy(a){ const x0=clamp(Math.min(a[0],a[2])),x1=clamp(Math.max(a[0],a[2])),y0=clamp(Math.min(a[1],a[3])),y1=clamp(Math.max(a[1],a[3])); return [(x0+x1)/2,(y0+y1)/2,x1-x0,y1-y0]; }
function iouB(a,b){ const p=xyxy(a), q=xyxy(b); const iw_=Math.max(0,Math.min(p[2],q[2])-Math.max(p[0],q[0])), ih_=Math.max(0,Math.min(p[3],q[3])-Math.max(p[1],q[1])); const i=iw_*ih_, u=a[2]*a[3]+b[2]*b[3]-i; return u>0?i/u:0; }
function median(a){ if(!a.length) return 0; const s=a.slice().sort((x,y)=>x-y); return s[Math.floor(s.length/2)]; }

function qc(){
  const out=[]; const med=median(sizes);
  boxes.forEach((b,i)=>{
    const wp=b[2]*iw, hp=b[3]*ih;
    if(Math.min(wp,hp)<6) out.push([i,'box '+(i+1)+' is very small ('+Math.round(wp)+'x'+Math.round(hp)+' px)']);
    else if(b[2]>HUGE||b[3]>HUGE) out.push([i,'box '+(i+1)+' is very large for '+LABEL+' ('+Math.round(100*Math.max(b[2],b[3]))+'% of the photo)']);
    else if(sizes.length>=15){ const sz=Math.sqrt(b[2]*b[3]); if(sz>4*med||sz<med/4) out.push([i,'box '+(i+1)+' is unusual compared with your other boxes']); }
    for(let j=0;j<i;j++){ if(iouB(b,boxes[j])>0.6) out.push([i,'boxes '+(j+1)+' and '+(i+1)+' overlap a lot (duplicate?)']); }
  });
  return out;
}
function draw(){
  ctx.clearRect(0,0,cv.width,cv.height); ctx.fillStyle='#0c1114'; ctx.fillRect(0,0,cv.width,cv.height);
  if(!img.complete||!img.naturalWidth) return;
  ctx.imageSmoothingEnabled = view.s<3; ctx.imageSmoothingQuality='high';
  if(enhance) ctx.filter='contrast(1.25) saturate(1.1) brightness(1.04)';
  ctx.drawImage(img,view.ox,view.oy,iw*view.s,ih*view.s); ctx.filter='none';
  const bad=new Set(qc().map(x=>x[0]));
  if(!hideBoxes) boxes.forEach((b,i)=>{
    const a=xyxy(b), p0=toCv(a[0],a[1]), p1=toCv(a[2],a[3]); const W=p1[0]-p0[0], H=p1[1]-p0[1];
    const isSel=i===sel, isBad=bad.has(i);
    ctx.setLineDash(isBad?[6,4]:[]); ctx.lineWidth=isSel?3:2; ctx.strokeStyle=isSel?'#ffe14d':(isBad?'#ff5a5a':'#ff9a1f');
    ctx.fillStyle=isSel?'rgba(255,225,77,.16)':'rgba(255,154,31,.08)'; ctx.fillRect(p0[0],p0[1],W,H); ctx.strokeRect(p0[0],p0[1],W,H); ctx.setLineDash([]);
    ctx.fillStyle=isSel?'#ffe14d':'#ffb35c'; ctx.font='11px Segoe UI'; ctx.fillText(String(i+1),p0[0]+2,Math.max(10,p0[1]-3));
    if(isSel){ ctx.fillStyle='#ffe14d'; handles(b).forEach(h=>ctx.fillRect(h[1]-4,h[2]-4,8,8)); }
  });
  if(drag&&drag.type==='new'){ ctx.setLineDash([6,4]); ctx.strokeStyle='#7fe3a2'; ctx.lineWidth=2; ctx.strokeRect(Math.min(drag.x0,drag.x1),Math.min(drag.y0,drag.y1),Math.abs(drag.x1-drag.x0),Math.abs(drag.y1-drag.y0)); ctx.setLineDash([]); }
  const w=qc(); $('warn').innerHTML = w.length ? w.slice(0,5).map(x=>'<div class="w">&#9888; '+x[1]+'</div>').join('') : (boxes.length?'<div class="good">&#10003; checks OK</div>':'');
}
function handles(b){ const a=xyxy(b), x=[a[0],(a[0]+a[2])/2,a[2]], y=[a[1],(a[1]+a[3])/2,a[3]]; const n=['tl','t','tr','l','','r','bl','b','br']; const r=[]; for(let j=0;j<3;j++) for(let i=0;i<3;i++){ const k=n[j*3+i]; if(k){ const p=toCv(x[i],y[j]); r.push([k,p[0],p[1]]); } } return r; }
function hitHandle(i,px,py){ for(const h of handles(boxes[i])){ if(Math.abs(px-h[1])<=9&&Math.abs(py-h[2])<=9) return h[0]; } return null; }
function hitBox(px,py){ const [x,y]=toImg(px,py); let best=-1,bestA=1e9; boxes.forEach((b,i)=>{ const a=xyxy(b); if(x>=a[0]&&x<=a[2]&&y>=a[1]&&y<=a[3]){ const ar=b[2]*b[3]; if(ar<bestA){bestA=ar;best=i;} } }); return best; }
function pushUndo(){ undo.push(JSON.stringify(boxes)); if(undo.length>80) undo.shift(); redo=[]; }
function doUndo(){ if(!undo.length) return; redo.push(JSON.stringify(boxes)); boxes=JSON.parse(undo.pop()); sel=-1; dirty=true; draw(); status(); }
function doRedo(){ if(!redo.length) return; undo.push(JSON.stringify(boxes)); boxes=JSON.parse(redo.pop()); sel=-1; dirty=true; draw(); status(); }
function addBox(b){ pushUndo(); boxes.push(b); sel=boxes.length-1; lastSize=[b[2],b[3]]; dirty=true; draw(); status(); }

cv.addEventListener('contextmenu',e=>e.preventDefault());
cv.addEventListener('mousedown',e=>{
  const [px,py]=mpos(e);
  if(e.button===1||e.button===2||spaceDown){ drag={type:'pan',sx:px,sy:py,ox:view.ox,oy:view.oy}; cv.style.cursor='grabbing'; return; }
  if(e.button!==0) return;
  if(sel>=0){ const h=hitHandle(sel,px,py); if(h){ pushUndo(); drag={type:'resize',h:h,orig:xyxy(boxes[sel])}; return; } }
  const hit=hitBox(px,py);
  if(hit>=0){ sel=hit; pushUndo(); const [x,y]=toImg(px,py); drag={type:'move',sx:x,sy:y,orig:boxes[hit].slice()}; draw(); status(); return; }
  if(e.shiftKey){ const [x,y]=toImg(px,py); if(x>=0&&x<=1&&y>=0&&y<=1){ const w=lastSize[0],h=lastSize[1]; addBox([Math.min(1-w/2,Math.max(w/2,x)),Math.min(1-h/2,Math.max(h/2,y)),w,h]); } return; }
  sel=-1; drag={type:'new',x0:px,y0:py,x1:px,y1:py}; draw();
});
window.addEventListener('mousemove',e=>{
  if(!drag) return; const [px,py]=mpos(e);
  if(drag.type==='pan'){ view.ox=drag.ox+(px-drag.sx); view.oy=drag.oy+(py-drag.sy); }
  else if(drag.type==='new'){ drag.x1=px; drag.y1=py; }
  else if(drag.type==='move'){ const [x,y]=toImg(px,py); const o=drag.orig; let cx=o[0]+(x-drag.sx), cy=o[1]+(y-drag.sy); cx=Math.min(1-o[2]/2,Math.max(o[2]/2,cx)); cy=Math.min(1-o[3]/2,Math.max(o[3]/2,cy)); boxes[sel]=[cx,cy,o[2],o[3]]; dirty=true; }
  else if(drag.type==='resize'){ const [x,y]=toImg(px,py); const a=drag.orig.slice(), h=drag.h;
    if(h.includes('l')) a[0]=x; if(h.includes('r')) a[2]=x; if(h.includes('t')) a[1]=y; if(h.includes('b')) a[3]=y;
    boxes[sel]=fromXyxy(a); dirty=true; }
  draw();
});
window.addEventListener('mouseup',e=>{
  if(!drag) return; cv.style.cursor=spaceDown?'grab':'crosshair';
  if(drag.type==='new'){ const w=Math.abs(drag.x1-drag.x0), h=Math.abs(drag.y1-drag.y0);
    if(w>6&&h>6){ const a=toImg(drag.x0,drag.y0), b=toImg(drag.x1,drag.y1); addBox(fromXyxy([a[0],a[1],b[0],b[1]])); } }
  else if(drag.type==='move'||drag.type==='resize'){ if(sel>=0) lastSize=[boxes[sel][2],boxes[sel][3]]; }
  drag=null; draw(); status();
});
cv.addEventListener('wheel',e=>{ e.preventDefault(); const [mx,my]=mpos(e); const f=e.deltaY<0?1.2:1/1.2; const ns=Math.min(fitS*16,Math.max(fitS*0.6,view.s*f));
  view.ox=mx-(mx-view.ox)*(ns/view.s); view.oy=my-(my-view.oy)*(ns/view.s); view.s=ns; draw(); },{passive:false});

function reviewedCount(){ return items.filter(i=>i.reviewed).length; }
function status(){
  const it=items[idx]; if(!it) return;
  $('title').textContent=it.name;
  const rc=reviewedCount(); $('count').textContent='photo '+(idx+1)+' / '+items.length+'  |  reviewed '+rc+' / '+items.length;
  $('fill').style.width=(100*rc/Math.max(1,items.length))+'%';
  $('status').innerHTML=(dirty?'<span class="no">unsaved</span> &middot; ':'')+(it.reviewed?'<span class="ok">&#10003; saved</span>':'<span class="no">not reviewed</span>')+' &middot; '+boxes.length+' box'+(boxes.length===1?'':'es');
  const avg=recent.length?recent.reduce((a,b)=>a+b,0)/recent.length:0; const left=items.length-rc;
  $('eta').textContent=recent.length?('avg '+avg.toFixed(0)+' s / photo  |  about '+Math.ceil(avg*left/60)+' min left'):'';
  $('size').textContent=sizes.length?('your median box: '+Math.round(median(sizes)*Math.sqrt(iw*ih))+' px'):'';
  $('hide').classList.toggle('on',hideBoxes); $('enh').classList.toggle('on',enhance);
}
function load(i){
  if(i<0||i>=items.length) return;
  idx=i; sel=-1; dirty=false; undo=[]; redo=[]; tShow=Date.now();
  api('/api/labels/'+encodeURIComponent(items[i].name)).then(d=>{ boxes=d.boxes;
    img=new Image(); img.onload=()=>{ iw=img.naturalWidth; ih=img.naturalHeight; fitView(); draw(); status(); }; img.src='/api/image/'+encodeURIComponent(items[i].name)+'?t='+Date.now(); });
}
function persist(payload,action){
  const secs=Math.min(300,(Date.now()-tShow)/1000);
  return api('/api/labels/'+encodeURIComponent(items[idx].name),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({boxes:payload})})
    .then(r=>{ items[idx].reviewed=true; items[idx].n=r.saved; boxes=payload.slice(); dirty=false; payload.forEach(b=>sizes.push(Math.sqrt(b[2]*b[3]))); if(sizes.length>400) sizes=sizes.slice(-400);
      if(!items[idx].timed){ items[idx].timed=true; recent.push(secs); if(recent.length>12) recent.shift(); } logEv(action,r.saved,secs.toFixed(1)); status(); });
}
function go(i){ if(i<0||i>=items.length) return; if(dirty){ persist(boxes,'autosave').then(()=>load(i)).catch(err=>alert('Could not save: '+err.message)); } else load(i); }
function nextUn(from){ for(let k=1;k<=items.length;k++){ const j=(from+k)%items.length; if(!items[j].reviewed) return j; } return -1; }
function nextUnreviewed(){ const j=nextUn(idx); if(j>=0) go(j); else alert('Every photo has been reviewed. Well done!'); }
function save(empty){ persist(empty?[]:boxes,empty?'none':'save').then(()=>{ const j=nextUn(idx); if(j>=0) load(j); else { status(); alert('Every photo has been reviewed. Well done!'); } }).catch(err=>alert('Could not save: '+err.message)); }
function delSel(){ if(sel>=0){ pushUndo(); boxes.splice(sel,1); sel=-1; dirty=true; draw(); status(); } }
function zoomBy(f){ const mx=cv.width/2,my=cv.height/2; const ns=Math.min(fitS*16,Math.max(fitS*0.6,view.s*f)); view.ox=mx-(mx-view.ox)*(ns/view.s); view.oy=my-(my-view.oy)*(ns/view.s); view.s=ns; draw(); }

// review sheet
function drawCard(cvs,name,boxesN){ const im=new Image(); im.onload=()=>{ const w=210,h=Math.round(210*im.naturalHeight/im.naturalWidth); cvs.width=w; cvs.height=h; const c=cvs.getContext('2d'); c.drawImage(im,0,0,w,h); c.strokeStyle='#ff9a1f'; c.lineWidth=1.5; boxesN.forEach(b=>{ const a=xyxy(b); c.strokeRect(a[0]*w,a[1]*h,(a[2]-a[0])*w,(a[3]-a[1])*h); }); }; im.src='/api/image/'+encodeURIComponent(name); }
function openSheet(){ const list=items.map((it,i)=>[it,i]).filter(x=>x[0].reviewed); $('sheet').style.display='block'; const per=24, pages=Math.max(1,Math.ceil(list.length/per)); sheetPage=Math.min(sheetPage,pages-1);
  $('spage').textContent='page '+(sheetPage+1)+' / '+pages+' ('+list.length+' reviewed photos)'; const g=$('grid'); g.innerHTML='';
  list.slice(sheetPage*per,(sheetPage+1)*per).forEach(([it,i])=>{ const d=document.createElement('div'); d.className='card'; const c=document.createElement('canvas'); const t=document.createElement('div'); t.textContent=(i+1)+' | '+it.n+' box'+(it.n===1?'':'es'); d.appendChild(c); d.appendChild(t);
    d.onclick=()=>{ $('sheet').style.display='none'; go(i); }; g.appendChild(d); api('/api/labels/'+encodeURIComponent(it.name)).then(r=>drawCard(c,it.name,r.boxes)); }); }
$('sp').onclick=()=>{ sheetPage=Math.max(0,sheetPage-1); openSheet(); }; $('sn').onclick=()=>{ sheetPage++; openSheet(); }; $('sx').onclick=()=>{ $('sheet').style.display='none'; };
function toggleRule(){ const r=$('rule'); r.style.display=r.style.display==='block'?'none':'block'; }
$('rule').innerHTML='<b>Rule for '+LABEL+':</b> '+RULE+'<br><span style="color:#8fa1aa">Use the SAME rule for every photo. If unsure, box what a careful person would call one lesion.</span>';

$('prev').onclick=()=>go(idx-1); $('next').onclick=()=>go(idx+1); $('nextun').onclick=nextUnreviewed; $('del').onclick=delSel;
$('undo').onclick=doUndo; $('redo').onclick=doRedo; $('save').onclick=()=>save(false); $('none').onclick=()=>save(true);
$('hide').onclick=()=>{ hideBoxes=!hideBoxes; draw(); status(); }; $('enh').onclick=()=>{ enhance=!enhance; draw(); status(); }; $('fit').onclick=()=>{ fitView(); draw(); };
$('sheetb').onclick=openSheet; $('ruleb').onclick=toggleRule;
window.addEventListener('keydown',e=>{
  if(e.target&&/INPUT|TEXTAREA/.test(e.target.tagName)) return;
  const k=e.key.toLowerCase();
  if(e.ctrlKey&&k==='z'){ e.preventDefault(); doUndo(); return; } if(e.ctrlKey&&k==='y'){ e.preventDefault(); doRedo(); return; }
  if(e.key===' '){ e.preventDefault(); spaceDown=true; cv.style.cursor='grab'; return; }
  if(k==='escape'){ $('sheet').style.display='none'; $('rule').style.display='none'; return; }
  if(k==='s'||k==='enter'){ e.preventDefault(); save(false); }
  else if(k==='x'){ save(true); }
  else if(k==='a'){ go(idx-1); } else if(k==='d'){ go(idx+1); }
  else if(k==='n'){ nextUnreviewed(); }
  else if(k==='delete'||k==='backspace'){ e.preventDefault(); delSel(); }
  else if(k==='h'){ hideBoxes=!hideBoxes; draw(); status(); }
  else if(k==='e'){ enhance=!enhance; draw(); status(); }
  else if(k==='0'){ fitView(); draw(); }
  else if(k==='+'||k==='='){ zoomBy(1.25); } else if(k==='-'){ zoomBy(1/1.25); }
  else if(k==='v'){ openSheet(); } else if(k==='g'){ toggleRule(); }
  else if(k==='tab'){ e.preventDefault(); if(boxes.length){ sel=(sel+(e.shiftKey?-1:1)+boxes.length)%boxes.length; draw(); status(); } }
  else if(k.startsWith('arrow')){
    if(sel>=0){ e.preventDefault(); const dx=(k==='arrowleft'?-1:k==='arrowright'?1:0)/(iw*view.s), dy=(k==='arrowup'?-1:k==='arrowdown'?1:0)/(ih*view.s); pushUndo();
      if(e.shiftKey){ const b=boxes[sel]; boxes[sel]=fromXyxy([b[0]-b[2]/2,b[1]-b[3]/2,b[0]+b[2]/2+dx,b[1]+b[3]/2+dy]); } else { const b=boxes[sel]; boxes[sel]=[clamp(b[0]+dx),clamp(b[1]+dy),b[2],b[3]]; }
      lastSize=[boxes[sel][2],boxes[sel][3]]; dirty=true; draw(); status(); }
    else if(k==='arrowleft'){ go(idx-1); } else if(k==='arrowright'){ go(idx+1); }
  }
});
window.addEventListener('keyup',e=>{ if(e.key===' '){ spaceDown=false; cv.style.cursor='crosshair'; } });
window.addEventListener('resize',()=>{ const r=$('stage').getBoundingClientRect(); cv.width=Math.max(50,Math.floor(r.width)); cv.height=Math.max(50,Math.floor(r.height)); if(iw>1) fitView(); draw(); });
window.addEventListener('beforeunload',e=>{ if(dirty){ e.preventDefault(); e.returnValue=''; } });
(function init(){ const r=$('stage').getBoundingClientRect(); cv.width=Math.max(50,Math.floor(r.width)); cv.height=Math.max(50,Math.floor(r.height));
  api('/api/list').then(l=>{ items=l; if(!items.length){ $('title').textContent='No images found'; return; } const f=items.findIndex(i=>!i.reviewed); load(f>=0?f:0); }); })();
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
            page = PAGE.replace("{{LABEL}}", LABEL).replace("{{RULE_JSON}}", json.dumps(RULE))
            self._send(200, page.encode("utf-8"), "text/html; charset=utf-8")
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
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length).decode("utf-8") if length else "{}"
        if p == "/api/log":
            try:
                STORE.log(json.loads(raw))
            except Exception:
                pass
            self._json({"ok": True})
            return
        if not p.startswith("/api/labels/"):
            self._json({"error": "not found"}, 404)
            return
        n = self._name(p, "/api/labels/")
        if not n:
            self._json({"error": "unknown image"}, 404)
            return
        try:
            data = json.loads(raw)
            boxes = [[float(v) for v in b[:4]] for b in data["boxes"]]
        except Exception as exc:
            self._json({"error": "bad request: %s" % exc}, 400)
            return
        self._json({"ok": True, "saved": STORE.write_boxes(n, boxes)})


def main():
    global STORE, LABEL, RULE
    ap = argparse.ArgumentParser(description="Fast local lesion box editor (stdlib only).")
    ap.add_argument("folder", help="folder containing images\\ and labels\\")
    ap.add_argument("--label", default="acne", help="condition shown on the page and used for the default rule (acne, hyperpigmentation, eczema)")
    ap.add_argument("--rule", default=None, help="your own labelling rule text (default: a rule for the chosen condition)")
    ap.add_argument("--port", type=int, default=8800)
    ap.add_argument("--no-browser", action="store_true")
    a = ap.parse_args()
    LABEL = a.label.lower()
    RULE = a.rule or RULES.get(LABEL, "One tight box per visible lesion. If you cannot see any, press X.")
    STORE = Store(a.folder)
    srv = ThreadingHTTPServer(("127.0.0.1", a.port), Handler)
    url = "http://127.0.0.1:%d" % a.port
    n = len(STORE.names())
    done = sum(1 for x in STORE.listing() if x["reviewed"])
    print("Folder  : %s" % STORE.root)
    print("Photos  : %d  (already reviewed: %d)" % (n, done))
    print("Rule    : %s" % RULE)
    print("Open    : %s   (this computer only; nothing is uploaded)" % url)
    print("Log     : %s" % STORE.log_path)
    print("Stop    : press Ctrl+C in this window")
    if not a.no_browser:
        threading.Timer(0.6, lambda: webbrowser.open(url)).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped. Your labels are in: %s" % STORE.labels_dir)


if __name__ == "__main__":
    main()
