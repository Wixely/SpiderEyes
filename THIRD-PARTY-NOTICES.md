# Third-Party Notices

PlaywrightMCPSharp is licensed under the MIT License (see `LICENSE`). It depends
on the third-party components listed below. Each remains under its own license;
this file is provided for attribution.

## NuGet packages

| Package | License |
| --- | --- |
| Microsoft.Playwright | MIT |
| Microsoft.CodeAnalysis.CSharp.Scripting | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | MIT |
| Microsoft.AspNetCore.Mvc.Testing | MIT |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |
| ModelContextProtocol | Apache-2.0 |
| ModelContextProtocol.AspNetCore | Apache-2.0 |
| Serilog.AspNetCore | Apache-2.0 |
| Serilog | Apache-2.0 |
| Serilog.Enrichers.Environment | Apache-2.0 |
| Serilog.Enrichers.Process | Apache-2.0 |
| Serilog.Enrichers.Thread | Apache-2.0 |
| Serilog.Settings.Configuration | Apache-2.0 |
| Serilog.Sinks.Console | Apache-2.0 |
| Serilog.Sinks.File | Apache-2.0 |
| xunit | Apache-2.0 |
| xunit.runner.visualstudio | Apache-2.0 |

The full text of the MIT and Apache-2.0 licenses is available at
<https://opensource.org/license/mit> and
<https://www.apache.org/licenses/LICENSE-2.0> respectively.

## Browser runtimes

PlaywrightMCPSharp drives browser engines via Playwright. By default these
binaries are **not** redistributed with this project — they are downloaded
separately onto the host at runtime (for example via the `browser_install_runtime`
tool or `playwright install`). When obtained this way, each engine remains under
its own license and is not distributed as part of this software:

- Chromium — BSD-3-Clause (and the licenses of its bundled components)
- Mozilla Firefox — Mozilla Public License 2.0
- WebKit — BSD-2-Clause / LGPL

If you choose to bundle these browser binaries into a distributable artifact
(for example a Docker image or a self-contained package), you are responsible for
complying with their respective licenses, including any required attribution.

## Trademarks

"Playwright" is a trademark of Microsoft Corporation. Use of the name in this
project does not imply endorsement by, or affiliation with, Microsoft.
