# Frends.Smb.DeleteDirectory

Task for deleting SMB directories.

[![DeleteDirectory_build](https://github.com/FrendsPlatform/Frends.Smb/actions/workflows/DeleteDirectory_build_and_test_on_main.yml/badge.svg)](https://github.com/FrendsPlatform/Frends.Smb/actions/workflows/DeleteDirectory_build_and_test_on_main.yml)
![Coverage](https://app-github-custom-badges.azurewebsites.net/Badge?key=FrendsPlatform/Frends.Smb/Frends.Smb.DeleteDirectory|main)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)

## Installing

You can install the Task via Frends UI Task View.

## Building

### Clone a copy of the repository

`git clone https://github.com/FrendsPlatform/Frends.Smb.git`

### Build the project

`dotnet build`

### Run tests

These SMB integration tests require Docker and a Linux-compatible environment (e.g. WSL2).
They will not run on Windows natively because SMB port 445 is reserved by the OS.
To execute the tests, run them inside WSL with Docker running:
`dotnet test`
The tests will automatically start a temporary Samba container and mount test files for reading.

### Create a NuGet package

`dotnet pack --configuration Release`

### Third-party licenses

StyleCop.Analyzer version (unmodified version 1.1.118) used to analyze code uses Apache-2.0 license, full text and
source code can be found at https://github.com/DotNetAnalyzers/StyleCopAnalyzers
