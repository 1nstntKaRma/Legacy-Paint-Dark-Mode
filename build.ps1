# Builds Paint Dark Mode -> bin\PaintDark.exe, then offers Explorer integration.
#
# Uses the Roslyn compiler shipped with any installed .NET SDK and targets .NET Framework 4.8,
# which is part of every Windows 10 / 11 installation (nothing extra to install for users).
#
#   .\build.ps1                     build, then ask about Explorer integration
#   .\build.ps1 -Shell Yes          build, then "Edit" -> Paint Dark Mode, hide "Edit with Paint 3D"
#   .\build.ps1 -Shell Revert       build, then "Edit" -> original Paint, hide "Edit with Paint 3D"
#   .\build.ps1 -Shell No           build only
#   .\build.ps1 -NoBuild -Shell ... only change the Explorer integration
#   .\build.ps1 -NoBuild -Shell Clean   remove every change this script ever made (factory state)
param(
    [switch]$Debug,
    [ValidateSet('Ask', 'Yes', 'Revert', 'No', 'Clean')][string]$Shell = 'Ask',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$obj = Join-Path $root 'obj'
$exe = Join-Path $bin 'PaintDark.exe'

# ======================================================================================= build
function Invoke-Build {
    New-Item -ItemType Directory -Force $bin, $obj | Out-Null
    $csc = Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Roslyn\bincore\csc.dll" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.FullName -replace '.*\\sdk\\([\d\.]+)[^\\]*\\.*', '$1') } -Descending | Select-Object -First 1
    if (-not $csc) { throw 'No .NET SDK found (needed only to build).' }

    $fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    if (-not (Test-Path $fw)) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }
    $refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF\PresentationCore.dll', 'WPF\WindowsBase.dll', 'System.Xaml.dll') |
        ForEach-Object { "-r:$fw\$_" }
    $files = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
    $common = @('-nologo', '-noconfig', '-nostdlib', '-target:winexe', '-platform:anycpu', '-unsafe', '-langversion:latest',
                '-deterministic', '-nowarn:CS0169,CS0414,CS0649,CS0168,CS0219,CS1998,CS0108,CS1690', "-win32manifest:$src\app.manifest")
    if ($Debug) { $common += @('-debug:portable', '-optimize-', '-define:DEBUG') } else { $common += @('-optimize+', '-debug-') }

    function Compile($out, $extra) {
        & dotnet $csc.FullName @common @extra "-out:$out" @refs @files
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }
    }

    $running = Get-Process PaintDark -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
    if ($running) { throw "Paint Dark Mode is running from $exe. Close it and build again." }

    $ico = Join-Path $obj 'app.ico'
    if (-not (Test-Path $ico)) {
        # first pass without an icon, then let the app draw its own icon
        $tmp = Join-Path $obj 'PaintDark.noicon.exe'
        Compile $tmp @()
        & $tmp --export-icon $ico | Out-Null
        Start-Sleep -Milliseconds 300
    }
    Compile $exe @("-win32icon:$ico")
    $size = [math]::Round((Get-Item $exe).Length / 1KB)
    Write-Host "Built $exe ($size KB) with $($csc.Directory.Parent.Parent.Name)" -ForegroundColor Green
}

# ======================================================================================= Explorer integration
# Everything is written per user (HKCU\Software\Classes), which Windows merges over the machine-wide
# registrations, so no administrator rights are needed and nothing system-wide is deleted.
# Every key we create carries a 'PaintDarkMode' marker so Revert / Clean only touch our own changes.
# (the .NET registry API is used directly: PowerShell's provider is very slow on the merged HKCR view)
$HKCU = [Microsoft.Win32.Registry]::CurrentUser
$HKCR = [Microsoft.Win32.Registry]::ClassesRoot
$ClassesRoot = 'Software\Classes'
$Marker = 'PaintDarkMode'
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
    # creates the key chain, marking only the parts that are new so they can be removed later
    $cur = $ClassesRoot
    foreach ($p in $path -split '\\') {
        $cur = "$cur\$p"
        $k = $HKCU.OpenSubKey($cur, $true)
        if (-not $k) { $k = $HKCU.CreateSubKey($cur); $k.SetValue($Marker, 'created') }
        $k.Close()
    }
}

function Remove-OwnedKey($path) {
    # removes our marked keys bottom-up, keeping anything the user or other apps own
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
        if ($i -eq $parts.Count) { $HKCU.DeleteSubKeyTree($rel); continue }    # the verb key itself, with its command
        if ($subs -eq 0 -and $others -eq 0 -and -not $hasDefault) { $HKCU.DeleteSubKey($rel) } else { break }
    }
}

function Set-EditCommand($verbPath, $command, $tag) {
    if (($null -ne (Get-UserValue "$verbPath\command" '')) -and ($null -eq (Get-UserValue $verbPath $Marker))) {
        # a per-user override that is not ours: remember it so Revert can put it back
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
            # put the user's own override back exactly as it was
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
    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\mspaint.exe'   # Windows 11: Paint app alias
    if (Test-Path $alias) { return $alias }
    return $null
}

function Install-ShellIntegration {
    if (-not (Test-Path $exe)) { throw "Build first: $exe was not found." }
    $cmd = "`"$exe`" `"%1`""
    Write-Host "Explorer integration -> Paint Dark Mode" -ForegroundColor Cyan
    Remove-EditOverrides
    foreach ($v in $EditVerbs) {
        # Paint's own ProgIDs only get an override where they exist on this system
        if ($v -like 'SystemFileAssociations*' -or (Test-MergedKey ($v -replace '\\shell\\edit$', ''))) { Set-EditCommand $v $cmd 'darkpaint' }
    }
    Write-Host "  'Edit' on pictures now opens $exe"
    Hide-Paint3D
}

function Revert-ShellIntegration {
    Write-Host "Explorer integration -> original Paint" -ForegroundColor Cyan
    Remove-EditOverrides
    $c = Get-MergedCommand 'SystemFileAssociations\image\shell\edit'
    $target = Get-CommandExe $c
    if ($c -and $target -and (Test-Path $target)) { Write-Host "  'Edit' on pictures opens the original Paint again ($c)" }
    else {
        # this Windows has no built-in "Edit" verb (e.g. some Windows 11 builds): add it back for Paint
        $paint = Find-LegacyPaint
        if ($paint) {
            Set-EditCommand 'SystemFileAssociations\image\shell\edit' "`"$paint`" `"%1`"" 'restored'
            Write-Host "  'Edit' added back for pictures -> $paint"
        }
        else { Write-Host "  Paint (mspaint.exe) was not found on this system, so 'Edit' was left out." -ForegroundColor Yellow }
    }
    Hide-Paint3D
}

function Clear-ShellIntegration {
    Write-Host "Explorer integration -> removing every change made by this script" -ForegroundColor Cyan
    Remove-EditOverrides
    Show-Paint3D
    Write-Host "  Done. Explorer uses the system defaults again."
}

function Update-Explorer {
    # tell Explorer that file associations changed, so the context menu refreshes immediately
    if (-not ('PaintDarkBuild.Shell' -as [type])) {
        Add-Type -Namespace 'PaintDarkBuild' -Name 'Shell' -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, System.IntPtr a, System.IntPtr b);'
    }
    [PaintDarkBuild.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Test-Interactive {
    try { return [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and $Host.Name -eq 'ConsoleHost' } catch { return $false }
}

function Test-OwnConsole {
    # true when this script got its own console window (e.g. "Run with PowerShell"), so we pause before it closes
    try {
        if (-not ('PaintDarkBuild.Console' -as [type])) {
            Add-Type -Namespace 'PaintDarkBuild' -Name 'Console' -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint GetConsoleProcessList(uint[] list, uint count);'
        }
        $list = New-Object 'uint[]' 8
        return [PaintDarkBuild.Console]::GetConsoleProcessList($list, 8) -le 1
    }
    catch { return $false }
}

# ======================================================================================= main
$ownConsole = Test-OwnConsole
$asked = $false
try {
    if (-not $NoBuild) { Invoke-Build }

    if ($Shell -eq 'Ask') {
        if (Test-Interactive) {
            $choices = [System.Management.Automation.Host.ChoiceDescription[]]@(
                (New-Object System.Management.Automation.Host.ChoiceDescription '&Yes', 'Right-click > Edit opens Paint Dark Mode, and "Edit with Paint 3D" is removed.'),
                (New-Object System.Management.Automation.Host.ChoiceDescription '&Revert', 'Right-click > Edit opens the original Paint again, and "Edit with Paint 3D" is removed.'),
                (New-Object System.Management.Automation.Host.ChoiceDescription '&No', 'Leave the Explorer context menu unchanged.'))
            $msg = "`n  Yes    - 'Edit' opens Paint Dark Mode, 'Edit with Paint 3D' is removed`n" +
                   "  Revert - 'Edit' opens the original Paint again, 'Edit with Paint 3D' is removed`n" +
                   "  No     - leave the context menu unchanged`n"
            $pick = $Host.UI.PromptForChoice('Explorer right-click menu for pictures', $msg, $choices, 2)
            $asked = $true
            $Shell = @('Yes', 'Revert', 'No')[$pick]
        }
        else { $Shell = 'No' }
    }

    switch ($Shell) {
        'Yes' { Install-ShellIntegration; Update-Explorer }
        'Revert' { Revert-ShellIntegration; Update-Explorer }
        'Clean' { Clear-ShellIntegration; Update-Explorer }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($ownConsole -and (Test-Interactive) -and $Shell -eq 'Ask') { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}
if ($ownConsole -and $asked) { Read-Host "`nDone. Press Enter to close" | Out-Null }
