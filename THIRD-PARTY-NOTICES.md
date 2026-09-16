# Third-party notices

The root MIT license applies to project-owned code and documentation.
Third-party material retains its own terms. In particular, the SVG and PNG
battery assets described below are licensed under Apache-2.0, not the root MIT
license. No affiliation or endorsement by the upstream owners is implied.

## IconPark battery assets

Copyright 2019-present Bytedance Inc.

- Website: <https://iconpark.oceanengine.com/official>
- Upstream: <https://github.com/bytedance/IconPark>
- Verified revision: `8dc132da4c85671ba6a5962c87aa2bdafbf158e9`
- License: Apache License, Version 2.0
- License copy: [licenses/IconPark-LICENSE.txt](licenses/IconPark-LICENSE.txt)

| Local source filename suffix | Pinned upstream source |
| --- | --- |
| `_battery-empty.svg` | [source/Energy/battery-empty.svg](https://github.com/bytedance/IconPark/blob/8dc132da4c85671ba6a5962c87aa2bdafbf158e9/source/Energy/battery-empty.svg) |
| `_battery-full.svg` | [source/Energy/battery-full.svg](https://github.com/bytedance/IconPark/blob/8dc132da4c85671ba6a5962c87aa2bdafbf158e9/source/Energy/battery-full.svg) |
| `_battery-working-one.svg` | [source/Hardware/battery-working-one.svg](https://github.com/bytedance/IconPark/blob/8dc132da4c85671ba6a5962c87aa2bdafbf158e9/source/Hardware/battery-working-one.svg) |

The local SVGs use IconPark's outline customization: 24-by-24 intrinsic size,
the original 48-by-48 view box and geometry, no battery-body fill, and `#333`
foreground strokes/fill instead of the upstream multi-color palette.
Source-file comments and [icons/NOTICE.txt](icons/NOTICE.txt) identify these
customizations and the license.

The project generator adapts the full/empty sources by retaining zero to four
charge bars, changing the foreground to white for runtime tinting, and
rasterizing at 16, 20, 24, 32, 40, 48, and 64 pixels. These adaptations are by
Abo-Fat. The resulting `battery-*.png` files in
`src\CopilotUsage.Tray\Assets` retain Apache-2.0. They carry embedded attribution
and modification metadata, with a companion
[NOTICE.txt](src/CopilotUsage.Tray/Assets/NOTICE.txt).
The working-battery SVG is retained as source but is not used by the generator.

The inspected upstream revision has no root NOTICE file. These project notice
files describe the selected assets and adaptations; they are not an upstream
NOTICE or a change to the Apache license. When redistributing the assets,
retain the copyright, modification notices, and Apache-2.0 license copy.

## Referenced development and runtime dependencies

| Component | Version | Verified license information | Use in this source distribution |
| --- | --- | --- | --- |
| `@resvg/resvg-js` | 2.6.2 | MPL-2.0, declared in the installed package manifest and committed npm lock file; [upstream](https://github.com/yisibl/resvg-js) | Optional development tool for generating PNGs. The renderer and its native dependencies are not vendored or embedded in the generated images. |
| `Microsoft.Web.WebView2` SDK | 1.0.3650.58 | The exact NuGet package's `LICENSE.txt` contains Microsoft's BSD-3-Clause terms; [package license](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3650.58/License) | Referenced by the tray project and restored by NuGet, not vendored in this repository. |
| .NET SDK / runtime | .NET 10 | .NET components have their own licenses and third-party notices; [dotnet/runtime](https://github.com/dotnet/runtime) | Build prerequisite. Neither the local SDK nor a self-contained application/runtime bundle is committed. |
| Microsoft Edge WebView2 Runtime | Installed separately | Governed by the runtime's own Microsoft terms, not the WebView2 SDK package license | Required for the login window; not included in this source distribution. |

Generated images do not contain the renderer's code; using an MPL-2.0 renderer
does not replace the images' IconPark license. The npm manifest and lock file
describe how to obtain the build tool, not a bundled copy of that tool.

This repository currently distributes source and the required battery assets
only. Before distributing executable packages, audit the actual package
contents, copy the required .NET/WebView2 and other component licenses and
notices into the package, and retain the IconPark asset license and notices.
The local build script does not currently assemble a license-complete Release
archive; its output is for local use and verification.
