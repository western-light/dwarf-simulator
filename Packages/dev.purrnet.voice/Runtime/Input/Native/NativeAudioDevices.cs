using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PurrNet.Voice
{
    public static class NativeAudioDevices
    {
        private const int NameBufferSize = 512;
        private const int IdBufferSize = 1024;
        private static readonly List<NativeInputDevice> Devices = new();
        private static readonly StringBuilder TextBuffer = new(NameBufferSize);

        /// <summary>
        /// Available native microphone devices from the latest refresh.
        /// </summary>
        public static IReadOnlyList<NativeInputDevice> devices => Devices;

        /// <summary>
        /// Default native microphone device from the latest refresh.
        /// </summary>
        public static NativeInputDevice defaultDevice { get; private set; }

        /// <summary>
        /// Last native audio error reported by the plugin.
        /// </summary>
        public static string lastError
        {
            get
            {
                TextBuffer.Clear();
                try
                {
                    NativeAudioPlugin.GetLastError(TextBuffer, TextBuffer.Capacity);
                    return TextBuffer.ToString();
                }
                catch (DllNotFoundException)
                {
                    return "Native audio plugin was not found.";
                }
                catch (EntryPointNotFoundException)
                {
                    return "Native audio plugin is out of date.";
                }
            }
        }

        /// <summary>
        /// Refreshes the native microphone device list.
        /// </summary>
        public static bool Refresh()
        {
            Devices.Clear();
            defaultDevice = null;

            int count;
            try
            {
                count = NativeAudioPlugin.RefreshDevices();
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }

            if (count <= 0)
                return count == 0;

            int defaultIndex = NativeAudioPlugin.GetDefaultDeviceIndex();
            var nameBuilder = new StringBuilder(NameBufferSize);
            var idBuilder = new StringBuilder(IdBufferSize);

            for (int i = 0; i < count; i++)
            {
                nameBuilder.Clear();
                idBuilder.Clear();

                if (NativeAudioPlugin.GetDeviceName(i, nameBuilder, nameBuilder.Capacity) < 0)
                    continue;

                if (NativeAudioPlugin.GetDeviceId(i, idBuilder, idBuilder.Capacity) < 0)
                    continue;

                var device = new NativeInputDevice(idBuilder.ToString(), nameBuilder.ToString(), i == defaultIndex);
                Devices.Add(device);

                if (i == defaultIndex)
                    defaultDevice = device;
            }

            if (defaultDevice == null && Devices.Count > 0)
                defaultDevice = Devices[0];

            return true;
        }

        /// <summary>
        /// Gets a native microphone device by its persistent native id.
        /// </summary>
        public static NativeInputDevice GetDevice(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < Devices.Count; i++)
            {
                if (Devices[i].id == id)
                    return Devices[i];
            }

            return null;
        }

        internal static void Shutdown()
        {
            NativeAudioPlugin.Shutdown();
            Devices.Clear();
            defaultDevice = null;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            try
            {
                Shutdown();
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }

        internal static class NativeAudioPlugin
        {
            private const string LibraryName = "PurrVoiceNativeAudio";

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_RefreshDevices")]
            internal static extern int RefreshDevices();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_GetDeviceCount")]
            internal static extern int GetDeviceCount();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_GetDefaultDeviceIndex")]
            internal static extern int GetDefaultDeviceIndex();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_GetDeviceName", CharSet = CharSet.Ansi)]
            internal static extern int GetDeviceName(int deviceIndex, StringBuilder destination, int destinationLength);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_GetDeviceId", CharSet = CharSet.Ansi)]
            internal static extern int GetDeviceId(int deviceIndex, StringBuilder destination, int destinationLength);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_StartCapture", CharSet = CharSet.Ansi)]
            internal static extern int StartCapture(string deviceId, int sampleRate, int bufferMilliseconds);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_StopCapture")]
            internal static extern int StopCapture();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_IsCapturing")]
            internal static extern int IsCapturing();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_GetCaptureSampleRate")]
            internal static extern int GetCaptureSampleRate();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_GetAvailableSamples")]
            internal static extern int GetAvailableSamples();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_ReadSamples")]
            internal static extern int ReadSamples(float[] destination, int maxSampleCount);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_ClearCaptureBuffer")]
            internal static extern int ClearCaptureBuffer();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_GetLastError", CharSet = CharSet.Ansi)]
            internal static extern int GetLastError(StringBuilder destination, int destinationLength);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PurrVoiceNativeAudio_Shutdown")]
            internal static extern void Shutdown();
        }
    }
}
