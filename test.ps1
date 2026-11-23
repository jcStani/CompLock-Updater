# Builds the app and checks it:
#  1. the same selections through the site's Pack Builder script, pack.js (in headless Edge or Chrome), and through the app give the same
#     gameinfo.gi, mods.txt, paks, download size and estimate;
#  2. installs from the live site into a made-up game folder: first install, a game update that replaces gameinfo.gi,
#     a mod that is out of date (one row's update, and Apply leaving it alone), a file changed by hand, another mod
#     under a name the pack needs, a pak that is there under another number, an option turned off, and removal;
#     and next to mod managers: their search paths in gameinfo.gi, their records of a file that has to move, a folder
#     they renumbered, another copy of one of the pack's mods.
#   powershell -ExecutionPolicy Bypass -File test.ps1 [-Work <folder>] [-Browser <chrome or edge exe>] [-PackJs <file>]
param([string]$Work = (Join-Path $env:TEMP "complock-test"), [string]$Browser = "", [string]$PackJs = "")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $Work "CompLock.exe"
$fails = 0
function Check-That([string]$what, [bool]$ok) { if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:fails++ } }
function Run-App([string[]]$a) {
    $o = Join-Path $Work "out.txt"; $e = Join-Path $Work "err.txt"
    $p = Start-Process -FilePath $exe -ArgumentList $a -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $o -RedirectStandardError $e
    $script:code = $p.ExitCode
    (@(Get-Content $o -ErrorAction SilentlyContinue) + @(Get-Content $e -ErrorAction SilentlyContinue)) -join "`n"
}

if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force $Work | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "build.ps1") $exe
if ($LASTEXITCODE) { exit 1 }
$env:COMPLOCK_HOME = Join-Path $Work "home"

# ---------- the live site's pack data
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$html = (Invoke-WebRequest -Uri "https://deadlockconfig.com/" -UseBasicParsing).Content
$m = [regex]::Match($html, '<script id="pack-data" type="application/json">(.*?)</script>', 'Singleline')
if (-not $m.Success) { throw "the site's main page has no pack data" }
$data = Join-Path $Work "pack-data.json"
[IO.File]::WriteAllText($data, $m.Groups[1].Value, (New-Object Text.UTF8Encoding $false))

# ---------- 1: pack.js against the app
"pack.js against the app"
$codes = @("@default", "none", "soundcap,particles:stock", "hud:vanilla", "hud:hidden,cloth,fov:0", "ragdolls", "ticklines,fov:1.85",
    "hud:vanilla,soundcap,particles:reduced,lowdetailmap,brighterlighting,cloth,ragdolls,hpbars,ticklines,fov:2.60",
    "particles:nosuch,unknown,ragdolls:off,fov:9", "hpbars,ticklines,cloth,particles:reduced")
# the Pack Builder's script: the site's own copy, or a local one given with -PackJs
if (-not $PackJs) { $PackJs = Join-Path $Work "pack.js"; Invoke-WebRequest -Uri "https://deadlockconfig.com/docs/pack/pack.js" -UseBasicParsing -OutFile $PackJs }
$js = [IO.File]::ReadAllText($PackJs, [Text.Encoding]::UTF8)
$from = $js.IndexOf('const D = JSON.parse'); $to = $js.IndexOf('// ---------- the zip')
if ($from -lt 0 -or $to -lt 0) { throw "pack.js doesn't have the parts this test cuts out" }
$driver = @'
  const out = {};
  for (const code of CODES) {
    sel = code === "@default" ? withBase(Object.fromEntries(D.option.filter(o => o.on).map(o => [o.id, o.choice ? defaultChoice(o).id : true]))) : decode(code === "none" ? "" : code);
    const r = resolve(), e = estimate();
    out[code] = { code: encode(), paks: r.paks.map(p => p.file).join(","), down: r.down, est: signed(e.avg) + " " + signed(e.low),
      gameinfo: gameinfo(D.gameinfo, r.lines, r.off), mods: modsTxt(r) };
  }
  document.getElementById("out").textContent = "[[" + btoa(unescape(encodeURIComponent(JSON.stringify(out)))) + "]]";
'@
$page = '<!doctype html><meta charset="utf-8"><div id="pack-app"></div><pre id="out"></pre><script id="pack-data" type="application/json">' + $m.Groups[1].Value +
    '</script><script>(() => { const location = { origin: "https://deadlockconfig.com", search: "" }; const changed = () => {}; const CODES = ' + ($codes | ConvertTo-Json -Compress) + '; ' +
    $js.Substring($from, $to - $from) + $driver + '})();</script>'
$pageFile = Join-Path $Work "packjs.html"
[IO.File]::WriteAllText($pageFile, $page, (New-Object Text.UTF8Encoding $false))
$found = @($Browser, "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe")
foreach ($hive in "HKCU:", "HKLM:") { foreach ($n in "chrome.exe", "msedge.exe") {
    $found += (Get-ItemProperty "$hive\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\$n" -ErrorAction SilentlyContinue).'(default)' } }
$browser = $found | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $browser) { "  skipped: no Edge or Chrome to run pack.js in"; $fails++ }
else {
    $dom = Join-Path $Work "dom.txt"
    Start-Process -FilePath $browser -Wait -WindowStyle Hidden -RedirectStandardOutput $dom -RedirectStandardError (Join-Path $Work "browser-err.txt") -ArgumentList @(
        "--headless=new", "--disable-gpu", "--no-first-run", "--user-data-dir=`"$(Join-Path $Work 'browser')`"", "--virtual-time-budget=5000", "--dump-dom", "`"file:///$($pageFile -replace '\\','/')`"")
    $b64 = [regex]::Match([IO.File]::ReadAllText($dom), '\[\[([A-Za-z0-9+/=]+)\]\]')
    if (-not $b64.Success) { throw "pack.js didn't run in $browser (see $dom)" }
    $ref = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64.Groups[1].Value)) | ConvertFrom-Json
    foreach ($c in $codes) {
        $of = Join-Path $Work "app.json"
        Run-App @("--gameinfo", "`"$data`"", "`"$c`"", "`"$of`"") | Out-Null
        $mine = [IO.File]::ReadAllText($of, [Text.Encoding]::UTF8) | ConvertFrom-Json
        $r = $ref.$c
        $same = @("code", "paks", "down", "est", "gameinfo", "mods") | Where-Object { "$($r.$_)" -cne "$($mine.$_)" }
        Check-That "$c$(if ($same) { ': differs in ' + ($same -join ', ') })" (-not $same)
    }
}

# ---------- 2: installs into a made-up game folder
"installs"
$game = Join-Path $Work "Deadlock"; $cit = Join-Path $game "game\citadel"; $addons = Join-Path $cit "addons"; $g = "`"$game`""
New-Item -ItemType Directory -Force $addons | Out-Null
$stock = "`"GameInfo`"`r`n{`r`n`tgame `"citadel`"`r`n`tFileSystem`r`n`t{`r`n`t`tSearchPaths`r`n`t`t{`r`n`t`t`tGame_UILanguage`tcitadel_*LANGUAGE*`r`n`t`t`tGame_LowViolence`tcitadel_lv`r`n`t`t`tGame citadel`r`n`t`t`tGame core`r`n`t`t}`r`n`t}`r`n`tConVars`r`n`t{`r`n`t`t`"rate`" `"1000000`"`r`n`t}`r`n}`r`n"
[IO.File]::WriteAllText((Join-Path $cit "gameinfo.gi"), $stock)
$want = [IO.File]::ReadAllText($data, [Text.Encoding]::UTF8) | ConvertFrom-Json
function Hash-Of([string]$f) { (Get-FileHash $f -Algorithm SHA256).Hash }
function Size-Of([string]$pak) { (Get-Item "$addons\$pak").Length }
$hud = $want.files.'hud.vpk'.parts[0].size; $cap = $want.files.'soundcap.vpk'.parts[0].size
$bars = $want.files.'hpbars.vpk'.parts[0].size; $light = $want.files.'brighterlighting.vpk'.parts[0].size

# the player's video settings, where the game keeps them: Steam's userdata (the pack's gameinfo.gi leaves them there)
$env:COMPLOCK_STEAM = Join-Path $Work "steam"; $ud = "$env:COMPLOCK_STEAM\userdata\111\1422450\local\cfg"; New-Item -ItemType Directory -Force $ud | Out-Null
$video = "`"video.cfg`"`r`n{`r`n`t`"Version`"`t`t`"20`"`r`n`t`"setting.defaultres`"`t`t`"1920`"`r`n`t`"setting.defaultresheight`"`t`t`"1080`"`r`n`t`"setting.fullscreen`"`t`t`"1`"`r`n`t`"setting.r_citadel_ssao_quality`"`t`t`"3`"`r`n`t`"setting.r_effects_bloom`"`t`t`"true`"`r`n`t`"setting.r_citadel_antialiasing`"`t`t`"1`"`r`n`t`"setting.r_citadel_upscaling`"`t`t`"4`"`r`n`t`"setting.r_citadel_shadow_quality`"`t`t`"2`"`r`n`t`"setting.r_texture_stream_mip_bias`"`t`t`"0`"`r`n`t`"setting.shaderquality`"`t`t`"1`"`r`n`t`"setting.mat_viewportscale`"`t`t`"0.8`"`r`n}`r`n"
[IO.File]::WriteAllText("$ud\video.txt", $video)
$o = Run-App @("--install", $g, "soundcap,hpbars")
Check-That "first install: HUD++, sound cap and HP bars are there" ((Test-Path "$addons\pak01_dir.vpk") -and (Test-Path "$addons\pak03_dir.vpk") -and (Test-Path "$addons\pak05_dir.vpk") -and $code -eq 0)
Check-That "pak01 has the site's size" ((Size-Of "pak01_dir.vpk") -eq $hud)
Check-That "gameinfo.gi was written, the stock one kept beside it" (((Get-Content "$cit\gameinfo.gi" -Raw) -match 'citadel/addons') -and ((Get-Content "$cit\gameinfo.gi.complock-bak" -Raw) -eq $stock))
Check-That "mods.txt and complock.json are there, the download folder is gone" ((Test-Path "$addons\mods.txt") -and (Test-Path "$addons\complock.json") -and -not (Test-Path "$addons\.complock-tmp"))
$v = [IO.File]::ReadAllText("$ud\video.txt")
Check-That "video.txt: the player's file in Steam's userdata is set to the lowest settings" ($v -match '"setting.r_citadel_ssao_quality"\s+"0"' -and $v -match '"setting.r_effects_bloom"\s+"false"' -and $v -match '"setting.r_citadel_upscaling"\s+"0"' -and $v -match '"setting.r_citadel_shadow_quality"\s+"0"' -and $v -match '"setting.r_texture_stream_mip_bias"\s+"2"' -and $v -match '"setting.r_depth_of_field"\s+"false"')
Check-That "resolution and window mode stay, render scale is the default 75%; the file from before is kept; none appears in the game's cfg folder" ($v -match '"setting.defaultres"\s+"1920"' -and $v -match '"setting.fullscreen"\s+"1"' -and $v -match '"setting.mat_viewportscale"\s+"0.75"' -and [IO.File]::ReadAllText("$ud\video.txt.complock-bak") -ceq $video -and -not (Test-Path "$cit\cfg\video.txt"))
$o = Run-App @("--look", $g)
Check-That "then up to date, with the selection read back" ($o -match 'up to date: True' -and $o -match 'pack: soundcap,hpbars')
# another copy of one of the pack's mods under a number the pack doesn't use (an older build put there by hand)
function Flip-LastByte([string]$file) { $b = [IO.File]::ReadAllBytes($file); $b[$b.Length - 1] = $b[$b.Length - 1] -bxor 0xFF; [IO.File]::WriteAllBytes($file, $b) }
Copy-Item "$addons\pak03_dir.vpk" "$addons\pak12_dir.vpk"; Flip-LastByte "$addons\pak12_dir.vpk"
$o = Run-App @("--look", $g)
Check-That "another copy of a pack mod under a number the pack doesn't use is found" ($o -match 'old copy pak12_dir.vpk  \(like Sound stack update cap\)' -and $o -match 'pending: True')
$env:COMPLOCK_REPLACE = "1"; $o = Run-App @("--apply", $g, "soundcap,hpbars"); $env:COMPLOCK_REPLACE = ""
$o = Run-App @("--look", $g)
Check-That "answered with remove, it goes and the pack's own stays" (-not (Test-Path "$addons\pak12_dir.vpk") -and (Size-Of "pak03_dir.vpk") -eq $cap -and $o -match 'up to date: True')
Run-App @("--gameinfo", "`"$data`"", "soundcap,hpbars", "`"$Work\sel.json`"") | Out-Null
$site = ([IO.File]::ReadAllText("$Work\sel.json", [Text.Encoding]::UTF8) | ConvertFrom-Json).gameinfo
Check-That "gameinfo.gi is the site's file, whole" ([IO.File]::ReadAllText("$cit\gameinfo.gi") -ceq $site)
$good = Hash-Of "$cit\gameinfo.gi"

# Mod managers put search paths of their own into gameinfo.gi: Grimoire its folder, Deadlock Mod Manager a marked
# block for a profile. Those are not an update, and the app keeps them when it writes the file again. A file that no
# longer mounts addons is an update, and the app's own line then goes in between two comments.
function Add-Theirs([switch]$Grimoire) {
    $gi = [IO.File]::ReadAllText("$cit\gameinfo.gi")
    $theirs = "`t`t`t// Deadlock Mod Manager - Start (SearchPaths v2)`n`t`t`tGame citadel/addons/profile_1_x // dmm-search-path`n`t`t`t// Deadlock Mod Manager (SearchPaths v2) - End`n"
    if ($Grimoire) { $theirs = "`t`t`tGame`t`tcitadel/grimoire`n" }
    $at = $gi.IndexOf("`n", $gi.IndexOf("{", $gi.IndexOf("SearchPaths"))) + 1
    [IO.File]::WriteAllText("$cit\gameinfo.gi", $gi.Insert($at, $theirs))
}
Add-Theirs; Add-Theirs -Grimoire
$o = Run-App @("--look", $g)
Check-That "mod managers' search paths in gameinfo.gi are not an update" ($o -match 'up to date: True')
$o = Run-App @("--apply", $g, "soundcap,hpbars,ticklines")
$gi = [IO.File]::ReadAllText("$cit\gameinfo.gi")
Check-That "a change of selection rewrites gameinfo.gi and keeps their search paths" ($gi -match 'citadel/grimoire' -and $gi -match 'profile_1_x // dmm-search-path' -and $gi -match '"citadel_unit_status_breakpoint_line_small_amt" "1000000"')
$o = Run-App @("--apply", $g, "soundcap,hpbars")
$o = Run-App @("--look", $g)
Check-That "and back: up to date, their paths still there" ($o -match 'up to date: True' -and [IO.File]::ReadAllText("$cit\gameinfo.gi") -match 'citadel/grimoire')
$gi = [IO.File]::ReadAllText("$cit\gameinfo.gi")
[IO.File]::WriteAllText("$cit\gameinfo.gi", ($gi -replace '(?m)^\s*Game\s+citadel/addons\r?\n', ''))
$o = Run-App @("--look", $g)
Check-That "a gameinfo.gi that no longer mounts addons is an update" ($o -match 'gameinfo update: True')
$o = Run-App @("--update", $g, "gameinfo")
$gi = [IO.File]::ReadAllText("$cit\gameinfo.gi")
$o = Run-App @("--look", $g)
Check-That "the update puts the pack's line back between its own comments and keeps theirs" ($gi -match '// CompLock BEGIN\r?\n\s*Game\s+citadel/addons\r?\n\s*// CompLock END' -and $gi -match 'citadel/grimoire' -and $gi -match 'profile_1_x' -and $o -match 'up to date: True')
# a game update puts its own gameinfo.gi back: an update on gameinfo.gi's row, nothing to apply
[IO.File]::WriteAllText((Join-Path $cit "gameinfo.gi"), $stock)
$o = Run-App @("--look", $g)
Check-That "a game update's gameinfo.gi shows as an update, not as a change of selection" ($o -match 'gameinfo: Reset' -and $o -match 'gameinfo update: True' -and $o -match 'pending: False' -and $o -match 'download: 0')
$o = Run-App @("--update", $g, "soundcap")
Check-That "another row's update leaves gameinfo.gi alone" ((Get-Content "$cit\gameinfo.gi" -Raw) -eq $stock)
$o = Run-App @("--update", $g, "gameinfo")
Check-That "its own row's update puts it back" ((Hash-Of "$cit\gameinfo.gi") -eq $good)

# a video setting raised in the game afterwards: an update on the video row, and only there
function Set-Shadows([string]$to) { [IO.File]::WriteAllText("$ud\video.txt", ([IO.File]::ReadAllText("$ud\video.txt") -replace '("setting.r_citadel_shadow_quality"\s+)"\d"', ('$1"' + $to + '"'))) }
Set-Shadows "2"
$o = Run-App @("--look", $g)
Check-That "a video setting raised afterwards is an update, not a change of selection" ($o -match 'video update: True' -and $o -match 'pending: False')
$o = Run-App @("--update", $g, "soundcap")
Check-That "another row's update leaves video.txt alone" ([IO.File]::ReadAllText("$ud\video.txt") -match '"setting.r_citadel_shadow_quality"\s+"2"')
$o = Run-App @("--update", $g, "video")
Check-That "its own row's update sets it back; the file kept from before is still the first one" ([IO.File]::ReadAllText("$ud\video.txt") -match '"setting.r_citadel_shadow_quality"\s+"0"' -and [IO.File]::ReadAllText("$ud\video.txt.complock-bak") -ceq $video)
# another render scale picked on the row
$env:COMPLOCK_SCALE = "60"; $o = Run-App @("--look", $g)
Check-That "another render scale picked is a change of selection, not an update" ($o -match 'video pending: True' -and $o -match 'video update: False')
$o = Run-App @("--apply", $g, "soundcap,hpbars"); $env:COMPLOCK_SCALE = ""
Check-That "Apply sets it" ([IO.File]::ReadAllText("$ud\video.txt") -match '"setting.mat_viewportscale"\s+"0.6"')
[IO.File]::WriteAllText("$ud\video.txt", ([IO.File]::ReadAllText("$ud\video.txt") -replace '("setting.mat_viewportscale"\s+)"0.6"', '$1"0.60000002"'))
$o = Run-App @("--look", $g)
Check-That "it is remembered, and the game's own way of writing the number is the same value" ($o -match 'video: scale 60' -and $o -match 'up to date: True')
$env:COMPLOCK_VIDEO = "0"; $o = Run-App @("--apply", $g, "soundcap,hpbars"); $env:COMPLOCK_VIDEO = ""
Set-Shadows "3"
$o = Run-App @("--look", $g)
Check-That "with the option switched off, video.txt is nothing to the app" ($o -match 'up to date: True' -and $o -match 'video: scale \d+  on False')
# a mod the app installed earlier, since updated on the site: the record says the app wrote this file as it is
[IO.File]::WriteAllText("$addons\pak03_dir.vpk", "old build")
$st = Get-Content "$addons\complock.json" -Raw | ConvertFrom-Json
$st.files.'pak03_dir.vpk'.size = 9; $st.files.'pak03_dir.vpk'.crc = 1; $st.files.'pak03_dir.vpk'.mtime = (Get-Item "$addons\pak03_dir.vpk").LastWriteTimeUtc.Ticks
[IO.File]::WriteAllText("$addons\complock.json", ($st | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
$o = Run-App @("--look", $g)
Check-That "an older build shows as an update, not as changed by hand" ($o -match 'update pak03_dir.vpk' -and $o -notmatch 'changed pak03' -and $o -match 'pending: False' -and $o -match 'up to date: False')

# Apply with one more option, and another mod sitting on the name it needs; mod managers have that file on record
[IO.File]::WriteAllText("$addons\pak04_dir.vpk", "someone else's mod"); [IO.File]::WriteAllText("$addons\pak04_000.vpk", "its chunk")
New-Item -ItemType Directory -Force "$addons\.disabled" | Out-Null; [IO.File]::WriteAllText("$addons\.disabled\pak08_dir.vpk", "a mod Grimoire has disabled")
$env:COMPLOCK_GRIMOIRE = Join-Path $Work "grimoire"; New-Item -ItemType Directory -Force $env:COMPLOCK_GRIMOIRE | Out-Null
[IO.File]::WriteAllText("$env:COMPLOCK_GRIMOIRE\settings.json", (@{ deadlockPath = $game } | ConvertTo-Json))
[IO.File]::WriteAllText("$env:COMPLOCK_GRIMOIRE\mod-metadata.json", (@{ "pak04_dir.vpk" = @{ modName = "Somebody's mod"; sha256 = (Hash-Of "$addons\pak04_dir.vpk").ToLower() }; "pak03_dir.vpk" = @{ modName = "A name from before"; sha256 = ("0" * 64); gameBananaId = 123 }; "pak05_dir.vpk" = @{ modName = "Another name from before"; sha256 = ("1" * 64) }; "parked_dir.vpk" = @{ modName = "A parked mod"; sha256 = ("2" * 64); heroDetection = @{ version = 1; heroes = @("abrams") } } } | ConvertTo-Json -Depth 5))
$o = Run-App @("--look", $g, "soundcap,hpbars,brighterlighting")
Check-That "the question names Grimoire and the mod it has under that name" ($o -match "foreign pak04_dir.vpk.*in Grimoire as Somebody's mod")
[IO.File]::WriteAllText("$addons\.dmm.json", '{"version":1,"mods":{"m1":{"enabled":true,"currentVpks":["pak04_dir.vpk"]}}}')
$o = Run-App @("--look", $g, "soundcap,hpbars,brighterlighting")
Check-That "or Deadlock Mod Manager, when the file is in its manifest" ($o -match 'foreign pak04_dir.vpk.*on record in Deadlock Mod Manager')
$o = Run-App @("--apply", $g, "soundcap,hpbars,brighterlighting")
Check-That "Apply installs the new option" ((Size-Of "pak04_dir.vpk") -eq $light)
Check-That "the other mod moved with its chunk to a number that isn't used in .disabled either" ((Test-Path "$addons\pak09_000.vpk") -and ((Get-Content "$addons\pak09_dir.vpk" -Raw) -eq "someone else's mod") -and -not (Test-Path "$addons\pak08_dir.vpk"))
$rows = [IO.File]::ReadAllText("$env:COMPLOCK_GRIMOIRE\mod-metadata.json", [Text.Encoding]::UTF8) | ConvertFrom-Json
Check-That "Grimoire's records: the moved mod keeps its name under its new file name" ($rows.'pak09_dir.vpk'.modName -eq "Somebody's mod")
Check-That "Grimoire's records: the pack's paks have their own names, whatever the old record said" ($rows.'pak04_dir.vpk'.modName -eq "Brighter lighting - BreadRollius" -and $rows.'pak05_dir.vpk'.modName -eq "jcStani's HP Bars - jcStani" -and $rows.'pak01_dir.vpk'.modName -eq "HUD++ - HoppCX, jcStani" -and $rows.'pak01_dir.vpk'.author -eq "HoppCX, jcStani")
Check-That "Grimoire's records: a record of another mod is left as it was" ($rows.'parked_dir.vpk'.modName -eq "A parked mod" -and $rows.'parked_dir.vpk'.heroDetection.heroes[0] -eq "abrams" -and (Test-Path "$env:COMPLOCK_GRIMOIRE\mod-metadata.json.complock-bak"))
Check-That "Apply leaves the out-of-date mod as it is" ((Get-Content "$addons\pak03_dir.vpk" -Raw) -eq "old build")
$o = Run-App @("--look", $g)
Check-That "the selection is saved, the update still offered" ($o -match 'pack: soundcap,brighterlighting,hpbars' -and $o -match 'pending: False' -and $o -match 'update pak03_dir.vpk')
$o = Run-App @("--update", $g, "hpbars")
Check-That "another row's update leaves it too" ((Get-Content "$addons\pak03_dir.vpk" -Raw) -eq "old build")
$o = Run-App @("--update", $g, "soundcap")
Check-That "its own row's update brings it up to date" ((Size-Of "pak03_dir.vpk") -eq $cap)
$rows = [IO.File]::ReadAllText("$env:COMPLOCK_GRIMOIRE\mod-metadata.json", [Text.Encoding]::UTF8) | ConvertFrom-Json
Check-That "Grimoire's records: an updated pak gets its name, and the other mod's id is gone from the record" ($rows.'pak03_dir.vpk'.modName -eq "Sound stack update cap - HoppCX" -and -not $rows.'pak03_dir.vpk'.gameBananaId)
$o = Run-App @("--look", $g)
Check-That "then up to date" ($o -match 'up to date: True')

# a mod manager renumbered the folder: another mod now sits under one of the pack's names
[IO.File]::WriteAllText("$addons\pak05_dir.vpk", "another mod, put here by a reorder")
$o = Run-App @("--look", $g)
Check-That "another mod under the pack's name is not taken for the pack's file" ($o -match 'foreign pak05_dir.vpk' -and $o -notmatch 'changed pak05' -and $o -notmatch 'looks like')
$o = Run-App @("--apply", $g, "soundcap,hpbars,brighterlighting")
Check-That "it is moved, not overwritten, and the pack's file is back" ((Size-Of "pak05_dir.vpk") -eq $bars -and ((Get-Content "$addons\pak10_dir.vpk" -Raw) -eq "another mod, put here by a reorder"))

# a file changed by hand
Add-Content "$addons\pak05_dir.vpk" "x"
$mine = Hash-Of "$addons\pak05_dir.vpk"
$o = Run-App @("--look", $g)
Check-That "a pak changed by hand is reported" ($o -match 'changed pak05_dir.vpk')
$o = Run-App @("--update", $g, "soundcap")
Check-That "another row's update leaves it alone" ((Hash-Of "$addons\pak05_dir.vpk") -eq $mine)
$o = Run-App @("--update", $g, "all")
Check-That "Update all replaces it" ((Size-Of "pak05_dir.vpk") -eq $bars)

# a pak that is there under another number
Move-Item "$addons\pak03_dir.vpk" "$addons\pak11_dir.vpk"
$o = Run-App @("--install", $g)
Check-That "the sound cap found under pak11 is moved back, not downloaded" ($o -match 'pak03_dir.vpk.*here as pak11_dir.vpk' -and (Test-Path "$addons\pak03_dir.vpk") -and -not (Test-Path "$addons\pak11_dir.vpk"))

$o = Run-App @("--apply", $g, "hpbars")
Check-That "options turned off: their paks go, the other mods stay" (-not (Test-Path "$addons\pak03_dir.vpk") -and -not (Test-Path "$addons\pak04_dir.vpk") -and (Test-Path "$addons\pak09_dir.vpk") -and (Test-Path "$addons\pak10_dir.vpk") -and (Test-Path "$addons\pak05_dir.vpk"))

Remove-Item "$addons\complock.json"
$o = Run-App @("--look", $g)
Check-That "without the app's own file, mods.txt gives the selection" ($o -match 'pack: hpbars' -and $o -match 'installed: True')
Remove-Item "$addons\mods.txt"
$o = Run-App @("--look", $g)
Check-That "without mods.txt, the paks and gameinfo.gi in the folder do" ($o -match 'pack: hpbars\s')

Flip-LastByte "$addons\pak05_dir.vpk"
$o = Run-App @("--look", $g)
Check-That "an older copy of a pack mod still gives the selection to start from" ($o -match 'pack: hpbars\s')
Flip-LastByte "$addons\pak05_dir.vpk"
$o = Run-App @("--remove", $g)
Check-That "removal: the pack's paks gone, the other mods, the stock gameinfo.gi and the player's video.txt back" (-not (Test-Path "$addons\pak01_dir.vpk") -and -not (Test-Path "$addons\pak05_dir.vpk") -and (Test-Path "$addons\pak09_dir.vpk") -and ((Get-Content "$cit\gameinfo.gi" -Raw) -eq $stock) -and [IO.File]::ReadAllText("$ud\video.txt") -ceq $video)

# an older or edited copy of one of the pack's mods, put under the pack's name by hand: no record says it is the pack's
$o = Run-App @("--install", $g, "none")
Remove-Item "$addons\complock.json", "$addons\mods.txt"
$b = [IO.File]::ReadAllBytes("$addons\pak01_dir.vpk"); $b[$b.Length - 1] = $b[$b.Length - 1] -bxor 0xFF; [IO.File]::WriteAllBytes("$addons\pak01_dir.vpk", $b)
$o = Run-App @("--look", $g, "none")
Check-That "another copy of HUD++ under pak01 is seen as one" ($o -match "foreign pak01_dir.vpk  \(looks like the pack's\)")
$before = @(Get-ChildItem "$addons\pak*_dir.vpk").Count
$env:COMPLOCK_REPLACE = "1"; $o = Run-App @("--install", $g, "none"); $env:COMPLOCK_REPLACE = ""
$o = Run-App @("--look", $g, "none")
Check-That "answered with replace, it is replaced where it is and nothing is moved" ($o -match 'up to date: True' -and @(Get-ChildItem "$addons\pak*_dir.vpk").Count -eq $before)

# ---------- finding the game: Steam on one drive, the game in a library on another, never modded (no addons folder).
# --find prints the citadel folder the app would use: with no argument what it finds through Steam, with one what it
# makes of a folder picked by hand. With COMPLOCK_STEAM set it looks nowhere but under that made-up Steam.
$keep = $env:COMPLOCK_STEAM
$fs = Join-Path $Work "find\steam"; $fl = Join-Path $Work "find\GamesSSD\SteamLibrary"; $fg = "$fl\steamapps\common\Deadlock"; $fc = "$fg\game\citadel"
New-Item -ItemType Directory -Force "$fs\steamapps", "$fs\config", $fc, "$fg\game\bin\win64", "$fl\steamapps\common\Other" | Out-Null
[IO.File]::WriteAllText("$fc\gameinfo.gi", "x")
$env:COMPLOCK_STEAM = $fs
$q = { param($p) $p.Replace('\', '\\') }
$first = "`"libraryfolders`"`n{`n`t`"0`"`n`t{`n`t`t`"path`"`t`t`"$(& $q $fs)`"`n`t`t`"apps`"`n`t`t{`n`t`t`t`"228980`"`t`t`"1`"`n`t`t}`n`t}`n"
$both = $first + "`t`"1`"`n`t{`n`t`t`"path`"`t`t`"$(& $q $fl)`"`n`t`t`"apps`"`n`t`t{`n`t`t`t`"1422450`"`t`t`"2`"`n`t`t}`n`t}`n}`n"; $only = $first + "}`n"
$old = "`"LibraryFolders`"`n{`n`t`"TimeNextStatsReport`"`t`t`"1700000000`"`n`t`"ContentStatsID`"`t`t`"-1`"`n`t`"1`"`t`t`"$(& $q $fl)`"`n}`n"
$acf = "$fl\steamapps\appmanifest_1422450.acf"; $list = "$fs\steamapps\libraryfolders.vdf"
[IO.File]::WriteAllText($acf, "`"AppState`"`n{`n`t`"appid`"`t`t`"1422450`"`n`t`"installdir`"`t`t`"Deadlock`"`n}`n")
[IO.File]::WriteAllText($list, $both)
$o = Run-App @("--find"); Check-That "the game in a second library is found, with no addons folder yet" ($o -eq "found: $fc")
[IO.File]::WriteAllText($list, $only); [IO.File]::WriteAllText("$fs\config\libraryfolders.vdf", $both)
$o = Run-App @("--find"); Check-That "a library that only Steam's config\libraryfolders.vdf lists is found" ($o -eq "found: $fc")
Remove-Item "$fs\config\libraryfolders.vdf"; [IO.File]::WriteAllText($list, $old)
$o = Run-App @("--find"); Check-That "the older form of libraryfolders.vdf is read" ($o -eq "found: $fc")
[IO.File]::WriteAllText($list, $both); Remove-Item $acf
$o = Run-App @("--find"); Check-That "a library without the game's appmanifest is still looked in" ($o -eq "found: $fc")
[IO.File]::WriteAllText($list, $only)
$o = Run-App @("--find"); Check-That "a game in no library Steam lists is not found there" ($o -match '^found: nothing' -and $code -eq 3)
foreach ($pick in @($fg, "$fg\game", $fc, "$fg\game\bin\win64", "$fl\steamapps\common", $fl, (Split-Path $fl))) {
    $o = Run-App @("--find", $pick); Check-That "picked by hand, $($pick.Substring($Work.Length + 6)) gives the game's folder" ($o -eq "found: $fc")
}
$o = Run-App @("--find", "$fl\steamapps\common\Other"); Check-That "another game's folder is not taken, and the app says what is missing" ($o -match 'found: nothing \(there is no game\\citadel\\gameinfo\.gi')
$env:COMPLOCK_STEAM = $keep

if ($fails) { "$fails failed"; exit 1 }
"all passed"
