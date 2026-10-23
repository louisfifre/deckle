# User profile path bug notes

## Vault and credentials errors named their file by its full path, user name included

- Trigger: the secret vault, the Anytype credentials file or the space aliases file cannot be read, decrypted or parsed. The exception carries the file's path.
- Observed symptom: the exception message held `C:\Users\<name>\AppData\Local\Deckle\...`. The Anytype runtime start-up and the LLM provider key read log `ex.Message`, so the Windows user name reached the log (`HostProvisioningFailed`, the runtime end event, `ProviderKeyReadFailedDetail`).
- Cause: the messages interpolated the resolved path. The logging sites cannot tell a path from the rest of a message, so the fix belongs where the path is written.
- Violated invariant: a line carries paths relative to a known root, and nothing that names the person.
- Fix: `UserProfilePath.Mask` names the folder by its variable (`%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`); `SecretVault`, `AnytypeCredentials` and `AnytypeSpaceAliases` use it in every message that names a path. The screen shows the same text, and a Windows user can open the variable as is.
- Uncertain: other exceptions that carry paths (an IOException from the backend files, for example) are not covered by this change; they come from the framework and the logging site would have to mask them.
- Recurrence cue: a new exception message that interpolates a path from `AppPaths`, or a log line that contains the profile folder.
- Guarded by: `UserProfilePathTests`, the regression case on an unreadable vault.