# Builds Legacy Paint - Dark Mode -> bin\PaintDark.exe, then offers Explorer integration.
#
# Uses the Roslyn compiler shipped with any installed .NET SDK and targets .NET Framework 4.8,
# which is part of every Windows 10 / 11 installation (nothing extra to install for users).
#
#   .\build.ps1                         build (if src\ is present), then ask about Explorer integration
#   .\build.ps1 -Shell Apply            build, install to %LOCALAPPDATA%\PaintDark, and register Right-click -> Edit
#   .\build.ps1 -Shell Revert           restore the original Windows Paint for Right-click -> Edit
#   .\build.ps1 -Shell Abort            build only (exit without changing Explorer integration)
#   .\build.ps1 -NoBuild -Shell ...     only change the Explorer integration
#   .\build.ps1 -NoBuild -Shell Clean   restore Windows defaults and remove per-user installation
#   .\build.ps1 -Launch                 launch PaintDark.exe after building
param(
    [switch]$Debug,
    [ValidateSet('Ask', 'Apply', 'Apply Dark Mode', 'Yes', '1', 'Revert', '2', 'Cancel', 'Abort', 'No', '3', 'Clean')][string]$Shell = 'Ask',
    [switch]$NoBuild,
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$obj = Join-Path $root 'obj'
$exe = Join-Path $bin 'PaintDark.exe'

$Marker = 'PaintDarkMode'
$InstallDir = Join-Path $env:LOCALAPPDATA 'PaintDark'
$InstalledExe = Join-Path $InstallDir 'PaintDark.exe'
$InstallMarker = Join-Path $InstallDir 'installed-by-build.ps1.txt'

# Strip Mark-of-the-Web (Zone.Identifier) from extracted release files so Windows SmartScreen
# does not block execution on standard PCs.
try { Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue } catch {}

# Normalize $Shell aliases
$ReleaseZipUrl = 'https://github.com/1nstntKaRma/Legacy-Paint-Dark-Mode/releases/latest/download/PaintDark-v1.4.zip'
$RoslynNugetUrl = 'https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers.Toolset/4.11.0'
$TempRoslynDir = Join-Path $env:TEMP 'RoslynCompiler-4.11.0'

switch ($Shell) {
    '1'               { $Shell = 'Apply' }
    'Yes'             { $Shell = 'Apply' }
    'Apply Dark Mode' { $Shell = 'Apply' }
    '2'               { $Shell = 'Revert' }
    '3'               { $Shell = 'Cancel' }
    'Abort'           { $Shell = 'Cancel' }
    'No'              { $Shell = 'Cancel' }
}

function Read-Choice([string[]]$lines, [int]$defaultIndex = 0) {
    Write-Host ''
    foreach ($line in $lines) { Write-Host $line }
    Write-Host ''
    while ($true) {
        Write-Host 'Choice: ' -NoNewline
        $raw = Read-Host
        if ($null -eq $raw) { return $defaultIndex }
        $v = $raw.Trim()
        if ($v -eq '') { return $defaultIndex }
        switch ($v) {
            '1' { return 0 }
            '2' { return 1 }
            '3' { return 2 }
        }
    }
}

function Test-Interactive {
    try { return [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and $Host.Name -eq 'ConsoleHost' -and ([Environment]::GetCommandLineArgs() -notcontains '-NonInteractive') } catch { return $false }
}

function Start-ExitCountdown([int]$seconds = 3, [string]$color = 'Gray') {
    if (-not (Test-Interactive)) { return }
    for ($i = $seconds; $i -ge 1; $i--) {
        $s = if ($i -eq 1) { '' } else { 's' }
        Write-Host "`rClosing in $i second$s...   " -ForegroundColor $color -NoNewline
        Start-Sleep -Seconds 1
    }
    Write-Host ''
}

function Exit-Session([int]$code = 0) {
    if (Test-Path -LiteralPath $TempRoslynDir) { Remove-Item -LiteralPath $TempRoslynDir -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Interactive) {
        try {
            $self = Get-CimInstance Win32_Process -Filter "ProcessId = $PID" -ErrorAction SilentlyContinue
            if ($self -and $self.ParentProcessId) {
                $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $($self.ParentProcessId)" -ErrorAction SilentlyContinue
                if ($parent -and $parent.Name -match '^(powershell|pwsh|cmd)(\.exe)?$') {
                    $parentPid = [int]$parent.ProcessId
                    $tempRoot = [System.IO.Path]::GetTempPath().TrimEnd('\')
                    $cleanCmd = ""
                    if ($root.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $escapedRoot = $root.Replace("'", "''")
                        $cleanCmd = "Remove-Item -LiteralPath '$escapedRoot' -Recurse -Force -ErrorAction SilentlyContinue; "
                    }
                    $killScript = "Start-Sleep -Milliseconds 250; $cleanCmd Stop-Process -Id $parentPid -Force -ErrorAction SilentlyContinue"
                    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-WindowStyle', 'Hidden', '-Command', $killScript) -WorkingDirectory $tempRoot -WindowStyle Hidden | Out-Null
                }
            }
        } catch {}
        try { $Host.SetShouldExit($code) } catch {}
        [Environment]::Exit($code)
    }
    exit $code
}

function Test-HasSource {
    if (-not (Test-Path -LiteralPath $src)) { return $false }
    $csCount = @(Get-ChildItem -LiteralPath $src -Filter *.cs -Recurse -ErrorAction SilentlyContinue).Count
    return ($csCount -gt 0)
}

function Show-BuildSummary {
    $size = [math]::Round((Get-Item -LiteralPath $exe).Length / 1KB)
    Write-Host 'Legacy Paint - Dark Mode Build path:' -ForegroundColor Green
    Write-Host "`"$bin`"" -ForegroundColor Green
    Write-Host "$(Split-Path $exe -Leaf) ($size KB)" -ForegroundColor Green
    if ((Test-Path $InstallMarker) -and (Test-Path $InstalledExe)) {
        $same = (Get-FileHash $exe).Hash -eq (Get-FileHash $InstalledExe).Hash
        if (-not $same) { Write-Host "  The installed copy in $InstallDir is older. Choose Apply Dark Mode (or -Shell Apply) to update it." -ForegroundColor Yellow }
    }
}

# ======================================================================================= build
function Invoke-Build {
    if (-not (Test-HasSource)) {
        if (Test-Path -LiteralPath $exe) {
            Show-BuildSummary
            return
        }
        throw "No source files found in '$src' and pre-built binary was not found in '$bin'. Extract the entire zip archive before running build.ps1."
    }

    New-Item -ItemType Directory -Force $bin, $obj | Out-Null
    $csc = Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Roslyn\bincore\csc.dll" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.FullName -replace '.*\\sdk\\([\d\.]+)[^\\]*\\.*', '$1') } -Descending | Select-Object -First 1
    if (-not $csc) {
        $tempCsc = Join-Path $TempRoslynDir 'tasks\net472\csc.exe'
        if (Test-Path -LiteralPath $tempCsc) { $csc = Get-Item -LiteralPath $tempCsc }
    }
    if (-not $csc) {
        if (Test-Path -LiteralPath $exe) {
            Show-BuildSummary
            return
        }
        $cPick = 0
        if (Test-Interactive) {
            Write-Host 'No local C# compiler (.NET SDK) was detected on this system.' -ForegroundColor Yellow
            $compilerLines = @(
                '  [1] Compile Locally - Download Microsoft''s official Roslyn compiler (~25 MB in temp) and compile from source.',
                '  [2] Use Pre-built   - Download the pre-built release executable directly from GitHub Releases.',
                '  [3] Cancel          - Exit without downloading or changing anything.'
            )
            $cPick = Read-Choice -lines $compilerLines -defaultIndex 0
        }
        if ($cPick -eq 2) {
            Write-Host ''
            Write-Host '[X] Operation aborted: No files were downloaded and no system changes were made.' -ForegroundColor Red
            Exit-Session 0
        }
        elseif ($cPick -eq 1) {
            Write-Host 'Downloading pre-built release executable from GitHub Releases...' -ForegroundColor Cyan
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $z = Join-Path $env:TEMP ('PaintDark-rel-' + [guid]::NewGuid().ToString('N') + '.zip')
            $x = Join-Path $env:TEMP ('PaintDark-ext-' + [guid]::NewGuid().ToString('N'))
            try {
                Invoke-WebRequest -Uri $ReleaseZipUrl -OutFile $z -UseBasicParsing
                Expand-Archive -LiteralPath $z -DestinationPath $x -Force
                Copy-Item -LiteralPath (Join-Path $x 'bin\PaintDark.exe') -Destination $exe -Force
                try { Unblock-File -LiteralPath $exe -ErrorAction SilentlyContinue } catch {}
            } finally {
                Remove-Item -LiteralPath $z -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $x -Recurse -Force -ErrorAction SilentlyContinue
            }
            Show-BuildSummary
            return
        }
        else {
            Write-Host "Downloading Microsoft's official Roslyn compiler (~25 MB) to temp..." -ForegroundColor Cyan
            try {
                [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
                $z = Join-Path $env:TEMP ('roslyn-' + [guid]::NewGuid().ToString('N') + '.zip')
                Invoke-WebRequest -Uri $RoslynNugetUrl -OutFile $z -UseBasicParsing
                if (Test-Path -LiteralPath $TempRoslynDir) { Remove-Item -LiteralPath $TempRoslynDir -Recurse -Force -ErrorAction SilentlyContinue }
                Expand-Archive -LiteralPath $z -DestinationPath $TempRoslynDir -Force
                Remove-Item -LiteralPath $z -Force -ErrorAction SilentlyContinue
                $tempCsc = Join-Path $TempRoslynDir 'tasks\net472\csc.exe'
                if (Test-Path -LiteralPath $tempCsc) { $csc = Get-Item -LiteralPath $tempCsc }
            } catch {
                Write-Host "Standalone Roslyn download unavailable ($($_.Exception.Message)). Falling back to winget (.NET SDK)..." -ForegroundColor Yellow
            }
            if (-not $csc) {
                $wg = Get-Command winget -ErrorAction SilentlyContinue
                if ($wg) {
                    & winget install --id Microsoft.DotNet.SDK.8 --exact --accept-source-agreements --accept-package-agreements
                    $csc = Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Roslyn\bincore\csc.dll" -ErrorAction SilentlyContinue |
                        Sort-Object { [version]($_.FullName -replace '.*\\sdk\\([\d\.]+)[^\\]*\\.*', '$1') } -Descending | Select-Object -First 1
                }
            }
            if (-not $csc) { throw 'Unable to acquire the Roslyn C# compiler via NuGet or winget.' }
        }
    }

    $fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    if (-not (Test-Path $fw)) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }
    $refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF\PresentationCore.dll', 'WPF\WindowsBase.dll', 'System.Xaml.dll') |
        ForEach-Object { "-r:$fw\$_" }
    $files = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
    $common = @('-nologo', '-noconfig', '-nostdlib', '-target:winexe', '-platform:anycpu', '-unsafe', '-langversion:latest',
                '-deterministic', '-nowarn:CS0169,CS0414,CS0649,CS0168,CS0219,CS1998,CS0108,CS1690', "-win32manifest:$src\app.manifest")
    if ($Debug) { $common += @('-debug:portable', '-optimize-', '-define:DEBUG') } else { $common += @('-optimize+', '-debug-') }

    function Compile($out, $extra) {
        $argv = @($common) + @($extra | Where-Object { $_ }) + @("-out:$out") + @($refs) + @($files)
        if ($csc.Extension -ieq '.dll') { & dotnet $csc.FullName @argv } else { & $csc.FullName @argv }
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }
    }

    $running = Get-Process PaintDark -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
    if ($running) { throw "Paint Dark Mode is running from $exe. Close it and build again." }

    $ico = Join-Path $obj 'app.ico'
    $tmp = Join-Path $obj 'PaintDark.noicon.exe'
    Compile $tmp @()
    & $tmp --export-icon $ico | Out-Null
    Start-Sleep -Milliseconds 200
    if (-not (Test-Path $ico)) { throw 'Icon export failed.' }
    Compile $exe @("-win32icon:$ico")

    Show-BuildSummary
}

# ======================================================================================= Explorer integration
# Everything is written per user (HKCU\Software\Classes and %LOCALAPPDATA%\PaintDark), which Windows merges
# over machine-wide registrations, so no administrator rights are needed and nothing system-wide is modified.
$HKCU = [Microsoft.Win32.Registry]::CurrentUser
$HKCR = [Microsoft.Win32.Registry]::ClassesRoot
$ClassesRoot = 'Software\Classes'
$EditVerbs = @('SystemFileAssociations\image\shell\edit', 'Paint.Picture\shell\edit', 'PBrush\shell\edit')

function U($path) { "$ClassesRoot\$path" }

function Test-MergedKey($path) {
    $k = $HKCR.OpenSubKey($path)
    if ($k) { $k.Close(); return $true }
    return $false
}

function Get-MergedCommand($verbPath) {
    $k = $HKCR.OpenSubKey("$verbPath\command")
    if (-not $k) { return $null }
    try { return $k.GetValue('') } finally { $k.Close() }
}

function Get-UserValue($path, $name) {
    $k = $HKCU.OpenSubKey((U $path))
    if (-not $k) { return $null }
    try { return $k.GetValue($name) } finally { $k.Close() }
}

function Set-UserValue($path, $name, $value) {
    $k = $HKCU.OpenSubKey((U $path), $true)
    try { $k.SetValue($name, [string]$value) } finally { $k.Close() }
}

function New-OwnedKey($path) {
    $cur = $ClassesRoot
    foreach ($p in $path -split '\\') {
        $cur = "$cur\$p"
        $k = $HKCU.OpenSubKey($cur, $true)
        if (-not $k) { $k = $HKCU.CreateSubKey($cur); $k.SetValue($Marker, 'created') }
        $k.Close()
    }
}

function Remove-OwnedKey($path) {
    $parts = $path -split '\\'
    for ($i = $parts.Count; $i -ge 1; $i--) {
        $rel = U (($parts[0..($i - 1)]) -join '\')
        $k = $HKCU.OpenSubKey($rel)
        if (-not $k) { continue }
        $mine = $null -ne $k.GetValue($Marker)
        $others = @($k.GetValueNames() | Where-Object { $_ -ne $Marker -and $_ -ne '' }).Count
        $hasDefault = $null -ne $k.GetValue('')
        $subs = $k.SubKeyCount
        $k.Close()
        if (-not $mine) { break }
        if ($i -eq $parts.Count) { $HKCU.DeleteSubKeyTree($rel); continue }
        if ($subs -eq 0 -and $others -eq 0 -and -not $hasDefault) { $HKCU.DeleteSubKey($rel) } else { break }
    }
}

function Set-EditCommand($verbPath, $command, $tag) {
    if (($null -ne (Get-UserValue "$verbPath\command" '')) -and ($null -eq (Get-UserValue $verbPath $Marker))) {
        Set-UserValue $verbPath "$Marker.Previous" (Get-UserValue "$verbPath\command" '')
    }
    New-OwnedKey "$verbPath\command"
    Set-UserValue "$verbPath\command" '' $command
    Set-UserValue $verbPath $Marker $tag
}

function Hide-Paint3D {
    $n = 0
    $sfa = $HKCR.OpenSubKey('SystemFileAssociations')
    if ($sfa) {
        foreach ($ext in $sfa.GetSubKeyNames()) {
            $k = $sfa.OpenSubKey("$ext\Shell\3D Edit")
            if (-not $k) { continue }
            $k.Close()
            $rel = "SystemFileAssociations\$ext\Shell\3D Edit"
            New-OwnedKey $rel
            Set-UserValue $rel 'ProgrammaticAccessOnly' ''
            if ($null -eq (Get-UserValue $rel $Marker)) { Set-UserValue $rel $Marker 'hidden' }
            $n++
        }
        $sfa.Close()
    }
    if ($n -gt 0) { Write-Host "  'Edit with Paint 3D' hidden for $n file types." }
    else { Write-Host "  'Edit with Paint 3D' is not present on this system." }
}

function Show-Paint3D {
    $sfa = $HKCU.OpenSubKey((U 'SystemFileAssociations'))
    if (-not $sfa) { return }
    $exts = $sfa.GetSubKeyNames()
    $sfa.Close()
    foreach ($ext in $exts) {
        $rel = "SystemFileAssociations\$ext\Shell\3D Edit"
        if ($null -eq (Get-UserValue $rel $Marker)) { continue }
        $k = $HKCU.OpenSubKey((U $rel), $true)
        try { $k.DeleteValue('ProgrammaticAccessOnly', $false) } finally { $k.Close() }
        Remove-OwnedKey $rel
    }
}

function Remove-EditOverrides {
    foreach ($v in $EditVerbs) {
        if ($null -eq (Get-UserValue $v $Marker)) { continue }
        $prev = Get-UserValue $v "$Marker.Previous"
        if ($null -ne $prev) {
            Set-UserValue "$v\command" '' $prev
            $k = $HKCU.OpenSubKey((U $v), $true)
            try { $k.DeleteValue("$Marker.Previous", $false); $k.DeleteValue($Marker, $false) } finally { $k.Close() }
        }
        else { Remove-OwnedKey $v }
    }
}

function Get-CommandExe($command) {
    if (-not $command) { return $null }
    $c = [Environment]::ExpandEnvironmentVariables($command.Trim())
    if ($c.StartsWith('"')) { return $c.Substring(1, $c.IndexOf('"', 1) - 1) }
    return ($c -split ' ')[0]
}

function Find-LegacyPaint {
    $sys = Join-Path $env:SystemRoot 'System32\mspaint.exe'
    if (Test-Path $sys) { return $sys }
    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\mspaint.exe'
    if (Test-Path $alias) { return $alias }
    return $null
}

function Install-Files {
    if (-not (Test-Path -LiteralPath $exe)) { throw "Build first: $exe was not found." }
    $running = Get-Process PaintDark -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ieq $InstalledExe }
    if ($running) { throw "Legacy Paint Dark Mode is running from $InstallDir. Close it and try again." }
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    try { Unblock-File -LiteralPath $exe -ErrorAction SilentlyContinue } catch {}
    Copy-Item -LiteralPath $exe -Destination $InstalledExe -Force
    try { Unblock-File -LiteralPath $InstalledExe -ErrorAction SilentlyContinue } catch {}
    Set-Content -Path $InstallMarker -Encoding ASCII -Value @(
        "Installed by $PSCommandPath ($Marker) on $(Get-Date -Format s).",
        'Remove with: build.ps1 -NoBuild -Shell Revert (or -Shell Clean).')
    Write-Host "  Installed to $InstalledExe"
}

function Remove-Files {
    if (-not (Test-Path -LiteralPath $InstallDir)) { return }
    if (-not (Test-Path -LiteralPath $InstallMarker)) { return }
    $running = Get-Process PaintDark -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ieq $InstalledExe }
    if ($running) { throw "Legacy Paint Dark Mode is running from $InstallDir. Close it and try again." }
    Remove-Item -LiteralPath $InstalledExe -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $InstallMarker -Force -ErrorAction SilentlyContinue
    $remaining = @(Get-ChildItem -LiteralPath $InstallDir -Force -ErrorAction SilentlyContinue).Count
    if ($remaining -eq 0) {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed $InstallDir"
    } else {
        Write-Host "  Removed $InstalledExe (kept user settings in $InstallDir)"
    }
}

function Install-ShellIntegration {
    Write-Host 'Explorer integration -> Legacy Paint Dark Mode' -ForegroundColor Cyan
    Install-Files
    $cmd = "`"$InstalledExe`" `"%1`""
    Remove-EditOverrides
    foreach ($v in $EditVerbs) {
        if ($v -like 'SystemFileAssociations*' -or (Test-MergedKey ($v -replace '\\shell\\edit$', ''))) { Set-EditCommand $v $cmd 'darkpaint' }
    }
    Write-Host "  'Edit' on pictures now opens $InstalledExe"
    Hide-Paint3D
    Update-Explorer
    Write-Host ''
    Write-Host '[OK] Operation completed successfully: Legacy Paint Dark Mode is active (Right-click > Edit)' -ForegroundColor Green
}

function Revert-ShellIntegration {
    Write-Host 'Explorer integration -> the original Windows Paint' -ForegroundColor Cyan
    Remove-EditOverrides
    Remove-Files
    $c = Get-MergedCommand 'SystemFileAssociations\image\shell\edit'
    $target = Get-CommandExe $c
    if ($c -and $target -and (Test-Path $target)) { Write-Host "  'Edit' on pictures opens the original Paint again ($c)" }
    else {
        $paint = Find-LegacyPaint
        if ($paint) {
            Set-EditCommand 'SystemFileAssociations\image\shell\edit' "`"$paint`" `"%1`"" 'restored'
            Write-Host "  'Edit' restored for pictures -> $paint"
        }
        else { Write-Host "  Paint (mspaint.exe) was not found on this system, so 'Edit' was left out." -ForegroundColor Yellow }
    }
    Hide-Paint3D
    Update-Explorer
    Write-Host ''
    Write-Host '[OK] Operation completed successfully: Reverted to the original Windows Paint.' -ForegroundColor DarkYellow
}

function Clear-ShellIntegration {
    Write-Host 'Explorer integration -> removing all changes from this script' -ForegroundColor Cyan
    Remove-EditOverrides
    Show-Paint3D
    if ((Test-Path -LiteralPath $InstallDir) -and (Test-Path -LiteralPath $InstallMarker)) {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed $InstallDir"
    }
    Update-Explorer
    Write-Host ''
    Write-Host '[OK] Operation completed successfully: System restored to Windows defaults.' -ForegroundColor DarkYellow
}

function Update-Explorer {
    if (-not ('PaintDarkBuild.Shell' -as [type])) {
        Add-Type -Namespace 'PaintDarkBuild' -Name 'Shell' -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, System.IntPtr a, System.IntPtr b);'
    }
    [PaintDarkBuild.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}

# ======================================================================================= main
$asked = $false
try {
    if (-not $NoBuild) {
        if ((Test-HasSource) -or $Shell -ne 'Revert') {
            Invoke-Build
        }
    }

    if ($Launch -and (Test-Path -LiteralPath $exe)) {
        Start-Process -FilePath $exe
    }

    if ($Shell -eq 'Ask') {
        if (Test-Interactive) {
            $actionLines = @(
                '  [1] Apply Dark Mode - Right-click "Edit" @ image files will use Paint - Dark Mode.',
                '  [2] Revert          - Restore the original Windows Paint.',
                '  [3] Cancel          - Exit without changing anything.'
            )
            $pick = Read-Choice -lines $actionLines -defaultIndex 0
            $asked = $true
            $Shell = @('Apply', 'Revert', 'Cancel')[$pick]
        }
        else { $Shell = 'Cancel' }
    }

    if ($Shell -eq 'Cancel') {
        if ($asked -or $NoBuild) {
            Write-Host ''
            Write-Host '[X] Operation aborted: No system changes were made.' -ForegroundColor Red
        }
        Exit-Session 0
    }

    switch ($Shell) {
        'Apply'  { Install-ShellIntegration }
        'Revert' { Revert-ShellIntegration }
        'Clean'  { Clear-ShellIntegration }
    }

    $countdownColor = if ($Shell -eq 'Apply') { 'Green' } else { 'DarkYellow' }
    Start-ExitCountdown -seconds 3 -color $countdownColor
    Exit-Session 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if (Test-Interactive) { Read-Host "`nPress Enter to close" | Out-Null }
    exit 1
}
