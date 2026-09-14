# RGBOff

A minimal, dependency-free Windows console tool that turns off NVIDIA GPU illumination (RGB) zones — no vendor software, no background service, just one small executable.

It talks to `nvapi64.dll` directly via P/Invoke, resolving each NVAPI function at runtime by ID through `NvAPI_QueryInterface` (the standard way to call NVAPI, since NVIDIA doesn't export individual function names). There are no external dependencies — not even NvAPIWrapper.dll — and no NuGet packages.

## Usage

Run `RGBOff.exe`. It initializes NVAPI, enumerates GPU 0, reads its illumination zones, and sets every zone to off (R=0, G=0, B=0, brightness=0) using manual RGB control mode.

If your GPU has no illumination zones, or the NVIDIA driver isn't present, the tool prints an informational message and exits cleanly — it's not an error condition.

## How it works

NVAPI function names aren't exported from `nvapi64.dll`; every function (other than `nvapi_QueryInterface` itself) is resolved at runtime by a numeric function ID. RGBOff verifies those IDs and the layout of the structs it marshals against two independent sources:

1. **[falahati/NvAPIWrapper](https://github.com/falahati/NvAPIWrapper)** — an open-source C# NVAPI wrapper by **Soroush Falahati**, licensed under **LGPL-3.0**. RGBOff does not link against or redistribute `NvAPIWrapper.dll` — it's used purely as a reference for function IDs and struct field layouts (`FunctionId.cs`, `IlluminationZone*.cs`).
2. **[NVIDIA/nvapi](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h)** — NVIDIA's own official open-source NVAPI header repo, used to cross-check the function IDs byte-for-byte.

Struct field order was additionally verified against NVIDIA's public [NVAPI reference docs](https://docs.nvidia.com/nvapi/struct__NV__GPU__CLIENT__ILLUM__ZONE__CONTROL__PARAMS__V1.html).

Credit and thanks to Soroush Falahati and the NvAPIWrapper project for documenting the NVAPI illumination surface in a usable, readable form.

## Building

Single-file C# console app targeting .NET Framework / .NET with P/Invoke — compile `RGBOff.cs` with `csc` or any standard C# toolchain. No project file or package references are required.

## License

RGBOff is released under the [MIT License](LICENSE).

NvAPIWrapper (referenced, not redistributed) is separately licensed under LGPL-3.0 by its author, Soroush Falahati — see [its repository](https://github.com/falahati/NvAPIWrapper) for terms.
