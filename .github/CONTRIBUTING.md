# Contributing to NetMediate

Thank you for your interest in contributing to NetMediate! We welcome contributions from the community and are pleased to have you join us.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Making Changes](#making-changes)
- [Code Style](#code-style)
- [Testing](#testing)
- [Submitting Changes](#submitting-changes)
- [Release Process](#release-process)

## Code of Conduct

This project and everyone participating in it is governed by our [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

## Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- Git
- A GitHub account

### Development Setup

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/yourusername/net-mediate.git
   cd net-mediate
   ```

3. Add the original repository as upstream:
   ```bash
   git remote add upstream https://github.com/schivei/net-mediate.git
   ```

4. Restore dependencies:
   ```bash
   dotnet restore
   ```

5. Build the project:
   ```bash
   dotnet build
   ```

6. Run tests to ensure everything works:
   ```bash
   dotnet test
   ```

## Making Changes

### Branch Naming

Use descriptive branch names with the following prefixes:
- `feature/` - New features
- `bugfix/` - Bug fixes
- `hotfix/` - Critical fixes
- `docs/` - Documentation updates
- `refactor/` - Code refactoring

Example: `feature/add-new-mediator-pattern`

### Commit Messages

Follow conventional commit format:
- `feat:` - New features
- `fix:` - Bug fixes
- `docs:` - Documentation changes
- `style:` - Code style changes (formatting, etc.)
- `refactor:` - Code refactoring
- `test:` - Adding or updating tests
- `chore:` - Maintenance tasks

Example: `feat: add support for async request handlers`

## Code Style

This project uses CSharpier for code formatting. All code must be properly formatted before submission.

### Formatting Your Code

Run the formatter before committing:
```bash
dotnet csharpier format .
```

### Check Formatting

Verify your code meets formatting standards:
```bash
dotnet csharpier check .
```

**Note:** The CI pipeline will automatically check code formatting and fail if standards are not met.

## Testing

### Writing Tests

- Write unit tests for all new functionality
- Aim for high test coverage (minimum 95% required)
- Use descriptive test names that explain what is being tested
- Follow the Arrange-Act-Assert pattern

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Test Coverage Requirements

- All pull requests must maintain at least 95% code coverage
- The CI pipeline will enforce this requirement
- Coverage reports are automatically generated and uploaded to Codecov

## Submitting Changes

### Pull Request Process

1. Ensure your branch is up to date with the main branch:
   ```bash
   git fetch upstream
   git checkout main
   git merge upstream/main
   git checkout your-feature-branch
   git rebase main
   ```

2. Run all tests and ensure they pass:
   ```bash
   dotnet test
   ```

3. Format your code:
   ```bash
   dotnet csharpier format .
   ```

4. Push your changes to your fork:
   ```bash
   git push origin your-feature-branch
   ```

5. Create a pull request through GitHub's web interface

### Pull Request Guidelines

- Fill out the pull request template completely
- Provide a clear description of the changes
- Reference any related issues
- Ensure all CI checks pass
- Be responsive to code review feedback

### What to Expect

- All pull requests require review before merging
- CI checks must pass (build, tests, formatting, coverage)
- Code coverage must be at least 95%
- Changes may require documentation updates

## Release Process

Releases are handled by maintainers:

1. Version tags follow semantic versioning (e.g., `v1.2.3`)
2. Releases are automatically built and published to NuGet
3. Release notes are generated automatically from commit messages

## Getting Help

- Create an issue for bugs or feature requests
- Use discussions for questions about usage
- Check existing issues and documentation first

## Recognition

Contributors are recognized in our release notes and README. Thank you for making NetMediate better!