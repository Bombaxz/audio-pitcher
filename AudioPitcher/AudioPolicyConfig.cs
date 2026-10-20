using System;
using System.Runtime.InteropServices;

namespace AudioPitcher
{
    /// <summary>
    /// Cambia el dispositivo de salida de una app concreta — lo mismo que
    /// Configuración › Sonido › Mezclador de volumen, pero por código.
    /// Usa IAudioPolicyConfig (API no documentada, la que usan EarTrumpet/SoundSwitch):
    /// se llama por vtable porque .NET 8 ya no marshalea WinRT.
    /// </summary>
    internal static class AudioPolicyConfig
    {
        private const string ActivatableClassId = "Windows.Media.Internal.AudioPolicyConfig";

        // El vtable arranca con IInspectable (6 slots) + 19 métodos que no usamos
        // (volumen de grupo, vibración, chat apps — ver IAudioPolicyConfigFactory de EarTrumpet).
        private const int SlotSetPersistedDefaultAudioEndpoint = 25;
        private const int SlotGetPersistedDefaultAudioEndpoint = 26;

        private static readonly Guid IID_Win11 = new("ab3d4648-e242-459f-b02f-541c70306324");
        private static readonly Guid IID_Win10 = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

        private const int FlowRender = 0;
        private static readonly int[] AllRoles = { 0, 1, 2 };   // eConsole, eMultimedia, eCommunications

        // DEVINTERFACE_AUDIO_RENDER
        private const string RenderInterfaceGuid = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";

        private static IntPtr _factory = IntPtr.Zero;

        public static bool IsSupported
        {
            get
            {
                try { return GetFactory() != IntPtr.Zero; }
                catch { return false; }
            }
        }

        /// <summary>Rutea la app al dispositivo dado. mmDeviceId vacío = volver al default del sistema.</summary>
        public static void SetDefaultEndpoint(int processId, string mmDeviceId)
        {
            IntPtr factory = GetFactory();
            var    setFn   = GetMethod<SetPersistedDefaultAudioEndpointFn>(factory, SlotSetPersistedDefaultAudioEndpoint);

            string persisted = string.IsNullOrEmpty(mmDeviceId)
                ? ""
                : $@"\\?\SWD#MMDEVAPI#{mmDeviceId}#{RenderInterfaceGuid}";

            IntPtr hstr = CreateHString(persisted);
            try
            {
                foreach (int role in AllRoles)
                    Marshal.ThrowExceptionForHR(setFn(factory, (uint)processId, FlowRender, role, hstr));
            }
            finally { DeleteHString(hstr); }

            string actual = GetDefaultEndpoint(processId);
            string expected = mmDeviceId ?? "";
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"El ruteo no se aplicó (esperado '{expected}', quedó '{actual}'). " +
                    "Posible cambio de layout de IAudioPolicyConfig en esta build de Windows.");
        }

        /// <summary>Devuelve el mmDeviceId al que está ruteada la app, o "" si usa el default.</summary>
        public static string GetDefaultEndpoint(int processId)
        {
            IntPtr factory = GetFactory();
            var    getFn   = GetMethod<GetPersistedDefaultAudioEndpointFn>(factory, SlotGetPersistedDefaultAudioEndpoint);

            IntPtr hstr = IntPtr.Zero;
            try
            {
                int hr = getFn(factory, (uint)processId, FlowRender, 1 /* eMultimedia */, out hstr);
                if (hr != 0 || hstr == IntPtr.Zero) return "";

                string persisted = ReadHString(hstr);
                if (string.IsNullOrEmpty(persisted)) return "";

                // \\?\SWD#MMDEVAPI#{0.0.0.x}.{guid}#{interface-guid}  →  {0.0.0.x}.{guid}
                int start = persisted.IndexOf("MMDEVAPI#", StringComparison.OrdinalIgnoreCase);
                if (start < 0) return "";
                start += "MMDEVAPI#".Length;
                int end = persisted.IndexOf('#', start);
                return end < 0 ? persisted[start..] : persisted[start..end];
            }
            catch { return ""; }
            finally { if (hstr != IntPtr.Zero) DeleteHString(hstr); }
        }

        // ── Salida predeterminada de todo el sistema ─────────────────────────
        // IPolicyConfig (no documentada, estable desde Windows 7; la usan SoundSwitch/EarTrumpet).

        private static readonly Guid CLSID_PolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
        private static readonly Guid IID_IPolicyConfig        = new("f8679f50-850a-41cf-9c72-430f290290c8");
        private const int SlotSetDefaultEndpoint = 13;   // IUnknown (3) + 10 métodos previos

        /// <summary>Pone mmDeviceId como salida predeterminada de Windows en todos los roles.</summary>
        public static void SetSystemDefaultRender(string mmDeviceId)
        {
            var clsid = CLSID_PolicyConfigClient;
            var iid   = IID_IPolicyConfig;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, ref iid, out IntPtr pc));
            try
            {
                var setFn = GetMethod<SetDefaultEndpointFn>(pc, SlotSetDefaultEndpoint);
                foreach (int role in AllRoles)
                    Marshal.ThrowExceptionForHR(setFn(pc, mmDeviceId, role));
            }
            finally { Marshal.Release(pc); }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetDefaultEndpointFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int context, ref Guid iid, out IntPtr obj);

        // ── Interop ──────────────────────────────────────────────────────────

        private static IntPtr GetFactory()
        {
            if (_factory != IntPtr.Zero) return _factory;

            IntPtr classId = CreateHString(ActivatableClassId);
            try
            {
                var iid = IID_Win11;
                int hr  = RoGetActivationFactory(classId, ref iid, out IntPtr factory);
                if (hr != 0)
                {
                    iid = IID_Win10;
                    hr  = RoGetActivationFactory(classId, ref iid, out factory);
                }
                Marshal.ThrowExceptionForHR(hr);
                _factory = factory;
                return _factory;
            }
            finally { DeleteHString(classId); }
        }

        private static T GetMethod<T>(IntPtr comObject, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            IntPtr method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(method);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPersistedDefaultAudioEndpointFn(
            IntPtr self, uint processId, int flow, int role, IntPtr deviceId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPersistedDefaultAudioEndpointFn(
            IntPtr self, uint processId, int flow, int role, out IntPtr deviceId);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

        private static IntPtr CreateHString(string value)
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(value, value.Length, out IntPtr hstr));
            return hstr;
        }

        private static void DeleteHString(IntPtr hstring)
        {
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
        }

        private static string ReadHString(IntPtr hstring)
        {
            IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
            return buffer == IntPtr.Zero || length == 0 ? "" : Marshal.PtrToStringUni(buffer, (int)length);
        }
    }
}
