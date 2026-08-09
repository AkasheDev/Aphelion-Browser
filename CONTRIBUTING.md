# Contributing to Aphelion Browser

Thanks for your interest. This document explains how the repository is organized
and what a contribution has to satisfy to be merged.

Please read it before opening a pull request. Aphelion enforces architectural
boundaries deliberately, and a pull request that crosses them will be declined no
matter how good the code is.

## Project status

Aphelion is in early development. The desktop client is being built first; mobile
product features follow once desktop behavior is settled and documented. Large
feature contributions are premature at this stage — open an issue to discuss
direction before writing code.

## Ground rules

### The two clients stay separate

`desktop/` and `mobile/` are independent applications. They share no source code.
A single pull request must not modify both. If a change genuinely requires both
clients to move, split it into two pull requests and explain the link between them.

What the clients share lives in `shared/`: contracts, design tokens and product
specifications. Code is never shared; behavior is.

### Shared areas need agreement first

Changes to `shared/`, `backend/` or `docs/decisions/` affect both clients at once.
Open an issue and reach agreement before submitting such a change. A pull request
that unilaterally alters a contract will be asked to go back to discussion.

### Product direction is set by the project owner

Goals, roadmap and scope are decided by the project owner and are not settled in
pull requests. If you want to change direction rather than implement it, open an
issue first.

## Architecture

Both clients apply Clean Architecture. Dependencies point inward and never outward:

```text
Presentation ─┐
Platform     ─┼──▶ Application ──▶ Domain
Infrastructure┘
```

- **Domain** depends on nothing. Pure business model: entities, value objects, rules.
- **Application** depends only on Domain. Use cases, ports, DTOs.
- **Infrastructure** and **Platform** implement ports declared by the inner layers.
- **Presentation** contains no business rules.

A change that makes Domain reference a database, a UI framework or an HTTP client
is architecturally wrong even if it compiles and passes tests.

The desktop dependency rule is spelled out in [desktop/README.md](desktop/README.md);
the mobile one in [mobile/README.md](mobile/README.md).

## Dependencies

New third-party packages require discussion in an issue before they appear in a
pull request. Aphelion is a browser — every dependency is part of the attack
surface and every dependency is a long-term maintenance commitment.

When proposing one, say what it does, why the standard library or existing
dependencies cannot cover it, and what its license is. Dependencies incompatible
with AGPLv3 cannot be accepted.

Desktop package versions are managed centrally in `desktop/Directory.Packages.props`.

## Quality gates

A change is not finished until all of these hold:

- The build succeeds with **zero warnings**. Desktop builds with
  `TreatWarningsAsErrors`, so a warning is a failure.
- Static analysis passes (`dotnet build` for desktop, `flutter analyze` for mobile).
- Automated tests pass, and new behavior comes with tests covering it.
- No secrets, credentials or API keys appear in source, configuration or contracts.
- No personal or sensitive browsing data is written to logs.

Verify locally before pushing:

```shell
# Desktop
dotnet build desktop/Aphelion.Desktop.slnx -c Release

# Mobile
cd mobile && flutter analyze && flutter test
```

## Code style

Follow the conventions already present in the area you are editing rather than
importing your own. Keep comments and documentation in English throughout the
repository.

Prefer solutions built to last. Temporary workarounds and "we will fix it later"
patches are not accepted; if the correct fix needs a design decision, raise it in
an issue instead of shipping a shortcut.

## Architecture decision records

Decisions that shape the system — choosing a browser engine, a persistence layer,
a test framework — are recorded in [`docs/decisions/`](docs/decisions/) using
[the template](docs/decisions/0000-template.md). Records are numbered and immutable:
a decision that no longer holds is superseded by a new record, never edited.

If your pull request implements such a decision, the record should accompany it.

## Pull requests

- One focused change per pull request. Unrelated cleanups belong in their own.
- Describe what changed and why. Link the issue it addresses.
- State how you verified it, including which platforms you actually built and ran on.
- Be honest about what is incomplete. Known gaps stated up front are fine; gaps
  discovered during review are not.

## Reporting bugs

Open an issue including the client and platform, the steps to reproduce, what you
expected, what happened, and the version or commit you were on.

## Security issues

Do not open a public issue for a security vulnerability. Report it privately through
[GitHub's security advisory form](https://github.com/AkasheDev/Aphelion-Browser/security/advisories/new)
so a fix can be prepared before the problem is public.

## Licensing your contribution

Aphelion is licensed under the [GNU AGPLv3](LICENSE). By submitting a pull request
you agree that your contribution is licensed under the same terms.

Only submit code you have the right to license this way. Do not paste code from
projects under incompatible licenses, and do not contribute work your employer owns
without their permission.
