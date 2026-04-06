# Contributing to gc (Git Copy)

Thanks for your interest in contributing! This document covers the basics for getting started.

## Reporting Bugs

Open a [GitHub Issue](https://github.com/nickarora/gc/issues) with:

- A clear description of the problem
- Steps to reproduce
- Expected vs. actual behavior
- Your OS and .NET version

## Submitting Changes

1. Fork the repository
2. Create a feature branch (`git checkout -b feat/my-feature`)
3. Make your changes
4. Ensure the build and tests pass (see below)
5. Open a Pull Request against `main`

## Development Setup

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [git](https://git-scm.com/)

## Build & Test

```sh
dotnet build gc.sln
dotnet test gc.sln
dotnet test gc.sln --filter 'FullyQualifiedName~Cluster'   # cluster tests only
```

## Code Style

- Follow standard [C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/)
- Use **file-scoped namespaces**
- Prefer **pattern matching** where applicable
- Add **XML documentation** on all public APIs

## Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add support for shallow clones
fix: resolve path expansion on Windows
test: add cluster integration tests
docs: update README with new flags
chore: bump .NET SDK version
```

## PR Checklist

- [ ] `dotnet build gc.sln` passes with no warnings
- [ ] `dotnet test gc.sln` passes
- [ ] New public APIs have XML documentation
- [ ] Commit messages follow conventional commits

## License

By contributing, you agree that your changes will be licensed under the [MIT License](LICENSE).
