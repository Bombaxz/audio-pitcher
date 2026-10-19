using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioPitcher
{
    /// <summary>Avisa cuando cambia el dispositivo de salida predeterminado de Windows.
    /// El callback llega en un hilo COM: despachar al hilo de UI antes de tocar controles.</summary>
    internal sealed class DefaultDeviceWatcher : IMMNotificationClient, IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new();

        public event Action? DefaultRenderChanged;

        public DefaultDeviceWatcher() => _enumerator.RegisterEndpointNotificationCallback(this);

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                DefaultRenderChanged?.Invoke();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            _enumerator.Dispose();
        }
    }
}
