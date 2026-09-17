// RGBOff.cs
//
// Dependency-free tool that polls the current NVIDIA GPU illumination (RGB)
// state, verifies it against what it's about to change, turns every zone
// off, and then re-polls to confirm the write actually took effect.
// Calls nvapi64.dll directly via P/Invoke + nvapi_QueryInterface
// function-pointer resolution. No NvAPIWrapper.dll, no NuGet packages.
//
// Function IDs and struct layouts verified against two independent sources:
//   1. falahati/NvAPIWrapper (open-source C# NVAPI wrapper)
//      https://github.com/falahati/NvAPIWrapper
//      - NvAPIWrapper/Native/Helpers/FunctionId.cs
//      - NvAPIWrapper/Native/GPU/Structures/IlluminationZone*.cs
//   2. NVIDIA's own official open-source header repo
//      https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h
//      (byte-identical hex IDs for every function used below, GetControl
//      included: { "NvAPI_GPU_ClientIllumZonesGetControl", 0x3dbf5764 })
//   Struct field order additionally cross-checked against NVIDIA's public
//   NVAPI reference docs (docs.nvidia.com/nvapi/struct__NV__GPU__CLIENT__ILLUM__ZONE__CONTROL__PARAMS__V1.html)
//   Status codes verified against nvapi_lite_common.h (_NvAPI_Status enum).
//
// SAFETY / FUTUREPROOFING NOTES
// ------------------------------
// NVAPI's own versioning convention is the primary safety net here: a V1
// struct's binary layout is a fixed contract NVIDIA promises never to
// change - a struct that grows gets a new V2 typedef and a new encoded
// version dword. If NVIDIA ever does break that promise (or if this code
// has a bug in the layout), the driver detects the mismatch from the
// version dword we send and returns NVAPI_INCOMPATIBLE_STRUCT_VERSION
// rather than reading/writing past what it expects. This tool treats that
// status as a hard stop with its own message, and never calls SetControl
// unless the immediately preceding GetControl call succeeded and the zone
// count/types it reported agree with GetInfo. It also only ever mutates
// the 4 bytes documented as color/brightness data within a zone control
// entry it just read back from the driver - never a struct built from
// scratch - so any reserved bytes NVIDIA repurposes in a future minor
// revision are round-tripped unchanged rather than zeroed blindly.
//
// COMPATIBILITY / GRACEFUL FAILURE
// ---------------------------------
// Runs safely on any machine, any GPU vendor, no driver at all, etc: every
// failure point (missing nvapi64.dll, no NVIDIA driver, no GPU, no
// illumination interface, 0 zones, struct-version mismatch) is caught and
// reported, exit code 0, no crash, no write attempted. But it only DOES
// something on cards whose driver exposes RGB through NVAPI's ClientIllumZones
// interface - reliably true for NVIDIA Founders Edition cards. Many
// third-party AIB cards (ASUS/MSI/Gigabyte/etc.) control RGB through their
// own vendor software instead and aren't reachable here; on those this is
// expected to hit NVAPI_NOT_SUPPORTED / zoneCount==0 and exit having changed
// nothing - not a bug, just outside what this API covers.
//
using System;
using System.Runtime.InteropServices;

internal static class RGBOff
{
    // ---- nvapi64.dll entry point --------------------------------------------------
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    private static extern IntPtr nvapi_QueryInterface(uint interfaceId);

    // ---- Verified function IDs (see header comment for sources) -------------------
    private const uint ID_Initialize                = 0x0150E828;
    private const uint ID_Unload                     = 0xD22BDD7E;
    private const uint ID_EnumPhysicalGPUs           = 0xE5AC921F;
    private const uint ID_ClientIllumZonesGetInfo    = 0x4B81241B;
    private const uint ID_ClientIllumZonesGetControl = 0x3DBF5764; // new: read current state
    private const uint ID_ClientIllumZonesSetControl = 0x197D065E;

    private const int NVAPI_MAX_PHYSICAL_GPUS = 32;
    private const int MAX_ILLUM_ZONES = 32;

    // First 4 bytes of a zone's Data payload, per NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_DATA_RGB:
    // Data[0]=R, Data[1]=G, Data[2]=B, Data[3]=Brightness%. Everything after that is
    // reserved/mode-specific space we deliberately never touch.
    private const int COLOR_BYTES = 4;

    // ---- Verified NVAPI_Status values (nvapi_lite_common.h) -----------------------
    private const int NVAPI_OK = 0;
    private const int NVAPI_INCOMPATIBLE_STRUCT_VERSION = -9;
    private const int NVAPI_NOT_SUPPORTED = -104;

    // ---- Delegate signatures --------------------------------------------------------
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_Initialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_Unload();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_EnumPhysicalGPUs(
        [Out] IntPtr[] gpuHandles, out uint gpuCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_ClientIllumZonesGetInfo(
        IntPtr gpuHandle, ref NV_GPU_CLIENT_ILLUM_ZONE_INFO_PARAMS_V1 info);

    // GetControl and SetControl share the same params struct shape.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_ClientIllumZonesGetControl(
        IntPtr gpuHandle, ref NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 control);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_ClientIllumZonesSetControl(
        IntPtr gpuHandle, ref NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 control);

    // ---- Zone type / control mode enums --------------------------------------------
    private const uint ZONE_TYPE_INVALID = 0;
    private const uint ZONE_TYPE_RGB = 1;
    private const uint ZONE_TYPE_FIXED_COLOR = 2;

    private const uint CONTROL_MODE_MANUAL_RGB = 0;

    // ---- Structures -----------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_INFO_V1
    {
        public uint ZoneType;
        public byte DeviceIndex;
        public byte ProviderIndex;
        public uint ZoneLocation;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] // Data(64)+Reserved(64), contiguous
        public byte[] Data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_INFO_PARAMS_V1
    {
        public uint Version;
        public uint NumberOfZones;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ILLUM_ZONES)]
        public NV_GPU_CLIENT_ILLUM_ZONE_INFO_V1[] Zones;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1
    {
        public uint ZoneType;
        public uint ControlMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 192)] // Data(128)+Reserved(64), contiguous
        public byte[] Data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1
    {
        public uint Version;
        public uint Flags;               // bit0 = bDefault (0 = CurrentlyActive, 1 = Default)
        public uint NumberOfZoneControls;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_ILLUM_ZONES)]
        public NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1[] ZoneControls;
    }

    private static uint MakeVersion(int structSize, int structVersion)
    {
        return (uint)(structSize | (structVersion << 16));
    }

    private static T GetDelegate<T>(uint functionId) where T : class
    {
        IntPtr ptr = nvapi_QueryInterface(functionId);
        if (ptr == IntPtr.Zero)
            throw new EntryPointNotFoundException(
                "nvapi_QueryInterface returned null for function ID 0x" + functionId.ToString("X8"));
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private static string StatusName(int status)
    {
        switch (status)
        {
            case NVAPI_OK: return "NVAPI_OK";
            case NVAPI_INCOMPATIBLE_STRUCT_VERSION: return "NVAPI_INCOMPATIBLE_STRUCT_VERSION";
            case NVAPI_NOT_SUPPORTED: return "NVAPI_NOT_SUPPORTED";
            default: return "status " + status;
        }
    }

    private static string ZoneTypeName(uint zoneType)
    {
        switch (zoneType)
        {
            case ZONE_TYPE_RGB: return "RGB";
            case ZONE_TYPE_FIXED_COLOR: return "FixedColor";
            case ZONE_TYPE_INVALID: return "Invalid";
            default: return "Unknown(" + zoneType + ")";
        }
    }

    private static NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 NewControlParams()
    {
        var p = new NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1
        {
            Reserved = new byte[64],
            ZoneControls = new NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1[MAX_ILLUM_ZONES]
        };
        for (int i = 0; i < MAX_ILLUM_ZONES; i++)
            p.ZoneControls[i].Data = new byte[192];
        p.Version = MakeVersion(Marshal.SizeOf(typeof(NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1)), 1);
        return p;
    }

    private static void PrintZoneState(string label, uint zoneCount, NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 p)
    {
        Console.WriteLine(label + " (" + zoneCount + " zone(s)):");
        for (int i = 0; i < zoneCount; i++)
        {
            var z = p.ZoneControls[i];
            Console.WriteLine(string.Format(
                "  zone {0}: type={1} mode={2} R={3} G={4} B={5} Brightness={6}%",
                i, ZoneTypeName(z.ZoneType), z.ControlMode, z.Data[0], z.Data[1], z.Data[2], z.Data[3]));
        }
    }

    private static bool AllZonesOff(uint zoneCount, NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 p)
    {
        for (int i = 0; i < zoneCount; i++)
        {
            var z = p.ZoneControls[i];
            if (z.ControlMode != CONTROL_MODE_MANUAL_RGB)
                return false;
            for (int b = 0; b < COLOR_BYTES; b++)
                if (z.Data[b] != 0)
                    return false;
        }
        return true;
    }

    // Runs the full poll -> verify -> write -> confirm sequence for one physical GPU.
    // Zone count/types are read fresh from this GPU, never assumed.
    private static void ProcessGpu(
        IntPtr gpu,
        Delegate_ClientIllumZonesGetInfo getInfo,
        Delegate_ClientIllumZonesGetControl getControl,
        Delegate_ClientIllumZonesSetControl setControl)
    {
        // ---- Step 1: static zone info (types/count) --------------------------
        var infoParams = new NV_GPU_CLIENT_ILLUM_ZONE_INFO_PARAMS_V1
        {
            Reserved = new byte[64],
            Zones = new NV_GPU_CLIENT_ILLUM_ZONE_INFO_V1[MAX_ILLUM_ZONES]
        };
        infoParams.Version = MakeVersion(Marshal.SizeOf(typeof(NV_GPU_CLIENT_ILLUM_ZONE_INFO_PARAMS_V1)), 1);

        int infoStatus = getInfo(gpu, ref infoParams);
        if (infoStatus == NVAPI_INCOMPATIBLE_STRUCT_VERSION)
        {
            Console.WriteLine("NvAPI_GPU_ClientIllumZonesGetInfo: " + StatusName(infoStatus) +
                " - the driver no longer recognizes this struct's V1 layout. Refusing to proceed rather than " +
                "risk touching memory this build no longer understands. This tool needs updating for the current NVAPI SDK.");
            return;
        }
        if (infoStatus != NVAPI_OK)
        {
            Console.WriteLine("NvAPI_GPU_ClientIllumZonesGetInfo returned " + StatusName(infoStatus) +
                " (informational, e.g. NVAPI_NOT_SUPPORTED if this GPU has no RGB zones).");
            return;
        }

        uint zoneCount = infoParams.NumberOfZones;
        if (zoneCount == 0)
        {
            Console.WriteLine("GPU reports 0 illumination zones. Nothing to turn off.");
            return;
        }

        // ---- Step 2: poll current live control state --------------------------
        var before = NewControlParams();
        // Flags left at 0 => request CurrentlyActive state, not the saved Default profile.

        int getStatus = getControl(gpu, ref before);
        if (getStatus == NVAPI_INCOMPATIBLE_STRUCT_VERSION)
        {
            Console.WriteLine("NvAPI_GPU_ClientIllumZonesGetControl: " + StatusName(getStatus) +
                " - struct layout mismatch. Refusing to proceed.");
            return;
        }
        if (getStatus != NVAPI_OK)
        {
            Console.WriteLine("NvAPI_GPU_ClientIllumZonesGetControl returned " + StatusName(getStatus) + ". Aborting.");
            return;
        }

        // ---- Step 3: cross-check GetInfo vs GetControl before trusting either --
        if (before.NumberOfZoneControls != zoneCount)
        {
            Console.WriteLine("Zone count mismatch: GetInfo reported " + zoneCount +
                " but GetControl reported " + before.NumberOfZoneControls +
                ". This disagreement suggests the API is behaving unexpectedly - aborting without writing anything.");
            return;
        }
        for (int i = 0; i < zoneCount; i++)
        {
            if (before.ZoneControls[i].ZoneType != infoParams.Zones[i].ZoneType)
            {
                Console.WriteLine("Zone " + i + " type mismatch: GetInfo=" + ZoneTypeName(infoParams.Zones[i].ZoneType) +
                    " GetControl=" + ZoneTypeName(before.ZoneControls[i].ZoneType) + ". Aborting without writing anything.");
                return;
            }
        }

        PrintZoneState("Current illumination state", zoneCount, before);

        if (AllZonesOff(zoneCount, before))
        {
            Console.WriteLine("All zones already off. Nothing to do.");
            return;
        }

        // ---- Step 4: read-modify-write - clone what we just read, touch only ---
        // ---- the 4 documented color bytes per zone, leave everything else as-is
        var after = before; // struct copy; ZoneControls/Reserved arrays are still shared references below
        after.ZoneControls = (NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1[])before.ZoneControls.Clone();
        after.Flags = 0; // explicit: write to CurrentlyActive, not Default
        for (int i = 0; i < zoneCount; i++)
        {
            var zone = after.ZoneControls[i];
            zone.Data = (byte[])before.ZoneControls[i].Data.Clone();
            zone.ControlMode = CONTROL_MODE_MANUAL_RGB;
            for (int b = 0; b < COLOR_BYTES; b++)
                zone.Data[b] = 0;
            after.ZoneControls[i] = zone;
        }

        int setStatus = setControl(gpu, ref after);
        if (setStatus != NVAPI_OK)
        {
            Console.WriteLine("NvAPI_GPU_ClientIllumZonesSetControl returned " + StatusName(setStatus) + ".");
            return;
        }
        Console.WriteLine("SetControl OK: requested " + zoneCount + " zone(s) off.");

        // ---- Step 5: re-poll and confirm the write actually took -------------
        var verify = NewControlParams();
        int verifyStatus = getControl(gpu, ref verify);
        if (verifyStatus != NVAPI_OK)
        {
            Console.WriteLine("Post-write verification GetControl returned " + StatusName(verifyStatus) +
                " - could not confirm the change took effect.");
            return;
        }

        PrintZoneState("Post-write illumination state", zoneCount, verify);
        Console.WriteLine(AllZonesOff(zoneCount, verify)
            ? "Verified: all " + zoneCount + " zone(s) confirmed off."
            : "Warning: SetControl reported success, but one or more zones did not read back as off.");
    }

    private static int Main()
    {
        bool initialized = false;
        try
        {
            var initialize = GetDelegate<Delegate_Initialize>(ID_Initialize);
            int initStatus = initialize();
            if (initStatus != NVAPI_OK)
            {
                Console.WriteLine("NvAPI_Initialize failed with " + StatusName(initStatus) + ". Aborting.");
                return 0; // no NVIDIA driver present, etc. - not a crash
            }
            initialized = true;

            var enumGpus = GetDelegate<Delegate_EnumPhysicalGPUs>(ID_EnumPhysicalGPUs);
            IntPtr[] gpuHandles = new IntPtr[NVAPI_MAX_PHYSICAL_GPUS];
            uint gpuCount;
            int enumStatus = enumGpus(gpuHandles, out gpuCount);
            if (enumStatus != NVAPI_OK || gpuCount == 0)
            {
                Console.WriteLine("NvAPI_EnumPhysicalGPUs failed with " + StatusName(enumStatus) + " (count=" + gpuCount + ").");
                return 0;
            }
            Console.WriteLine("Found " + gpuCount + " NVIDIA GPU(s).");

            var getInfo = GetDelegate<Delegate_ClientIllumZonesGetInfo>(ID_ClientIllumZonesGetInfo);
            var getControl = GetDelegate<Delegate_ClientIllumZonesGetControl>(ID_ClientIllumZonesGetControl);
            var setControl = GetDelegate<Delegate_ClientIllumZonesSetControl>(ID_ClientIllumZonesSetControl);

            // Every physical GPU NvAPI_EnumPhysicalGPUs returned, not just the first one.
            for (int gpuIndex = 0; gpuIndex < gpuCount; gpuIndex++)
            {
                Console.WriteLine();
                Console.WriteLine("=== GPU " + gpuIndex + " ===");
                try
                {
                    ProcessGpu(gpuHandles[gpuIndex], getInfo, getControl, setControl);
                }
                catch (Exception ex)
                {
                    // One GPU's failure shouldn't stop the rest from being processed.
                    Console.WriteLine("GPU " + gpuIndex + ": unexpected error: " + ex.Message);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unexpected error: " + ex.Message);
            return 0;
        }
        finally
        {
            if (initialized)
            {
                try
                {
                    var unload = GetDelegate<Delegate_Unload>(ID_Unload);
                    unload();
                }
                catch { /* ignore unload failures on exit */ }
            }
        }
    }
}