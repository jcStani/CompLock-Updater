# Builds CompLock.exe with the C# compiler that is part of Windows (.NET Framework 4.x): nothing to install.
#   powershell -ExecutionPolicy Bypass -File build.ps1 [out.exe]
# The window shows the day it was built (Build.Date, written here).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = if ($args.Count) { $args[0] } else { Join-Path $here "CompLock.exe" }
$date = [DateTime]::Now.ToString("d MMM yyyy", [Globalization.CultureInfo]::InvariantCulture)
$stamp = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($out))) "CompLock.build.cs"
[IO.File]::WriteAllText($stamp, "namespace CompLock { static class Build { public const string Date = `"$date`"; } }")
try {
    & $csc /nologo /target:winexe /optimize+ /platform:anycpu /warn:4 "/out:$out" "/win32icon:$here\icon.ico" "/win32manifest:$here\app.manifest" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "$here\src\*.cs" $stamp
}
finally { Remove-Item $stamp -Force -ErrorAction SilentlyContinue }
if ($LASTEXITCODE) { exit $LASTEXITCODE }
"built $out ($((Get-Item $out).Length) bytes, $date)"
