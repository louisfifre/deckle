# Monitor RAM + VRAM + GPU compute toutes les 500 ms, sortie JSONL.
#
# Usage :
#   pwsh _monitor_gpu.ps1 > monitor.jsonl
#   pwsh _monitor_gpu.ps1 -OutFile monitor.jsonl
#
# Pourquoi PowerShell : Get-Counter coûte cher en spawn. Une boucle pwsh
# unique sample en continu sans re-spawn → mesure honnête, charge CPU
# minimale. Le Python du bench tourne à côté et lit ce log post-mortem.
#
# Format ligne JSONL :
#   {"ts":"2026-05-24T12:34:56.789",
#    "ram_used_mb": 14234.5, "ram_total_mb": 31900, "ram_pct": 44.6,
#    "vram_dedicated_mb": 14080.0,
#    "vram_shared_mb": 4800.0,
#    "gpu_compute_pct": 87.3,
#    "top_gpu_proc": "llama-mtmd-cli.exe (6234 MB)"}

param(
    [string]$OutFile = "",
    [int]$IntervalMs = 500
)

$ErrorActionPreference = "Continue"

if ($OutFile) {
    # Truncate + open
    "" | Out-File -FilePath $OutFile -Encoding utf8
    $writer = [System.IO.StreamWriter]::new($OutFile, $false, [System.Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
}

Write-Host "→ Monitor started, interval=${IntervalMs}ms, output=$(if ($OutFile) { $OutFile } else { '<stdout>' })"

try {
    while ($true) {
        $ts = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss.fff")

        # RAM via CIM
        $os = Get-CimInstance Win32_OperatingSystem
        $ramUsedMb  = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1024, 1)
        $ramTotalMb = [math]::Round($os.TotalVisibleMemorySize / 1024, 1)
        $ramPct     = [math]::Round($ramUsedMb / $ramTotalMb * 100, 1)

        # VRAM dédiée (somme tous adaptateurs > 0)
        $vramDedMb = 0.0
        $vramShrMb = 0.0
        try {
            $cs = Get-Counter '\GPU Adapter Memory(*)\Dedicated Usage' -ErrorAction Stop
            $vramDedMb = [math]::Round(($cs.CounterSamples | Measure-Object CookedValue -Sum).Sum / 1MB, 1)
        } catch {}
        try {
            $cs = Get-Counter '\GPU Adapter Memory(*)\Shared Usage' -ErrorAction Stop
            $vramShrMb = [math]::Round(($cs.CounterSamples | Measure-Object CookedValue -Sum).Sum / 1MB, 1)
        } catch {}

        # Compute % (max sur tous engines compute)
        $gpuComputePct = 0.0
        try {
            $cs = Get-Counter '\GPU Engine(*engtype_Compute)\Utilization Percentage' -ErrorAction Stop
            $gpuComputePct = [math]::Round(($cs.CounterSamples | Measure-Object CookedValue -Sum).Sum, 1)
        } catch {}

        # Top processus GPU par VRAM dédiée
        $topProc = ""
        try {
            $cs = Get-Counter '\GPU Process Memory(*)\Dedicated Usage' -ErrorAction Stop
            $top = $cs.CounterSamples | Where-Object { $_.CookedValue -gt 100MB } |
                Sort-Object -Property CookedValue -Descending | Select-Object -First 1
            if ($top) {
                # Path: \\machine\gpu process memory(pid_XXXX_luid_..._phys_0)\dedicated usage
                if ($top.Path -match 'pid_(\d+)_') {
                    $pid_ = [int]$matches[1]
                    $proc = Get-Process -Id $pid_ -ErrorAction SilentlyContinue
                    $name = if ($proc) { $proc.ProcessName } else { "pid=$pid_" }
                    $mb = [math]::Round($top.CookedValue / 1MB, 0)
                    $topProc = "$name ($mb MB)"
                }
            }
        } catch {}

        $entry = [ordered]@{
            ts                = $ts
            ram_used_mb       = $ramUsedMb
            ram_total_mb      = $ramTotalMb
            ram_pct           = $ramPct
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

        Start-Sleep -Milliseconds $IntervalMs
    }
} finally {
    if ($OutFile) { $writer.Close() }
}
