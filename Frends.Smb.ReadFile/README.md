# Frends.Smb.ReadFile

Reads a file from directory.

[![ReadFile_build](https://github.com/FrendsPlatform/Frends.Smb/actions/workflows/ReadFile_build_and_test_on_main.yml/badge.svg)](https://github.com/FrendsPlatform/Frends.Smb/actions/workflows/ReadFile_build_and_test_on_main.yml)
![Coverage](https://app-github-custom-badges.azurewebsites.net/Badge?key=FrendsPlatform/Frends.Smb/Frends.Smb.ReadFile|main)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)

## Installing

You can install the Task via Frends UI Task View.

## Building

### Clone a copy of the repository

`git clone https://github.com/FrendsPlatform/Frends.Smb.git`

### Build the project

`dotnet build`

### Run tests

The SMB tests require Linux or WSL with Docker installed.
These tests will not work on Windows natively because SMB uses port 445, which is reserved by the OS.

`cd Frends.Smb.ReadFile.Tests`
`docker-compose up -d`
`dotnet test`

### Create a NuGet package

`dotnet pack --configuration Release`

### Third-party licenses

StyleCop.Analyzer version (unmodified version 1.1.118) used to analyze code uses Apache-2.0 license, full text and
source code can be found at https://github.com/DotNetAnalyzers/StyleCopAnalyzers
