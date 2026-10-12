# keeps tonight's hyperpigmentation experiment queue running even if WSL crashes ("Catastrophic failure"): checks every 2 minutes,
# restarts WSL if it is broken and relaunches the (resumable) queue. Stops when the queue writes ALL_DONE.
$log = "$env:USERPROFILE\Downloads\keep_queue_alive.log"
$job = "cd ~/PrecisionSkinV1; setsid nohup bash /mnt/c/Users/Bautista/source/repos/SkinDevApp/ml/scripts/run_hyp_tonight.sh > /dev/null 2>&1 < /dev/null & sleep 5"
while ($true) {
    $ok = $null; try { $ok = (& wsl.exe -e bash -lc "echo ok" 2>$null) } catch {}
    if ($ok -notmatch "ok") {
        Add-Content $log "$(Get-Date -Format s) WSL not responding -> wsl --shutdown"; & wsl.exe --shutdown; Start-Sleep -Seconds 10; continue
    }
    $done = (& wsl.exe -e bash -lc "tail -n 3 ~/PrecisionSkinV1/lesion_experiments/queue_hyp_tonight.log 2>/dev/null | grep -c ALL_DONE") 2>$null
    if ($done -match "^1") { Add-Content $log "$(Get-Date -Format s) queue finished"; break }
    $n = (& wsl.exe -e bash -lc "ps aux | grep -c '[l]esion_exp.py run'") 2>$null
    if ([int]($n -replace "\D", "0") -lt 1) { Add-Content $log "$(Get-Date -Format s) queue not running -> relaunch"; & wsl.exe -e bash -lc $job }
    Start-Sleep -Seconds 120
}
