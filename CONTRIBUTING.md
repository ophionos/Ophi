# Contributing

Bug reports and feature ideas are welcome as [issues](https://github.com/ophionos/Ophi/issues).

Pull requests are accepted for an agreed issue. Open or comment on the issue first and wait for the
maintainer to agree on the change before you write code; an unannounced pull request may be closed.
Report security problems privately as described in [SECURITY.md](SECURITY.md), not in an issue.

## How to work

- **TDD.** Write a failing test, then the minimum code to pass, then refactor. Backend tests are
  named `[Method]_[Scenario]_[ExpectedResult]`; frontend tests `should [behavior] when [condition]`.
- **Follow the existing patterns.** New backend features are vertical slices; copy
  `src/Ophi.Api/Features/Tags/CreateTag.cs`. The design is in
  [docs/architecture.md](docs/architecture.md) and the invariants in
  [docs/agent-notes.md](docs/agent-notes.md).
- **Update the docs in `docs/`** in the same pull request when behavior changes.
- **Conventional Commits:** `<type>(<scope>): <description>`, with type `feat`, `fix`, `docs`,
  `style`, `refactor`, `test`, or `chore`.

## Running the tests

Set up the development environment as in the [README](README.md#development), then:

```bash
dotnet test --filter-not-trait "Category=Integration"   # backend, as CI runs it

cd src/Ophi.Web
bun run test:run   # frontend unit and component tests
bun run check      # type-check; `bun run build` does not type-check
```

The Postgres test tier (`tests/Ophi.Postgres.Tests`) needs `POSTGRES_TEST_CONNECTION` or Docker.
Which of these a change must pass before commit (the green-before-commit gate) is defined in the
Testing Conventions section of [CLAUDE.md](CLAUDE.md); CI runs the same checks on every pull request.

## License

Ophi is licensed under [AGPL-3.0-only](LICENSE). By submitting a contribution, you license it under
the same terms.
