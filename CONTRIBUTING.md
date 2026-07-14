# Contributing

Deckle is a personal project, built for the maintainer's daily use and as a way to learn software development in depth. The repository is public so others can inspect it, reuse ideas, report problems, and help improve it. There is no roadmap commitment or review SLA.

## Ideas for language support

Ideas and design proposals for supporting more languages are especially welcome.

Transcription is already multilingual through Whisper. The open architectural problem is the system-wide corrector: it is French-first today, and parts of its pipeline depend deliberately on French lexical data, morphology, evaluation corpora, and small local models. Adding a language should preserve the qualities that make correction trustworthy — conservative decisions, measurable false-correction rates, reversibility, local inference, and modest hardware requirements — rather than amount to swapping one dictionary for another.

An implementation is not required. A useful proposal can instead clarify:

- which language and typing errors it targets, with representative examples;
- which lexical data, corpora, or compact models could support it, including their licenses;
- what belongs in a language-independent correction pipeline and what belongs in a language-specific component;
- how the result could be evaluated, especially false corrections of valid text;
- the expected storage, memory, latency, and hardware costs.

Open an issue to discuss the approach before investing in a pull request. Proposals that challenge the current architecture constructively are welcome too.

## Bug reports and pull requests

Bug reports are welcome. Include what you did, what you expected, what happened, and the Deckle version or commit where you observed it. Logs can help, but review them and redact anything you consider private before attaching them.

Open an issue before a pull request to describe what you want to change and why. Contributions that align with the project's local-first direction are evaluated case by case.

By contributing, you agree that your contribution is distributed under Deckle's [MIT license](LICENSE).
