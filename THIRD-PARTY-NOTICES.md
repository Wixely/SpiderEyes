# Third-Party Notices

PlaywrightMCPSharp is licensed under the MIT License (see `LICENSE`). It depends
on the third-party components listed below. Each remains under its own license;
this file is provided for attribution.

## NuGet packages (server)

| Package | License |
| --- | --- |
| Microsoft.CodeAnalysis.CSharp.Scripting | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | MIT |
| Microsoft.Playwright | MIT |
| ModelContextProtocol.AspNetCore | Apache-2.0 |
| Serilog.AspNetCore | Apache-2.0 |
| Serilog.Enrichers.Environment | Apache-2.0 |
| Serilog.Enrichers.Process | Apache-2.0 |
| Serilog.Enrichers.Thread | Apache-2.0 |
| Serilog.Settings.Configuration | Apache-2.0 |
| Serilog.Sinks.Console | Apache-2.0 |
| Serilog.Sinks.File | Apache-2.0 |

The test project (`tests/PlaywrightMCPSharp.Server.Tests`) additionally uses
`Microsoft.AspNetCore.Mvc.Testing` (MIT), `Microsoft.NET.Test.Sdk` (MIT),
`coverlet.collector` (MIT), `xunit` (Apache-2.0), and `xunit.runner.visualstudio`
(Apache-2.0). These are not shipped in release artifacts.

The full text of the MIT and Apache-2.0 licenses is available at
<https://opensource.org/license/mit> and
<https://www.apache.org/licenses/LICENSE-2.0> respectively.

## Browser runtimes

PlaywrightMCPSharp does **not** ship browser binaries (Chromium, Firefox,
WebKit). They are downloaded onto the host at deployment time via the
`browser_install_runtime` tool or `playwright install`, or the server connects
to a browser you provide separately. When obtained this way, each engine
remains under its own licence (Chromium — BSD-3-Clause and its bundled
components; Firefox — Mozilla Public License 2.0; WebKit — BSD-2-Clause /
LGPL) and is not distributed by this project.

If you choose to bundle a browser binary into a distributable artifact of your
own (for example a Docker image or a self-contained package), you are
responsible for complying with its respective licence and any required
attribution.

## Trademarks

"Playwright" is a trademark of Microsoft Corporation. Use of the name in this
project does not imply endorsement by, or affiliation with, Microsoft.
