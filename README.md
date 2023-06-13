# PSDetour

[![Test workflow](https://github.com/jborean93/PSDetour/workflows/Test%20PSDetour/badge.svg)](https://github.com/jborean93/PSDetour/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/jborean93/PSDetour/branch/main/graph/badge.svg?token=b51IOhpLfQ)](https://codecov.io/gh/jborean93/PSDetour)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PSDetour.svg)](https://www.powershellgallery.com/packages/PSDetour)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/jborean93/PSDetour/blob/main/LICENSE)

Hook C APIs in PowerShell as well as run PowerShell in other local processes.
See [about_PSDetour](docs/en-US/about_PSDetour.md) for more details.
See [PSDetour-Hooks](https://github.com/jborean93/PSDetour-Hooks) for some hooks that can be used with [Trace-PSDetourProcess](docs/en-US/Trace-PSDetourProcess.md).

## Documentation

Documentation for this module and details on the cmdlets included can be found [here](docs/en-US/PSDetour.md).

This module is highly experimental and misuse can crash the process you are hooking.
Currently it can only target x64 based processes on Windows.

## Requirements

These cmdlets have the following requirements

* PowerShell v7.2 or newer
* Windows Server 2008 R2/Windows 7 or newer

## Installing

The easiest way to install this module is through
[PowerShellGet](https://docs.microsoft.com/en-us/powershell/gallery/overview).

You can install this module by running;

```powershell
# Install for only the current user
Install-Module -Name PSDetour -Scope CurrentUser

# Install for all users
Install-Module -Name PSDetour -Scope AllUsers
```

## Contributing

Contributing is quite easy, fork this repo and submit a pull request with the changes.
To build this module run `.\build.ps1 -Task Build` in PowerShell.
To test a build run `.\build.ps1 -Task Test` in PowerShell.
This script will ensure all dependencies are installed before running the test suite.
