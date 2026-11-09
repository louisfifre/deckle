# Inspect context bug notes

## A loop variable took the array type of a parameter of the same name

- Trigger: running `scripts/inspect-context.ps1`, with or without `-Json`
  (menu action « Statistics / context »).
- Symptom: the summary printed, then the run stopped with « Cannot process
  argument transformation on parameter 'Title'. Cannot convert value to type
  System.String. », exit code 1, and no JSON file was written.
- Cause: the per-mode loop was written `foreach ($loadingMode in ...)`. PowerShell
  variable names ignore case, so this variable was the script parameter
  `[string[]]$LoadingMode`; it kept the parameter's array type, and
  `Write-Section $loadingMode` could not bind an array to its `[string]` Title.
- Violated invariant: a local variable never shares its name, in any case, with
  a typed script parameter. The trap is commented at the loop in
  `scripts/inspect-context.ps1`.
- Recurrence cue: a binding error naming a `[string]` parameter, raised on a
  value that looks like a single string, in a script whose `param()` block has
  a typed parameter spelled like the variable.
- Guarded by: `scripts/tests/commands/inspect-context.tests.ps1`, which runs the
  script on a two-document repository with `-Json` and checks the exit code and
  the file.
