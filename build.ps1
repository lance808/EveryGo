[CmdletBinding()]
param(
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $root 'dist'
$preservedData = $null
$buildFailed = $false
Push-Location $root
try {
    Write-Host '=== EveryGo build ===' -ForegroundColor Cyan

    $fwDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path $fwDir)) {
        throw "找不到 .NET Framework 4.x 目录：$fwDir"
    }
    $wpfDir = Join-Path $fwDir 'WPF'

    $references = @(
        (Join-Path $wpfDir 'PresentationFramework.dll'),
        (Join-Path $wpfDir 'PresentationCore.dll'),
        (Join-Path $wpfDir 'WindowsBase.dll'),
        (Join-Path $fwDir 'System.Xaml.dll'),
        (Join-Path $fwDir 'System.dll'),
        (Join-Path $fwDir 'System.Core.dll'),
        (Join-Path $fwDir 'System.Drawing.dll'),
        (Join-Path $fwDir 'System.Web.Extensions.dll'),
        (Join-Path $fwDir 'System.Windows.Forms.dll')
    )
    foreach ($reference in $references) {
        if (-not (Test-Path $reference)) {
            throw "缺少必需程序集：$reference"
        }
    }

    $csc = $null
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $installations = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.Roslyn.Compiler -property installationPath 2>$null
        foreach ($installation in $installations) {
            $trimmed = $installation.Trim()
            if ($trimmed.Length -eq 0) { continue }
            $candidate = Join-Path $trimmed 'MSBuild\Current\Bin\Roslyn\csc.exe'
            if (Test-Path $candidate) {
                $csc = $candidate
                break
            }
        }
    }

    if (-not $csc) {
        $fallbackCandidates = @()
        if ($env:ProgramFiles) {
            $fallbackCandidates += Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
        }
        if (${env:ProgramFiles(x86)}) {
            $fallbackCandidates += Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
        }
        $fallbackCandidates += 'D:\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
        $fallbackCandidates += 'C:\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
        foreach ($candidate in $fallbackCandidates) {
            if (Test-Path $candidate) {
                $csc = $candidate
                break
            }
        }
    }

    if (-not $csc) {
        $systemCsc = Join-Path $fwDir 'csc.exe'
        if (Test-Path $systemCsc) {
            $csc = $systemCsc
            Write-Host '未找到 Roslyn csc，回退到系统 csc（C# 5 兼容模式）。' -ForegroundColor Yellow
        }
    }
    if (-not $csc) {
        throw '找不到 C# 编译器：请安装 VS 2022 Build Tools（含 Roslyn），或确认系统 .NET Framework 编译器可用。'
    }
    Write-Host ("编译器：" + $csc)

    $srcDir = Join-Path $root 'src'
    if (-not (Test-Path $srcDir)) {
        throw "缺少源码目录：$srcDir"
    }
    $sourceFiles = @(Get-ChildItem -Path $srcDir -Filter *.cs -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName })
    if ($sourceFiles.Count -eq 0) {
        throw "src 目录下没有 .cs 源文件：$srcDir"
    }
    Write-Host ("源文件：" + $sourceFiles.Count + " 个")

    $distDir = Join-Path $root 'dist'
    if (Test-Path $distDir) {
        $runtimeData = Join-Path $distDir 'EveryGo.data'
        if (Test-Path $runtimeData) {
            $preservedData = Join-Path $env:TEMP ('EveryGo.data.preserve-' + [guid]::NewGuid().ToString('N'))
            Move-Item -Path $runtimeData -Destination $preservedData -Force
            Write-Host ("运行数据临时移出：" + $runtimeData)
        }
        Remove-Item -Path $distDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $distDir | Out-Null
    $exePath = Join-Path $distDir 'EveryGo.exe'

    $arguments = @(
        '/nologo',
        '/target:winexe',
        '/platform:x64',
        '/optimize+',
        '/langversion:5',
        '/warn:4',
        '/codepage:65001',
        '/utf8output',
        ('/out:' + $exePath),
        ('/win32manifest:' + (Join-Path $root 'app.manifest'))
    )
    foreach ($reference in $references) {
        $arguments += ('/reference:' + $reference)
    }
    $arguments += $sourceFiles

    & $csc $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "编译失败，csc 退出码 $LASTEXITCODE"
    }

    $files = @(Get-ChildItem -Path $distDir -Force)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'EveryGo.exe') {
        throw ("dist 目录应只包含 EveryGo.exe，实际：" + (($files | ForEach-Object { $_.Name }) -join ', '))
    }

    $exe = Get-Item -Path $exePath
    $sizeKB = [math]::Round($exe.Length / 1KB, 1)
    if ($exe.Length -ge 2MB) {
        throw ("产物超过 2 MB 限制：" + $sizeKB + " KB")
    }

    $hash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash
    Write-Host ("产物：" + $exe.FullName)
    Write-Host ("大小：" + $sizeKB + " KB（限制 < 2048 KB）")
    Write-Host ("SHA256：" + $hash)

    if ($SelfTest) {
        Write-Host ''
        Write-Host '=== 自检 ===' -ForegroundColor Cyan
        $reportPath = Join-Path $env:TEMP 'EveryGo.selftest.log'
        if (Test-Path $reportPath) {
            Remove-Item -Path $reportPath -Force
        }
        $process = Start-Process -FilePath $exePath -ArgumentList '--selftest' -Wait -PassThru
        Write-Host ("自检退出码：" + $process.ExitCode)
        if (Test-Path $reportPath) {
            Get-Content -Path $reportPath -Encoding UTF8 | ForEach-Object { Write-Host $_ }
        }
        else {
            throw "自检未生成报告：$reportPath"
        }
        if ($process.ExitCode -ne 0) {
            throw ("自检失败，退出码 " + $process.ExitCode)
        }
    }

    Write-Host ''
    Write-Host '构建完成（未启动 GUI）。' -ForegroundColor Green
}
catch {
    Write-Host ("构建失败：" + $_.Exception.Message) -ForegroundColor Red
    $buildFailed = $true
}
finally {
    if ($preservedData -and (Test-Path $preservedData)) {
        if (-not (Test-Path $distDir)) {
            New-Item -ItemType Directory -Path $distDir | Out-Null
        }
        $restoreTarget = Join-Path $distDir 'EveryGo.data'
        if (-not (Test-Path $restoreTarget)) {
            Move-Item -Path $preservedData -Destination $restoreTarget -Force
        }
    }
    Pop-Location
}
if ($buildFailed) {
    exit 1
}
exit 0
