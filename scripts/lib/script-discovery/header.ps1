# Script Header reader: comment-based help, param block and #Requires, through
# the PowerShell parser only, so reading a script never executes it.

$script:DeckleHelpKeywordPattern = '^\s*(<#|#)?\s*\.(SYNOPSIS|DESCRIPTION|NOTES|PARAMETER|EXAMPLE|INPUTS|OUTPUTS|LINK|COMPONENT|ROLE|FUNCTIONALITY)\b'

function ConvertTo-DeckleHelpSentence {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $lines = @($Text -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    return ($lines -join ' ')
}

function Get-DeckleAttributeName {
    param([Parameter(Mandatory)][object]$Attribute)

    $name = $Attribute.TypeName.Name
    if ($name.EndsWith('Attribute', [StringComparison]::OrdinalIgnoreCase)) {
        $name = $name.Substring(0, $name.Length - 'Attribute'.Length)
    }
    return $name
}

function Get-DeckleScriptParameters {
    param([AllowNull()][object]$ParamBlock)

    $parameters = [System.Collections.Generic.List[object]]::new()
    if ($null -eq $ParamBlock) { return @() }

    foreach ($parameterAst in $ParamBlock.Parameters) {
        $typeConstraint = @($parameterAst.Attributes | Where-Object {
            $_ -is [System.Management.Automation.Language.TypeConstraintAst]
        }) | Select-Object -First 1
        $typeName = if ($null -ne $typeConstraint) { $typeConstraint.TypeName.FullName } else { $null }

        $validateSet = $null
        $isMandatory = $false
        foreach ($attribute in $parameterAst.Attributes) {
            if ($attribute -isnot [System.Management.Automation.Language.AttributeAst]) { continue }
            $attributeName = Get-DeckleAttributeName -Attribute $attribute
            if ($attributeName -eq 'ValidateSet') {
                $values = foreach ($argument in $attribute.PositionalArguments) {
                    try { [string]$argument.SafeGetValue() } catch { $argument.Extent.Text }
                }
                $validateSet = @($values)
            } elseif ($attributeName -eq 'Parameter') {
                foreach ($named in $attribute.NamedArguments) {
                    if ($named.ArgumentName -ne 'Mandatory') { continue }
                    if ($named.ExpressionOmitted) { $isMandatory = $true; continue }
                    try { $isMandatory = [bool]$named.Argument.SafeGetValue() } catch { $isMandatory = $true }
                }
            }
        }

        # An expression default such as (Join-Path ...) cannot be evaluated
        # without running code, so it never marks a default Variant.
        $hasDefault = $null -ne $parameterAst.DefaultValue
        $hasConstantDefault = $false
        $defaultValue = $null
        if ($hasDefault) {
            try {
                $defaultValue = $parameterAst.DefaultValue.SafeGetValue()
                $hasConstantDefault = $true
            } catch {
                $hasConstantDefault = $false
            }
        }

        $staticType = $parameterAst.StaticType
        $parameters.Add([pscustomobject]@{
            Name = $parameterAst.Name.VariablePath.UserPath
            TypeName = $typeName
            IsSwitch = $staticType -eq [System.Management.Automation.SwitchParameter]
            IsMandatory = $isMandatory
            HasDefault = $hasDefault
            IsArray = $staticType.IsArray
            ValidateSet = $validateSet
            HasConstantDefault = $hasConstantDefault
            DefaultValue = $defaultValue
        })
    }
    return @($parameters)
}

function Get-DeckleLeadingCommentTokens {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tokens)

    $comments = [System.Collections.Generic.List[object]]::new()
    foreach ($token in $Tokens) {
        $kind = [string]$token.Kind
        if ($kind -eq 'Comment') { $comments.Add($token); continue }
        if ($kind -eq 'NewLine' -or $kind -eq 'LineContinuation') { continue }
        break
    }
    return @($comments)
}

function Get-DeckleVoidedHeaderReason {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tokens)

    $leading = @(Get-DeckleLeadingCommentTokens -Tokens $Tokens)
    $helpToken = $null
    foreach ($comment in $leading) {
        foreach ($line in ($comment.Text -split '\r?\n')) {
            if ($line -match $script:DeckleHelpKeywordPattern) { $helpToken = $comment; break }
        }
        if ($null -ne $helpToken) { break }
    }
    if ($null -eq $helpToken) { return $null }

    $helpIndex = [Array]::IndexOf($Tokens, $helpToken)
    for ($index = $helpIndex - 1; $index -ge 0; $index--) {
        $previous = $Tokens[$index]
        if ([string]$previous.Kind -eq 'NewLine') { continue }
        if ($previous.Extent.EndLineNumber -eq $helpToken.Extent.StartLineNumber - 1) {
            if ($previous.Text -match '^#requires\b') {
                return 'the block is glued under a #Requires line'
            }
            return 'the block is glued under another comment'
        }
        break
    }

    # A function declared just after the block takes the help for itself.
    for ($index = $helpIndex + 1; $index -lt $Tokens.Count; $index++) {
        $next = $Tokens[$index]
        $nextKind = [string]$next.Kind
        if ($nextKind -eq 'NewLine' -or $nextKind -eq 'Comment') { continue }
        if ($nextKind -eq 'Function' -and $next.Extent.StartLineNumber - $helpToken.Extent.EndLineNumber -le 2) {
            return 'a function declared right after the block takes the help'
        }
        break
    }
    return 'unknown help keyword or dot-word line'
}

function ConvertTo-DeckleHeaderAction {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    $segments = @($Value -split '\|' | ForEach-Object { $_.Trim() })
    $label = $segments[0]
    if ([string]::IsNullOrWhiteSpace($label)) {
        return [pscustomobject]@{ Action = $null; Error = 'action line has an empty label' }
    }
    $arguments = if ($segments.Count -ge 2) { $segments[1] } else { '' }
    $danger = $false
    $confirmation = $null
    for ($index = 2; $index -lt $segments.Count; $index++) {
        $segment = $segments[$index]
        if ($segment -eq '') { continue }
        if ($segment -ieq 'danger') { $danger = $true; continue }
        if ($null -ne $confirmation) {
            return [pscustomobject]@{ Action = $null; Error = "action '$label' declares two confirmation sentences" }
        }
        $confirmation = $segment
    }
    return [pscustomobject]@{
        Action = [pscustomobject]@{
            Label = $label
            Arguments = $arguments
            Danger = $danger
            Confirmation = $confirmation
        }
        Error = $null
    }
}

function Read-DeckleScriptHeader {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $header = [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.ScriptDiscovery.Header'
        Path = $fullPath
        Name = [System.IO.Path]::GetFileNameWithoutExtension($fullPath)
        Status = $null
        Reason = $null
        Label = ''
        Description = ''
        Keys = @()
        Section = $null
        Order = $null
        Actions = @()
        VariantParameter = $null
        Variants = @()
        Parameters = @()
        Requirements = $null
        ParseErrors = @()
    }

    # ReadAllText decodes BOM-less UTF-8 correctly on every engine, which the
    # parser's own file reader does not guarantee under Windows PowerShell.
    $text = [System.IO.File]::ReadAllText($fullPath)
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($text, $fullPath, [ref]$tokens, [ref]$errors)
    $header.ParseErrors = @($errors)
    $header.Requirements = $ast.ScriptRequirements
    $parameters = @(Get-DeckleScriptParameters -ParamBlock $ast.ParamBlock)
    $header.Parameters = @($parameters | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            TypeName = $_.TypeName
            IsSwitch = $_.IsSwitch
            IsMandatory = $_.IsMandatory
            HasDefault = $_.HasDefault
        }
    })

    $help = $ast.GetHelpContent()
    if ($null -eq $help) {
        if (@($errors).Count -gt 0) {
            $header.Status = 'ParseError'
            $header.Reason = $errors[0].Message
            return $header
        }
        $voidedReason = Get-DeckleVoidedHeaderReason -Tokens @($tokens)
        if ($null -ne $voidedReason) {
            $header.Status = 'VoidedHeader'
            $header.Reason = $voidedReason
        } else {
            $header.Status = 'NoHeader'
            $header.Reason = 'no comment-based help block'
        }
        return $header
    }

    $header.Label = ConvertTo-DeckleHelpSentence -Text $help.Synopsis
    $header.Description = ConvertTo-DeckleHelpSentence -Text $help.Description
    $noteLines = if ($help.Notes) { @($help.Notes -split '\r?\n') } else { @() }
    $keys = @(Read-DeckleKeyValueLines -Lines $noteLines)
    $header.Keys = $keys
    $table = ConvertTo-DeckleKeyValueTable -Pairs $keys

    $invalid = [System.Collections.Generic.List[string]]::new()
    if ($header.Label -eq '') { $invalid.Add('the synopsis is empty') }

    if ($table.ContainsKey('section') -and -not [string]::IsNullOrWhiteSpace($table['section'])) {
        $header.Section = $table['section'].Trim()
    }
    if ($table.ContainsKey('order') -and -not [string]::IsNullOrWhiteSpace($table['order'])) {
        $orderValue = 0
        $orderText = $table['order'].Trim()
        if ([int]::TryParse($orderText, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$orderValue)) {
            $header.Order = $orderValue
        } else {
            $invalid.Add("order '$orderText' is not an integer")
        }
    }

    $actions = [System.Collections.Generic.List[object]]::new()
    $actionPairs = @($keys | Where-Object { $_.Key -eq 'action' })
    if ($actionPairs.Count -eq 0) {
        if ($header.Label -ne '') {
            $actions.Add([pscustomobject]@{ Label = $header.Label; Arguments = ''; Danger = $false; Confirmation = $null })
        }
    } else {
        foreach ($pair in $actionPairs) {
            $parsed = ConvertTo-DeckleHeaderAction -Value $pair.Value
            if ($null -ne $parsed.Error) { $invalid.Add($parsed.Error); continue }
            $actions.Add($parsed.Action)
        }
    }

    # Only a scalar [ValidateSet] is a Variant axis; an array-typed one is a
    # multi-value Filter that Preparation owns.
    $axes = @($parameters | Where-Object { $null -ne $_.ValidateSet -and -not $_.IsArray })
    if ($axes.Count -gt 1) {
        $invalid.Add("two scalar [ValidateSet] parameters: $(($axes | ForEach-Object Name) -join ', ')")
    } elseif ($axes.Count -eq 1) {
        $axis = $axes[0]
        $header.VariantParameter = $axis.Name
        $header.Variants = @($axis.ValidateSet | ForEach-Object {
            [pscustomobject]@{
                Value = $_
                IsDefault = $axis.HasConstantDefault -and [string]$axis.DefaultValue -eq $_
            }
        })
    }

    if ($invalid.Count -gt 0) {
        $header.Status = 'InvalidHeader'
        $header.Reason = $invalid -join '; '
        return $header
    }
    $header.Status = 'Described'
    $header.Actions = @($actions)
    return $header
}
