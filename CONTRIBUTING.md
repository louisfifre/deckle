# Contributing

Deckle is a **personal project**, developed as a learning exercise and
for the maintainer's daily use. The codebase is public so others can read,
fork, or borrow ideas — not because external contributions are actively
solicited. There is no roadmap commitment and no review SLA.

## Pull requests

Open an **issue first** to describe what you want to change and why.
Drive-by PRs without a prior issue may be closed unreviewed. PRs that
align with the project direction are evaluated case-by-case.

## Bug reports

Bug reports are welcome. Please include:

- what you did, what you expected, what happened;
- relevant logs from `%LOCALAPPDATA%\Deckle\logs\` (typically `app.jsonl`,
  `latency.jsonl`) — redact anything you consider private, these logs
  are local-only by design;
- Windows build, GPU vendor (for the Vulkan backend), and the Whisper
  model in use.

## Security issues

Anything security-sensitive goes through [SECURITY.md](SECURITY.md) — do
**not** open a public issue.

## License

By contributing, you agree your contribution ships under the same
[MIT license](LICENSE) as the rest of the project.
