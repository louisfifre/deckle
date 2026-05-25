# Monitor système : RAM, CPU, disk, VRAM, GPU compute — sortie JSONL.
#
# Usage :
#   pwsh gpu_monitor.ps1 -OutFile monitor.jsonl
#
# Pourquoi PowerShell : Get-Counter coûte cher en spawn. Une boucle pwsh
# unique sample en continu sans re-spawn → mesure honnête, charge CPU
# minimale. Le Python du bench tourne à côté et lit ce log post-mortem.
#
# Cadence : `Get-Counter` impose un sample interval minimum de 1 s par
# appel (limite Windows perfcounter API). Le script batche tous les
# compteurs en un seul appel donc on paye 1 s/itération total au lieu
# de N s (mesure : N=4 séparés → ~3.5 s/itération avant batching, ~1 s
# après). `IntervalMs` est un sleep additionnel après chaque sample —
# laisser à 0 pour la cadence max (1 Hz).
#
# Format ligne JSONL :
#   {"ts":"2026-05-25T12:34:56.789",
#    "ram_used_mb": 14234.5, "ram_total_mb": 31900, "ram_pct": 44.6,
#    "cpu_pct": 12.3,
#    "disk_read_mb_s": 8.4, "disk_write_mb_s": 2.1, "disk_queue_length": 0.0,
#    "vram_dedicated_mb": 14080.0, "vram_shared_mb": 4800.0,
#    "gpu_compute_pct": 87.3,
#    "top_gpu_proc": "llama-mtmd-cli.exe (6234 MB)"}

param(
    [string]$OutFile  = "",
    [int]$IntervalMs  = 0
)

$ErrorActionPreference = "Continue"

# Tous les compteurs en un seul batch. Get-Counter accepte une liste et
# ne paie qu'un seul sample interval pour l'ensemble — gain x3-x4 sur
# la cadence par rapport à des appels séquentiels.
$CounterPaths = @(
    '\GPU Adapter Memory(*)\Dedicated Usage',
    '\GPU Adapter Memory(*)\Shared Usage',
    '\GPU Engine(*engtype_Compute)\Utilization Percentage',
    '\GPU Process Memory(*)\Dedicated Usage',
    '\Processor(_Total)\% Processor Time',
    '\PhysicalDisk(_Total)\Disk Read Bytes/sec',
    '\PhysicalDisk(_Total)\Disk Write Bytes/sec',
    '\PhysicalDisk(_Total)\Current Disk Queue Length'
)

if ($OutFile) {
    "" | Out-File -FilePath $OutFile -Encoding utf8
    $writer = [System.IO.StreamWriter]::new($OutFile, $false, [System.Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
}

Write-Host "→ Monitor started, sample~1s (Get-Counter min), extra-sleep=${IntervalMs}ms, output=$(if ($OutFile) { $OutFile } else { '<stdout>' })"

try {
    while ($true) {
        $ts = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss.fff")

        # RAM via CIM (fast, ~40 ms).
        $os = Get-CimInstance Win32_OperatingSystem
        $ramUsedMb  = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1024, 1)
        $ramTotalMb = [math]::Round($os.TotalVisibleMemorySize / 1024, 1)
        $ramPct     = [math]::Round($ramUsedMb / $ramTotalMb * 100, 1)

        # Batch perfcounters. SilentlyContinue : un counter sans instance
        # active (e.g. GPU Engine compute quand le GPU est idle) est juste
        # absent du résultat — pas une erreur fatale.
        $samples = @()
        try {
            $cs = Get-Counter -Counter $CounterPaths -ErrorAction SilentlyContinue
            if ($cs) { $samples = $cs.CounterSamples }
        } catch {}

        # Helper local : filtre par sous-chaîne sur le path (insensible à
        # la casse, le path est en lowercase côté Get-Counter).
        function _sum_matching($pattern) {
            ($samples | Where-Object { $_.Path -like $pattern } |
             Measure-Object CookedValue -Sum).Sum
        }
        function _first_matching($pattern) {
            ($samples | Where-Object { $_.Path -like $pattern } |
             Select-Object -First 1).CookedValue
        }

        $vramDedMb     = [math]::Round((_sum_matching '*gpu adapter memory(*)\dedicated usage') / 1MB, 1)
        $vramShrMb     = [math]::Round((_sum_matching '*gpu adapter memory(*)\shared usage') / 1MB, 1)
        $gpuComputePct = [math]::Round((_sum_matching '*gpu engine(*)\utilization percentage'), 1)

        $cpuPct        = [math]::Round((_first_matching '*processor(_total)\% processor time'), 1)
        $diskReadMbS   = [math]::Round((_first_matching '*physicaldisk(_total)\disk read bytes/sec') / 1MB, 2)
        $diskWriteMbS  = [math]::Round((_first_matching '*physicaldisk(_total)\disk write bytes/sec') / 1MB, 2)
        $diskQueue     = [math]::Round((_first_matching '*physicaldisk(_total)\current disk queue length'), 2)

        # Top processus GPU par VRAM dédiée. Filtre > 100 MB pour ignorer
        # les dwm/explorer qui polluent la liste, puis garde celui qui
        # consomme le plus.
        $topProc = ""
        $procSample = $samples | Where-Object { $_.Path -like '*gpu process memory(*)\dedicated usage' -and $_.CookedValue -gt 100MB } |
            Sort-Object -Property CookedValue -Descending | Select-Object -First 1
        if ($procSample) {
            # Path : \\machine\gpu process memory(pid_XXXX_luid_..._phys_0)\dedicated usage
            if ($procSample.Path -match 'pid_(\d+)_') {
                $pid_ = [int]$matches[1]
                $proc = Get-Process -Id $pid_ -ErrorAction SilentlyContinue
                $name = if ($proc) { $proc.ProcessName } else { "pid=$pid_" }
                $mb = [math]::Round($procSample.CookedValue / 1MB, 0)
                $topProc = "$name ($mb MB)"
            }
        }

        $entry = [ordered]@{
            ts                = $ts
            ram_used_mb       = $ramUsedMb
            ram_total_mb      = $ramTotalMb
            ram_pct           = $ramPct
            cpu_pct           = $cpuPct
            disk_read_mb_s    = $diskReadMbS
            disk_write_mb_s   = $diskWriteMbS
            disk_queue_length = $diskQueue
            vram_dedicated_mb = $vramDedMb
            vram_shared_mb    = $vramShrMb
            gpu_compute_pct   = $gpuComputePct
            top_gpu_proc      = $topProc
        }
        $json = $entry | ConvertTo-Json -Compress

        if ($OutFile) {
            $writer.WriteLine($json)
        } else {
            Write-Output $json
        }

        if ($IntervalMs -gt 0) { Start-Sleep -Milliseconds $IntervalMs }
    }
} finally {
    if ($OutFile) { $writer.Close() }
}
