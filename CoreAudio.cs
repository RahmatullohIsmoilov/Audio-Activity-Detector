using System;
using System.Runtime.InteropServices;

namespace AudioActivityDetector.CoreAudio
{
    // ---------------------------------------------------------------------
    // Minimal COM interop surface for the Windows Core Audio API (WASAPI).
    // This talks directly to the OS audio engine (audiosrv) - the same
    // subsystem that powers the volume mixer's per-app meters - so it can
    // tell us whether the system's default output device currently has
    // any signal on it, without needing NAudio or any other NuGet package.
    // ---------------------------------------------------------------------

    public enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    public enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);

        int GetDefaultAudioEndpoint(
            EDataFlow dataFlow,
            ERole role,
            out IMMDevice ppEndpoint);

        int GetDevice(string pwstrId, out IMMDevice ppDevice);

        int RegisterEndpointNotificationCallback(IntPtr pClient);

        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);

        int GetId(out IntPtr ppstrId);

        int GetState(out int pdwState);
    }

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioMeterInformation
    {
        // Returns the peak sample value, 0.0 (silence) to 1.0 (full scale),
        // observed on the endpoint since the last call.
        int GetPeakValue(out float pfPeak);

        int GetMeteringChannelCount(out int pnChannelCount);

        int GetChannelsPeakValues(int u32ChannelCount, [MarshalAs(UnmanagedType.LPArray)] float[] afPeakValues);

        int QueryHardwareSupport(out int pdwHardwareSupportMask);
    }

    /// <summary>
    /// Thin, disposable wrapper that gives you the current peak output level
    /// (0.0 - 1.0) of the system's default audio playback device.
    /// </summary>
    public sealed class DefaultOutputMeter : IDisposable
    {
        private static readonly Guid IID_IAudioMeterInformation = typeof(IAudioMeterInformation).GUID;

        private IMMDevice? _device;
        private IAudioMeterInformation? _meter;
        private bool _disposed;

        public DefaultOutputMeter()
        {
            Connect();
        }

        private void Connect()
        {
            var enumeratorType = Type.GetTypeFromCLSID(typeof(MMDeviceEnumeratorComObject).GUID)
                ?? throw new InvalidOperationException("Could not resolve MMDeviceEnumerator COM class.");
            var enumeratorObj = Activator.CreateInstance(enumeratorType)
                ?? throw new InvalidOperationException("Could not create MMDeviceEnumerator instance.");
            var enumerator = (IMMDeviceEnumerator)enumeratorObj;

            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
            Marshal.ThrowExceptionForHR(hr);
            _device = device;

            var iid = IID_IAudioMeterInformation;
            hr = _device.Activate(ref iid, /* CLSCTX_ALL */ 23, IntPtr.Zero, out object meterObj);
            Marshal.ThrowExceptionForHR(hr);
            _meter = (IAudioMeterInformation)meterObj;
        }

        /// <summary>
        /// Current peak level of the default playback device, 0.0 to 1.0.
        /// Reconnects automatically if the default device changed or was
        /// unplugged since the last read.
        /// </summary>
        public float GetPeakValue()
        {
            if (_meter == null)
            {
                Connect();
            }

            try
            {
                int hr = _meter!.GetPeakValue(out float peak);
                if (hr < 0)
                {
                    // Device likely went away (unplugged, disabled, default changed).
                    Reset();
                    Connect();
                    _meter!.GetPeakValue(out peak);
                }
                return peak;
            }
            catch (COMException)
            {
                Reset();
                Connect();
                _meter!.GetPeakValue(out float peak);
                return peak;
            }
        }

        private void Reset()
        {
            if (_meter != null && Marshal.IsComObject(_meter)) Marshal.ReleaseComObject(_meter);
            if (_device != null && Marshal.IsComObject(_device)) Marshal.ReleaseComObject(_device);
            _meter = null;
            _device = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Reset();
            _disposed = true;
        }
    }
}
