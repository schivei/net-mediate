# net-mediate

Mediator pattern and facilities

[![CI/CD Pipeline](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml)
[![NuGet Version](https://img.shields.io/nuget/v/NetMediate)](https://www.nuget.org/packages/NetMediate/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetMediate)](https://www.nuget.org/packages/NetMediate/)

## Features

- Mediator pattern implementation
- Support for multiple .NET versions (.NET 8.0, .NET 9.0, .NET Standard 2.1)
- Comprehensive testing with code coverage reporting
- Automated CI/CD pipeline with GitHub Actions

## CI/CD Pipeline

This repository includes a comprehensive CI/CD pipeline that automatically:

### On Pull Requests and Pushes
- **Build**: Compiles the project for all target frameworks
- **Test**: Runs all automated tests with detailed reporting
- **Coverage**: Generates code coverage reports and comments on pull requests
- **Security**: Performs CodeQL security analysis

### On Main/Master Branch Pushes
- **Package**: Creates NuGet packages with symbols and source
- **Publish**: Publishes packages to NuGet.org (requires `NUGET_API_KEY` secret)
- **Release**: Creates GitHub releases with package artifacts

### Setup Requirements

To enable NuGet publishing, configure the following repository secret:
- `NUGET_API_KEY`: Your NuGet.org API key for package publishing

### Workflow Features
- Multi-framework support (net8.0, net9.0, netstandard2.1)
- Test results and coverage artifacts
- Automated version management based on build timestamp
- Security analysis with CodeQL
- Pull request coverage reporting
