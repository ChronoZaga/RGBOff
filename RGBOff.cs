using System;
using System.Runtime.InteropServices;

internal static class RGBOff
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    private static extern IntPtr nvapi_QueryInterface(uint interfaceId);
    private const uint ID_Initialize                = 0x0150E828;
    private const uint ID_Unload                     = 0xD22BDD7E;
    private const uint ID_EnumPhysicalGPUs           = 0xE5AC921F;
    private const uint ID_ClientIllumZonesGetInfo    = 0x4B81241B;
    private const uint ID_ClientIllumZonesGetControl = 0x3DBF5764; 
    private const uint ID_ClientIllumZonesSetControl = 0x197D065E;
    private const int NVAPI_MAX_PHYSICAL_GPUS = 32;
    private const int MAX_ILLUM_ZONES = 32;
    private const int COLOR_BYTES = 4;
    private const int NVAPI_OK = 0;
    private const int NVAPI_INCOMPATIBLE_STRUCT_VERSION = -9;
    private const int NVAPI_NOT_SUPPORTED = -104;

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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_ClientIllumZonesGetControl(
        IntPtr gpuHandle, ref NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 control);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_ClientIllumZonesSetControl(
        IntPtr gpuHandle, ref NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 control);

    private const uint ZONE_TYPE_INVALID = 0;
    private const uint ZONE_TYPE_RGB = 1;
    private const uint ZONE_TYPE_FIXED_COLOR = 2;

    private const uint CONTROL_MODE_MANUAL_RGB = 0;

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_INFO_V1
    {
        public uint ZoneType;
        public byte DeviceIndex;
        public byte ProviderIndex;
        public uint ZoneLocation;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] 
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
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 192)] 
        public byte[] Data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1
    {
        public uint Version;
        public uint Flags;               
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

    private static void ProcessGpu(
        IntPtr gpu,
        Delegate_ClientIllumZonesGetInfo getInfo,
        Delegate_ClientIllumZonesGetControl getControl,
        Delegate_ClientIllumZonesSetControl setControl)
    {
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

        var before = NewControlParams();

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

        var after = before; 
        after.ZoneControls = (NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1[])before.ZoneControls.Clone();
        after.Flags = 0; 
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
                return 0; 
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