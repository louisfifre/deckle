# Query or set Windows feature configurations (the mechanism ViVeTool uses) through ntdll.
# Usage:
#   feature-flags.ps1 -Query -Ids 59213768,61161244
#   feature-flags.ps1 -Enable -Ids 59213768            # Runtime store only (lost at reboot)
#   feature-flags.ps1 -Reset  -Ids 59213768            # Runtime store only
# Struct layout and enums follow ViVe (GPL-3.0, github.com/thebookisclosed/ViVe), NativeStructs.cs / NativeEnums.cs.
param(
    [switch]$Query,
    [switch]$Enable,
    [switch]$Reset,
    [uint32[]]$Ids = @(59213768),
    [string]$LogFile
)
$ErrorActionPreference = 'Stop'
$code = @'
using System;
using System.Runtime.InteropServices;
public static class Fx {
    [StructLayout(LayoutKind.Sequential)]
    public struct CONFIG { public uint FeatureId; public uint CompactState; public uint VariantPayload; }
    [StructLayout(LayoutKind.Sequential)]
    public struct UPDATE {
        public uint FeatureId; public uint Priority; public uint EnabledState; public uint EnabledStateOptions;
        public uint Variant; public uint VariantPayloadKind; public uint VariantPayload; public uint Operation;
    }
    [DllImport("ntdll.dll")] public static extern int RtlQueryFeatureConfiguration(uint featureId, uint type, ref ulong changeStamp, out CONFIG cfg);
    [DllImport("ntdll.dll")] public static extern int RtlSetFeatureConfigurations(ref ulong previousChangeStamp, uint type, UPDATE[] updates, int count);
}
'@
Add-Type -TypeDefinition $code
$lines = New-Object System.Collections.Generic.List[string]
function Out-Line([string]$s) { $lines.Add($s); Write-Output $s }
function Describe-State([uint32]$cs) {
    $prio = $cs -band 0xF
    $state = ($cs -band 0x30) -shr 4
    $stateName = @('Default', 'Disabled', 'Enabled')[$state]
    $wexp = (($cs -band 0x40) -shr 6) -eq 1
    $variant = ($cs -band 0x3F00) -shr 8
    "state=$stateName priority=$prio wexp=$wexp variant=$variant"
}
function Query-All {
    foreach ($id in $Ids) {
        foreach ($store in @(@{ n = 'Runtime'; t = 1 }, @{ n = 'Boot'; t = 0 })) {
            $stamp = [uint64]0
            $cfg = New-Object Fx+CONFIG
            $hr = [Fx]::RtlQueryFeatureConfiguration($id, $store.t, [ref]$stamp, [ref]$cfg)
            if ($hr -eq 0) { Out-Line ("{0,-10} {1,-8} {2}" -f $id, $store.n, (Describe-State $cfg.CompactState)) }
            else { Out-Line ("{0,-10} {1,-8} no configuration (0x{2:X8})" -f $id, $store.n, $hr) }
        }
    }
}
if ($Query) { Query-All }
if ($Enable -or $Reset) {
    $updates = foreach ($id in $Ids) {
        $u = New-Object Fx+UPDATE
        $u.FeatureId = $id
        $u.Priority = 8            # User
        if ($Enable) { $u.EnabledState = 2; $u.Operation = 1 }   # Enabled, FeatureState
        else { $u.EnabledState = 0; $u.Operation = 4 }           # ResetState
        $u
    }
    $stamp = [uint64]0
    $hr = [Fx]::RtlSetFeatureConfigurations([ref]$stamp, 1, [Fx+UPDATE[]]$updates, $updates.Count)
    Out-Line ("RtlSetFeatureConfigurations(Runtime, {0}) -> 0x{1:X8} {2}" -f (($Ids | ForEach-Object { $_ }) -join ','), $hr, ($(if ($hr -eq 0) { 'OK' } else { 'FAILED' })))
    Out-Line '-- after:'
    Query-All
}
if ($LogFile) { $lines | Set-Content -Path $LogFile -Encoding utf8 }
