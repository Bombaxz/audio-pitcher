using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace AudioPitcher
{
    /// <summary>
    /// Captura el audio de un proceso concreto usando la Process Loopback API
    /// (ActivateAudioInterfaceAsync + AUDIOCLIENT_ACTIVATION_PARAMS). Requiere Windows build 20348+.
    /// </summary>
    internal sealed class ProcessLoopbackCapture : IWaveIn
    {
        private const string VirtualDevicePath = "VAD\\Process_Loopback";

        private const int  ShareModeShared          = 0;
        private const int  StreamFlagsLoopback      = 0x00020000;
        private const int  StreamFlagsEventCallback = 0x00040000;
        private const long BufferDurationHns        = 200_000;   // 20 ms
        private const int  BufferFlagsSilent        = 0x2;

        private static readonly Guid IID_IAudioClient        = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

        private IAudioClient?        _client;
        private IAudioCaptureClient? _captureClient;
        private EventWaitHandle?     _bufferReady;
        private Thread?              _thread;
        private volatile bool        _running;

        public int ProcessId { get; }
        public WaveFormat WaveFormat { get; set; }

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public static bool IsSupported => Environment.OSVersion.Version.Build >= 20348;

        public ProcessLoopbackCapture(int processId)
        {
            ProcessId  = processId;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        }

        public void StartRecording()
        {
            if (_running) return;
            if (!IsSupported)
                throw new InvalidOperationException("La captura por app requiere Windows 10 21H1 / Windows 11 o superior.");

            // La activación tiene que correr en un hilo MTA: el completion handler
            // vuelve en un hilo del pool y bloquear el hilo STA de la UI puede colgar COM.
            Task.Run(() =>
            {
                int hr = TryInit(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
                if (hr != 0)
                    hr = TryInit(new WaveFormat(48000, 16, 2));
                if (hr != 0)
                    Marshal.ThrowExceptionForHR(hr);

                _bufferReady = new EventWaitHandle(false, EventResetMode.AutoReset);
                Marshal.ThrowExceptionForHR(_client!.SetEventHandle(_bufferReady.SafeWaitHandle.DangerousGetHandle()));
                Marshal.ThrowExceptionForHR(_client.GetService(IID_IAudioCaptureClient, out object svc));
                _captureClient = (IAudioCaptureClient)svc;
                Marshal.ThrowExceptionForHR(_client.Start());
            }).GetAwaiter().GetResult();

            _running = true;
            _thread  = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "ProcessLoopbackCapture"
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private int TryInit(WaveFormat fmt)
        {
            IAudioClient client = Activate();
            var sessionGuid = Guid.Empty;
            int hr = client.Initialize(ShareModeShared,
                                       StreamFlagsLoopback | StreamFlagsEventCallback,
                                       BufferDurationHns, 0, fmt, ref sessionGuid);
            if (hr != 0)
            {
                Marshal.ReleaseComObject(client);
                return hr;
            }
            _client    = client;
            WaveFormat = fmt;
            return 0;
        }

        private IAudioClient Activate()
        {
            var loopbackParams = new AudioClientActivationParams
            {
                ActivationType      = 1,               // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                TargetProcessId     = (uint)ProcessId,
                ProcessLoopbackMode = 0                // INCLUDE_TARGET_PROCESS_TREE
            };

            int    blobSize = Marshal.SizeOf<AudioClientActivationParams>();
            IntPtr blob     = Marshal.AllocCoTaskMem(blobSize);
            IntPtr propVar  = Marshal.AllocCoTaskMem(Marshal.SizeOf<PropVariantBlob>());
            try
            {
                Marshal.StructureToPtr(loopbackParams, blob, false);
                Marshal.StructureToPtr(new PropVariantBlob
                {
                    Vt        = 65,                    // VT_BLOB
                    CbSize    = blobSize,
                    PBlobData = blob
                }, propVar, false);

                var handler = new ActivationHandler();
                ActivateAudioInterfaceAsync(VirtualDevicePath, IID_IAudioClient, propVar, handler, out _);

                if (!handler.Completed.WaitOne(5000))
                    throw new TimeoutException("Timeout activando la captura del proceso.");
                Marshal.ThrowExceptionForHR(handler.Hr);

                return (IAudioClient)handler.Interface!;
            }
            finally
            {
                Marshal.FreeCoTaskMem(propVar);
                Marshal.FreeCoTaskMem(blob);
            }
        }

        private void CaptureLoop()
        {
            Exception? error      = null;
            int        blockAlign = WaveFormat.BlockAlign;
            try
            {
                while (_running)
                {
                    if (!_bufferReady!.WaitOne(200)) continue;

                    while (_running)
                    {
                        if (_captureClient!.GetNextPacketSize(out int packetFrames) != 0 || packetFrames == 0)
                            break;
                        if (_captureClient.GetBuffer(out IntPtr data, out int frames, out int flags, out _, out _) != 0)
                            break;

                        int bytes  = frames * blockAlign;
                        var buffer = new byte[bytes];
                        if (bytes > 0 && data != IntPtr.Zero && (flags & BufferFlagsSilent) == 0)
                            Marshal.Copy(data, buffer, 0, bytes);

                        _captureClient.ReleaseBuffer(frames);

                        if (bytes > 0) DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                    }
                }
            }
            catch (Exception ex) { error = ex; }

            RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
        }

        public void StopRecording()
        {
            if (!_running && _client == null) return;

            _running = false;
            _thread?.Join(600);
            _thread = null;

            try { _client?.Stop(); } catch { }

            if (_captureClient != null) { Marshal.ReleaseComObject(_captureClient); _captureClient = null; }
            if (_client        != null) { Marshal.ReleaseComObject(_client);        _client        = null; }

            _bufferReady?.Dispose();
            _bufferReady = null;
        }

        public void Dispose() => StopRecording();

        // ── Interop ──────────────────────────────────────────────────────────

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioClientActivationParams
        {
            public int  ActivationType;
            public uint TargetProcessId;
            public int  ProcessLoopbackMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariantBlob
        {
            public ushort Vt;
            public ushort R1, R2, R3;
            public int    CbSize;
            public IntPtr PBlobData;
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult([MarshalAs(UnmanagedType.Error)] out int activateResult,
                                   [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
        }

        // Sin IAgileObject el callback intentaría volver por un proxy y se cuelga.
        [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAgileObject { }

        private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            public readonly ManualResetEvent Completed = new(false);
            public int     Hr;
            public object? Interface;

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
            {
                try
                {
                    activateOperation.GetActivateResult(out Hr, out object iface);
                    Interface = iface;
                }
                catch (Exception ex)
                {
                    Hr = ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
                }
                finally { Completed.Set(); }
            }
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration,
                                         long hnsPeriodicity, [In] WaveFormat format, ref Guid audioSessionGuid);
            [PreserveSig] int GetBufferSize(out int numBufferFrames);
            [PreserveSig] int GetStreamLatency(out long streamLatency);
            [PreserveSig] int GetCurrentPadding(out int numPaddingFrames);
            [PreserveSig] int IsFormatSupported(int shareMode, [In] WaveFormat format, IntPtr closestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
                                         [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr dataBuffer, out int numFramesToRead, out int bufferFlags,
                                        out long devicePosition, out long qpcPosition);
            [PreserveSig] int ReleaseBuffer(int numFramesRead);
            [PreserveSig] int GetNextPacketSize(out int numFramesInNextPacket);
        }
    }
}
