# Third-party notices

EnrolmentRules is licensed under the GNU Affero General Public License v3.0
(`LICENSE`). It incorporates or depends on the third-party components below,
each under its own license. Nothing in `LICENSE` alters those terms.

**Every license listed here is AGPLv3-compatible.** No component prevents
EnrolmentRules from being distributed under the AGPL, and none imposes a
conflicting obligation.

## Redistributed with the application

These ship inside the deployed application, and so form part of the
"Corresponding Source" obligation under AGPLv3 §1 and §13.

### Vendored web assets (`src/EnrolmentRules.Web/wwwroot/lib/`)

| Component | Version | License | AGPLv3 compatibility |
| --- | --- | --- | --- |
| Bootstrap | 5.3.3 | MIT | Compatible |

Bootstrap: Copyright (c) 2011-2025 The Bootstrap Authors. Licensed under MIT
(<https://github.com/twbs/bootstrap/blob/main/LICENSE>).

### NuGet packages

| Package | Version | License |
| --- | --- | --- |
| JsonSchema.Net | 9.4.0 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.10 | MIT |
| RulesEngine | 6.0.1 | MIT |
| Spectre.Console | 0.57.2 | MIT |
| YamlDotNet | 18.1.0 | MIT |

MIT and BSD-3-Clause are permissive and GPL-compatible. Apache-2.0 is
compatible with GPLv3 and AGPLv3 specifically — the compatibility is one-way,
so Apache-2.0 code may be incorporated into an AGPLv3 work but not the reverse.
Apache-2.0 also requires that this notice file accompany redistribution and
that modified files be marked as changed.

`JsonSchema.Net` ships an additional Open Source Maintenance Fee agreement
(`OSMFEULA.txt`) covering the maintainer's *binary* release: users with annual
gross revenue of US$10,000 or more from revenue-generating use owe a monthly
fee for those prebuilt binaries. It is not a license fee — the code stays MIT,
§4 makes the MIT grant govern any conflict, and it imposes no additional
restriction on redistribution or on self-compiled binaries. So it does not
affect AGPLv3 compatibility, but downstream commercial users should read it.

### Bundled npm packages (`src/EnrolmentRules.Web/ClientApp/`)

Compiled by Vite into `wwwroot/app/` and served to the browser.

| Package | Version | License |
| --- | --- | --- |
| vue | 3.5.41 | MIT |

## Build-time only

Analyzers, the LibraryManager restore task, and the frontend toolchain. Not
redistributed, and not part of Corresponding Source.

| Package | Version | License |
| --- | --- | --- |
| BenchmarkDotNet | 0.15.8 | MIT |
| lookbusy1344.RecordValueAnalyser | 1.3.1 | MIT |
| Microsoft.VisualStudio.Threading.Analyzers | 18.7.23 | MIT |
| Microsoft.Web.LibraryManager.Build | 3.0.114 | MIT |
| Roslynator.Analyzers | 4.15.0 | Apache-2.0 |

Frontend toolchain (`ClientApp/` devDependencies):

| Package | Version | License |
| --- | --- | --- |
| @eslint/js | 10.0.1 | MIT |
| @types/node | 26.2.0 | MIT |
| @vitejs/plugin-vue | 6.0.8 | MIT |
| @vue/tsconfig | 0.9.1 | MIT |
| eslint | 10.8.1 | MIT |
| eslint-plugin-vue | 10.10.0 | MIT |
| globals | 17.9.0 | MIT |
| jiti | 2.7.0 | MIT |
| prettier | 3.9.6 | MIT |
| typescript | 6.0.3 | Apache-2.0 |
| typescript-eslint | 8.66.0 | MIT |
| vite | 8.2.1 | MIT |
| vue-tsc | 3.3.9 | MIT |

## Test-time only

Not redistributed.

| Package | Version | License |
| --- | --- | --- |
| AwesomeAssertions | 9.5.0 | Apache-2.0 |
| coverlet.collector | 10.0.1 | MIT |
| FsCheck.Xunit | 3.3.4 | BSD-3-Clause |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.10 | MIT |
| Microsoft.CodeAnalysis.CSharp | 5.6.0 | MIT |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | MIT |
| Microsoft.NET.Test.Sdk | 18.8.1 | MIT |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 |

| Package (`ClientApp/`) | Version | License |
| --- | --- | --- |
| @playwright/test | 1.62.1 | Apache-2.0 |
| @vue/test-utils | 2.4.11 | MIT |
| jsdom | 30.0.1 | MIT |
| vitest | 4.1.10 | MIT |

## The `.NET` runtime and ASP.NET Core shared framework

Not vendored here. Microsoft distributes them under MIT, and they are consumed
as a platform rather than incorporated, so they fall under AGPLv3 §1's "System
Libraries" exception and need not be included in Corresponding Source.

## Regenerating

Package versions come from the individual `.csproj` files and
`Directory.Build.props`; vendored web assets from
`src/EnrolmentRules.Web/libman.json`; frontend packages from
`src/EnrolmentRules.Web/ClientApp/package.json`. Update this file when any of
them changes.
