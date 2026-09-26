# Centurion PowerShell completion — add to your $PROFILE:
#   . .\docs\cli\completions\centurion.ps1

Register-ArgumentCompleter -Native -CommandName 'centurion' -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)
    $commands = @('asr','ocr','from-script','correct','translate','dub','convert','build',
                  'quality','pipeline-graph','validate','migrate','update','init','models','providers')
    $globalOpts = @('--json','--dry-run','--verbose','-v','--lang','--profile','--github-proxy','--no-github-proxy')
    $tokens = $commandAst.CommandElements | ForEach-Object { $_.ToString() }
    $cmd = if ($tokens.Count -gt 1) { $tokens[1] } else { $null }

    $candidates = switch ($cmd) {
        'models'  { @('list','install','verify','remove') }
        'providers' { @('list','test') }
        'pipeline-graph' { $commands }
        $null { $commands + $globalOpts }
        default { $globalOpts }
    }
    $candidates | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
}
