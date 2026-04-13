# Frends.Smb.RenameFile

Frends Task for renaming files on remote SMB shares.

[![RenameFile_build](https://github.com/FrendsPlatform/Frends.Smb/actions/workflows/RenameFile_build_and_test_on_main.yml/badge.svg)](https://github.com/FrendsPlatform/Frends.Smb/actions/workflows/RenameFile_build_and_test_on_main.yml)
![Coverage](https://app-github-custom-badges.azurewebsites.net/Badge?key=FrendsPlatform/Frends.Smb/Frends.Smb.RenameFile|main)
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
They will not run on Windows natively because the OS reserves SMB port 445.
To execute the tests, you can either:
- Run them on a Linux machine 
- Disable in Windows Services service named Server (LanmanServer) 
  ```aiignore
  Stop-Service -Name LanmanServer -Force
  Set-Service -Name LanmanServer -StartupType Disabled
  ```
  and restart the machine (Remember to enable it again after tests) 

After that you can start up Docker run tests with: `dotnet test`.
The tests will automatically start a temporary Samba container and mount test files for reading.

### Create a NuGet package

`dotnet pack --configuration Release`

### Third-party licenses

StyleCop.Analyzer version (unmodified version 1.1.118) used to analyze code uses Apache-2.0 license, full text and
source code can be found at https://github.com/DotNetAnalyzers/StyleCopAnalyzers
