// RGBOff.cs
//
// Minimal, dependency-free tool that turns off NVIDIA GPU illumination (RGB) zones
// by calling nvapi64.dll directly via P/Invoke + nvapi_QueryInterface function-pointer
// resolution. No NvAPIWrapper.dll, no NuGet packages.
//
// Function IDs and struct layouts verified against two independent sources:
//   1. falahati/NvAPIWrapper (open-source C# NVAPI wrapper)
//      https://github.com/falahati/NvAPIWrapper
//      - NvAPIWrapper/Native/Helpers/FunctionId.cs
//      - NvAPIWrapper/Native/GPU/Structures/IlluminationZone*.cs
//   2. NVIDIA's own official open-source header repo
//      https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h
//      (byte-identical hex IDs for every function used below)
//   Struct field order additionally cross-checked against NVIDIA's public
//   NVAPI reference docs (docs.nvidia.com/nvapi/struct__NV__GPU__CLIENT__ILLUM__ZONE__CONTROL__PARAMS__V1.html)
//
using System;
using System.Runtime.InteropServices;

internal static class RGBOff
{
    // ---- nvapi64.dll entry point --------------------------------------------------
    // Every real NVAPI function (other than this one) is resolved at runtime by ID
    // through NvAPI_QueryInterface, because Microsoft/NVIDIA do not export the
    // individual function names from nvapi64.dll.
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    private static extern IntPtr nvapi_QueryInterface(uint interfaceId);

    // ---- Verified function IDs (see header comment for sources) -------------------
    private const uint ID_Initialize                    = 0x0150E828;
    private const uint ID_Unload                         = 0xD22BDD7E;
    private const uint ID_EnumPhysicalGPUs               = 0xE5AC921F;
    private const uint ID_ClientIllumZonesGetInfo        = 0x4B81241B;
    private const uint ID_ClientIllumZonesSetControl     = 0x197D065E;

    private const int NVAPI_MAX_PHYSICAL_GPUS = 32; // NvAPIWrapper: PhysicalGPUHandle.PhysicalGPUs
    private const int MAX_ILLUM_ZONES = 32;         // NV_GPU_CLIENT_ILLUM_ZONE_NUM_ZONES_MAX

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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Delegate_ClientIllumZonesSetControl(
        IntPtr gpuHandle, ref NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1 control);

    // ---- Zone type / control mode enums (from IlluminationZoneType.cs / IlluminationZoneControlMode.cs) ----
    private const uint ZONE_TYPE_INVALID = 0;
    private const uint ZONE_TYPE_RGB = 1;
    private const uint ZONE_TYPE_FIXED_COLOR = 2;

    private const uint CONTROL_MODE_MANUAL_RGB = 0;

    // ---- Structures -----------------------------------------------------------------
    // NV_GPU_CLIENT_ILLUM_ZONE_INFO_V1 (per-zone static info)
    // Source: NvAPIWrapper/Native/GPU/Structures/IlluminationZoneInfoV1.cs, Pack = 8
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_INFO_V1
    {
        public uint ZoneType;       // IlluminationZoneType
        public byte DeviceIndex;
        public byte ProviderIndex;
        public uint ZoneLocation;   // IlluminationZoneLocation (note: enum is uint, but sits after 2 bytes;
                                     // matches source layout byte,byte,uint with Pack=8 -> compiler inserts
                                     // 2 bytes padding before ZoneLocation, matching native layout)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Data;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Reserved;
    }

    // NV_GPU_CLIENT_ILLUM_ZONE_INFO_PARAMS_V1
    // Source: NvAPIWrapper/Native/GPU/Structures/IlluminationZoneInfoParametersV1.cs
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

    // NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1 (per-zone control entry)
    // Source: NvAPIWrapper/Native/GPU/Structures/IlluminationZoneControlV1.cs, Pack = 8
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1
    {
        public uint ZoneType;     // IlluminationZoneType
        public uint ControlMode;  // IlluminationZoneControlMode
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] Data;       // holds NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_DATA_RGB (64 data + 64 reserved)
                                   // whose first 4 bytes (manual mode) are R,G,B,Brightness%
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Reserved;
    }

    // NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1
    // Source: NvAPIWrapper/Native/GPU/Structures/IlluminationZoneControlParametersV1.cs
    // Confirmed field order against NVIDIA's own doc page:
    // https://docs.nvidia.com/nvapi/struct__NV__GPU__CLIENT__ILLUM__ZONE__CONTROL__PARAMS__V1.html
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

    // NVAPI version-encoding convention: version dword = sizeof(struct) | (structVersion << 16)
    private static uint MakeVersion(int structSize, int structVersion)
    {
        return (uint)(structSize | (structVersion << 16));
    }

    private static T GetDelegate<T>(uint functionId) where T : class
    {
        IntPtr ptr = nvapi_QueryInterface(functionId);
        if (ptr == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException(
                "nvapi_QueryInterface returned null for function ID 0x" + functionId.ToString("X8"));
        }
        return Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
    }

    private static int Main()
    {
        bool initialized = false;
        try
        {
            var initialize = GetDelegate<Delegate_Initialize>(ID_Initialize);
            int initStatus = initialize();
            if (initStatus != 0)
            {
                Console.WriteLine("NvAPI_Initialize failed with status " + initStatus + ". Aborting.");
                return 0; // Not a crash; just nothing we can do (e.g. no NVIDIA driver present).
            }
            initialized = true;

            var enumGpus = GetDelegate<Delegate_EnumPhysicalGPUs>(ID_EnumPhysicalGPUs);
            IntPtr[] gpuHandles = new IntPtr[NVAPI_MAX_PHYSICAL_GPUS];
            uint gpuCount;
            int enumStatus = enumGpus(gpuHandles, out gpuCount);
            if (enumStatus != 0 || gpuCount == 0)
            {
                Console.WriteLine("NvAPI_EnumPhysicalGPUs failed with status " + enumStatus + " (count=" + gpuCount + ").");
                return 0;
            }

            IntPtr gpu = gpuHandles[0];

            var getInfo = GetDelegate<Delegate_ClientIllumZonesGetInfo>(ID_ClientIllumZonesGetInfo);
            var infoParams = new NV_GPU_CLIENT_ILLUM_ZONE_INFO_PARAMS_V1
            {
                Reserved = new byte[64],
                Zones = new NV_GPU_CLIENT_ILLUM_ZONE_INFO_V1[MAX_ILLUM_ZONES]
            };
            infoParams.Version = MakeVersion(Marshal.SizeOf(typeof(NV_GPU_CLIENT_ILLUM_ZONE_INFO_PARAMS_V1)), 1);

            int infoStatus = getInfo(gpu, ref infoParams);
            if (infoStatus != 0)
            {
                // NVAPI_NOT_SUPPORTED (-8) is expected/legitimate on GPUs without illumination zones.
                Console.WriteLine("NvAPI_GPU_ClientIllumZonesGetInfo returned status " + infoStatus +
                    " (this is informational, e.g. NVAPI_NOT_SUPPORTED if the GPU has no RGB zones).");
                return 0;
            }

            uint zoneCount = infoParams.NumberOfZones;
            if (zoneCount == 0)
            {
                Console.WriteLine("GPU reports 0 illumination zones. Nothing to turn off.");
                return 0;
            }

            var controlParams = new NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1
            {
                Reserved = new byte[64],
                ZoneControls = new NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1[MAX_ILLUM_ZONES],
                Flags = 0, // bit0 = 0 => CurrentlyActive (matches original script's IlluminationZoneControlValuesType.CurrentlyActive)
                NumberOfZoneControls = zoneCount
            };
            controlParams.Version = MakeVersion(Marshal.SizeOf(typeof(NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_PARAMS_V1)), 1);

            for (int i = 0; i < zoneCount; i++)
            {
                uint zoneType = infoParams.Zones[i].ZoneType;
                var zoneControl = new NV_GPU_CLIENT_ILLUM_ZONE_CONTROL_V1
                {
                    ZoneType = zoneType,
                    ControlMode = CONTROL_MODE_MANUAL_RGB,
                    Data = new byte[128], // all-zero => R=0,G=0,B=0,Brightness=0 (or fixed-color brightness 0)
                    Reserved = new byte[64]
                };
                controlParams.ZoneControls[i] = zoneControl;
            }

            var setControl = GetDelegate<Delegate_ClientIllumZonesSetControl>(ID_ClientIllumZonesSetControl);
            int setStatus = setControl(gpu, ref controlParams);
            if (setStatus != 0)
            {
                Console.WriteLine("NvAPI_GPU_ClientIllumZonesSetControl returned status " + setStatus + ".");
            }
            else
            {
                Console.WriteLine("OK: turned off " + zoneCount + " illumination zone(s) on GPU 0.");
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
                catch
                {
                    // ignore unload failures on exit
                }
            }
        }
    }
}
