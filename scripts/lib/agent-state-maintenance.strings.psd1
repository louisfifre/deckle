@{
    Workflow = 'Reset local AI sessions'
    AuditSentence = 'The selected local AI session state was inspected without changing it.'
    ResetSentence = 'The selected local AI session state was reset.'
    CloseApps = 'Close Codex, Claude, and their command-line processes, then run the reset again.'
    CloudWarning = 'Online account conversations are outside this local reset.'
    LevelDbWarning = 'Claude Desktop LevelDB may still reference folders; it is mixed storage and was intentionally left unchanged.'
    ConfirmationPhrase = 'RESET LOCAL AI SESSIONS'
    MenuInspect = 'Inspect AI session state'
    MenuReset = 'Reset AI session state'
    MenuInspectHeader = 'Deckle > Maintenance > AI sessions > Inspect'
    MenuResetHeader = 'Deckle > Maintenance > AI sessions > Reset'
    MenuQuestion = 'Delete local AI session data and remembered project state?'
    MenuConfirm = 'Reset sessions'
    MenuCancel = 'Keep sessions'
    MenuContext = @(
        'Removes transcripts, automatic memory, session attachments, and local project history.'
        'Keeps settings, account sign-in, authored instructions, repositories, and worktrees.'
        'Claude Desktop may still remember folders in mixed storage that this reset cannot safely change.'
        'Codex and Claude must be completely closed.'
    )
    MaintenanceGuidance = 'Choose a read-only scan, cleanup, or current-account AI session action.'
    FailureSentence = 'Local AI session reset stopped before it could complete safely.'
}
