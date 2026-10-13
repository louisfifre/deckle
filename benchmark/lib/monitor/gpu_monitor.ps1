# System monitor: RAM, CPU, disk, VRAM and GPU compute, one JSONL line per sample.
#
# Usage:
#   pwsh gpu_monitor.ps1 -OutFile monitor.jsonl
#
# One long-lived pwsh loop, because spawning Get-Counter per sample is
# expensive and would load the machine being measured. The bench's Python
# runs beside it and reads this log afterwards.
#
# Get-Counter takes at least one 1 s sample interval per call, so the
# fastest cadence is 1 Hz. `IntervalMs` adds a sleep after each sample;
# leave it at 0 for that cadence.

param(
    [string]$OutFile  = "",
    [int]$IntervalMs  = 0
)

$ErrorActionPreference = "Continue"

# Every counter goes in one Get-Counter call: a list pays a single sample
# interval, where one call per counter would pay one each.
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

        $os = Get-CimInstance Win32_OperatingSystem
        $ramUsedMb  = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1024, 1)
        $ramTotalMb = [math]::Round($os.TotalVisibleMemorySize / 1024, 1)
        $ramPct     = [math]::Round($ramUsedMb / $ramTotalMb * 100, 1)

        # A counter with no live instance (GPU Engine compute while the GPU
        # is idle) is simply missing from the result, not an error.
        $samples = @()
        try {
            $cs = Get-Counter -Counter $CounterPaths -ErrorAction SilentlyContinue
            if ($cs) { $samples = $cs.CounterSamples }
        } catch {}

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

        # The 100 MB floor drops dwm and explorer, which always hold some VRAM.
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
