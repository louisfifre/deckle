# Two-dimensional grid picker.
function Write-GridLine {
    param(
        [int]$Top, [int]$Index, [object[]]$Body, [hashtable]$ColW, [int]$PrefixW,
        [int]$InnerWidth,
        [int]$ActiveBodyIndex, [int]$ActiveCol
    )
    $entry = $Body[$Index]
    Write-MenuLinePrefix -Row ($Top + $Index)
    $written = 0

    if ($entry.Kind -eq 'title') {
        $label = ' ' + ([string]$entry.Text).ToUpperInvariant() + ' '
        Write-MenuContentSegment -Text $label -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor Magenta -BackgroundColor $null
        $rule = New-MenuRule -MaxWidth ($InnerWidth - $written) -Style Section
        Write-MenuContentSegment -Text $rule -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor Gray -BackgroundColor $null
    } elseif ($entry.Kind -eq 'blank') {
        # Keep the row inside the frame intentionally empty.
    } else {
        # 'row'
        Write-MenuContentSegment -Text '  ' -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor $null -BackgroundColor $null
        if ($PrefixW -gt 0) {
            $p = ([string]$entry.Prefix).PadRight($PrefixW)
            Write-MenuContentSegment -Text $p -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor Cyan -BackgroundColor $null
        }
        for ($c = 0; $c -lt $entry.Cells.Count; $c++) {
            $cell = $entry.Cells[$c]
            $selected = (($Index -eq $ActiveBodyIndex) -and ($c -eq $ActiveCol))
            $label = [string]$cell.Label
            $txt = "  $label"
            $txt = $txt.PadRight($ColW[$c])
            $role = Get-MenuCellRole -Cell $cell
            $colors = Get-MenuRoleColor -Role $role -Selected:$selected
            Write-MenuContentSegment -Text $txt -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor $colors.Foreground -BackgroundColor $colors.Background
        }
    }
    Write-MenuLineRemainder -InnerWidth $InnerWidth -Written $written
}

function Invoke-GridLoop {
    param(
        [string]$Header,
        [object[]]$Rows,
        [string]$Footer,
        [int]$StartSel = 0,
        [int]$StartCol = 0,
        [ValidateSet('Cancel', 'Ignore')]
        [string]$EscapeAction = 'Cancel',
        [switch]$ClearScreen
    )
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        throw 'Invoke-GridLoop requires an interactive console (input or output is redirected).'
    }

    $GAP = 3
    $body = @()
    $sel  = @()          # selectable rows: @{ BodyIndex; NCells }
    $prefixW = 0
    $colW = @{}

    foreach ($r in $Rows) {
        if ($r.ContainsKey('Title')) {
            $body += @{ Kind = 'title'; Text = [string]$r['Title'] }
        } elseif ($r.ContainsKey('Cells')) {
            $prefix = if ($r.ContainsKey('Prefix') -and $r['Prefix']) { [string]$r['Prefix'] } else { '' }
            if ($prefix.Length -gt $prefixW) { $prefixW = $prefix.Length }
            $cells = @($r['Cells'])
            if ($cells.Count -eq 0) { throw 'Invoke-GridLoop: a row has empty Cells; use a Blank row for separators.' }
            for ($c = 0; $c -lt $cells.Count; $c++) {
                $len = ([string]$cells[$c].Label).Length + 2
                if (-not $colW.ContainsKey($c) -or $len -gt $colW[$c]) { $colW[$c] = $len }
            }
            $body += @{ Kind = 'row'; Prefix = $prefix; Cells = $cells }
            $sel  += @{ BodyIndex = ($body.Count - 1); NCells = $cells.Count }
        } else {
            $body += @{ Kind = 'blank' }
        }
    }
    if ($sel.Count -eq 0) { return $null }
    if ($prefixW -gt 0) { $prefixW += $GAP }
    foreach ($k in @($colW.Keys)) { $colW[$k] = $colW[$k] + $GAP }

    $selIdx = [Math]::Min([Math]::Max($StartSel, 0), $sel.Count - 1)
    $colIdx = [Math]::Min([Math]::Max($StartCol, 0), $sel[$selIdx].NCells - 1)

    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $body.Count -ClearScreen:$ClearScreen
    $top = $viewport.BodyTop

    $render = {
        for ($i = 0; $i -lt $body.Count; $i++) {
            Write-GridLine -Top $top -Index $i -Body $body -ColW $colW -PrefixW $prefixW `
                -InnerWidth $viewport.InnerWidth `
                -ActiveBodyIndex $sel[$selIdx].BodyIndex -ActiveCol $colIdx
        }
    }

    [Console]::CursorVisible = $false
    try {
        & $render
        while ($true) {
            $key = [Console]::ReadKey($true)
            $prevSelIdx = $selIdx
            $prevColIdx = $colIdx
            switch ($key.Key) {
                'UpArrow' {
                    if ($selIdx -gt 0) {
                        $selIdx--
                        if ($colIdx -gt $sel[$selIdx].NCells - 1) { $colIdx = $sel[$selIdx].NCells - 1 }
                    }
                }
                'DownArrow' {
                    if ($selIdx -lt $sel.Count - 1) {
                        $selIdx++
                        if ($colIdx -gt $sel[$selIdx].NCells - 1) { $colIdx = $sel[$selIdx].NCells - 1 }
                    }
                }
                'LeftArrow'  { if ($colIdx -gt 0) { $colIdx-- } }
                'RightArrow' { if ($colIdx -lt $sel[$selIdx].NCells - 1) { $colIdx++ } }
                'Enter' {
                    Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom
                    return $body[$sel[$selIdx].BodyIndex].Cells[$colIdx].Value
                }
                'Escape' {
                    if ($EscapeAction -eq 'Ignore') { continue }
                    Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom
                    return $null
                }
            }
            if ($selIdx -ne $prevSelIdx -or $colIdx -ne $prevColIdx) { & $render }
        }
    } finally {
        [Console]::CursorVisible = $true
    }
}

function Select-Grid {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer,
        [int]$StartSel = 0,
        [int]$StartCol = 0,
        [ValidateSet('Cancel', 'Ignore')]
        [string]$EscapeAction = 'Cancel',
        [switch]$ClearScreen
    )
    return Invoke-GridLoop -Header $Header -Rows $Rows -Footer $Footer -StartSel $StartSel -StartCol $StartCol -EscapeAction $EscapeAction -ClearScreen:$ClearScreen
}
