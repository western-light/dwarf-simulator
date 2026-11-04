#include "miniaudio.h"
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define PURRVOICE_API __declspec(dllexport)
#else
#define PURRVOICE_API __attribute__((visibility("default")))
#endif

#define PURRVOICE_SUCCESS 0
#define PURRVOICE_ERROR -1
#define PURRVOICE_ERROR_INVALID_ARGUMENT -2
#define PURRVOICE_ERROR_NOT_INITIALIZED -3
#define PURRVOICE_ERROR_ALREADY_RUNNING -4
#define PURRVOICE_ERROR_NOT_RUNNING -5
#define PURRVOICE_ERROR_DEVICE_NOT_FOUND -6

static ma_context g_context;
static ma_bool32 g_contextInitialized;
static ma_device_info* g_captureDevices;
static ma_uint32 g_captureDeviceCount;
static int g_defaultCaptureDeviceIndex = -1;
static ma_device g_captureDevice;
static ma_bool32 g_captureRunning;
static ma_pcm_rb g_captureRing;
static ma_bool32 g_captureRingInitialized;
static ma_uint32 g_captureSampleRate;
static char g_lastError[256];

static void PurrVoiceNativeAudio_SetLastError(const char* message)
{
    if (message == NULL)
        message = "";

    strncpy(g_lastError, message, sizeof(g_lastError) - 1);
    g_lastError[sizeof(g_lastError) - 1] = '\0';
}

static int PurrVoiceNativeAudio_EnsureContext(void)
{
    if (g_contextInitialized)
        return PURRVOICE_SUCCESS;

    if (ma_context_init(NULL, 0, NULL, &g_context) != MA_SUCCESS)
    {
        PurrVoiceNativeAudio_SetLastError("Failed to initialize miniaudio context.");
        return PURRVOICE_ERROR;
    }

    g_contextInitialized = MA_TRUE;
    return PURRVOICE_SUCCESS;
}

static int PurrVoiceNativeAudio_HexValue(char value)
{
    if (value >= '0' && value <= '9')
        return value - '0';

    if (value >= 'a' && value <= 'f')
        return value - 'a' + 10;

    if (value >= 'A' && value <= 'F')
        return value - 'A' + 10;

    return -1;
}

static int PurrVoiceNativeAudio_WriteText(char* destination, int destinationLength, const char* source)
{
    size_t length;

    if (destination == NULL || destinationLength <= 0)
        return PURRVOICE_ERROR_INVALID_ARGUMENT;

    if (source == NULL)
        source = "";

    length = strlen(source);
    if (length >= (size_t)destinationLength)
        length = (size_t)destinationLength - 1;

    memcpy(destination, source, length);
    destination[length] = '\0';
    return (int)length;
}

static int PurrVoiceNativeAudio_DeviceIdToString(const ma_device_id* id, char* destination, int destinationLength)
{
    static const char hex[] = "0123456789ABCDEF";
    const unsigned char* bytes = (const unsigned char*)id;
    int requiredLength = (int)(sizeof(ma_device_id) * 2);
    int i;

    if (destination == NULL || destinationLength <= 0 || id == NULL)
        return PURRVOICE_ERROR_INVALID_ARGUMENT;

    if (destinationLength <= requiredLength)
    {
        destination[0] = '\0';
        return PURRVOICE_ERROR_INVALID_ARGUMENT;
    }

    for (i = 0; i < (int)sizeof(ma_device_id); i++)
    {
        destination[i * 2] = hex[(bytes[i] >> 4) & 0x0F];
        destination[i * 2 + 1] = hex[bytes[i] & 0x0F];
    }

    destination[requiredLength] = '\0';
    return requiredLength;
}

static int PurrVoiceNativeAudio_DeviceIdFromString(const char* source, ma_device_id* id)
{
    unsigned char* bytes = (unsigned char*)id;
    size_t requiredLength = sizeof(ma_device_id) * 2;
    size_t i;

    if (source == NULL || source[0] == '\0' || id == NULL)
        return PURRVOICE_ERROR_INVALID_ARGUMENT;

    if (strlen(source) != requiredLength)
        return PURRVOICE_ERROR_INVALID_ARGUMENT;

    for (i = 0; i < sizeof(ma_device_id); i++)
    {
        int high = PurrVoiceNativeAudio_HexValue(source[i * 2]);
        int low = PurrVoiceNativeAudio_HexValue(source[i * 2 + 1]);

        if (high < 0 || low < 0)
            return PURRVOICE_ERROR_INVALID_ARGUMENT;

        bytes[i] = (unsigned char)((high << 4) | low);
    }

    return PURRVOICE_SUCCESS;
}

static const ma_device_id* PurrVoiceNativeAudio_FindDeviceId(const char* deviceId)
{
    ma_device_id requestedId;
    ma_uint32 i;

    if (deviceId == NULL || deviceId[0] == '\0')
        return NULL;

    if (PurrVoiceNativeAudio_DeviceIdFromString(deviceId, &requestedId) != PURRVOICE_SUCCESS)
        return NULL;

    for (i = 0; i < g_captureDeviceCount; i++)
    {
        if (ma_device_id_equal(&requestedId, &g_captureDevices[i].id))
            return &g_captureDevices[i].id;
    }

    return NULL;
}

static void PurrVoiceNativeAudio_DataCallback(ma_device* device, void* output, const void* input, ma_uint32 frameCount)
{
    ma_uint32 framesToWrite = frameCount;
    ma_uint32 writableFrames;
    const float* source = (const float*)input;
    ma_uint32 sourceOffset = 0;

    (void)output;
    (void)device;

    if (source == NULL || !g_captureRingInitialized)
        return;

    writableFrames = ma_pcm_rb_available_write(&g_captureRing);
    if (framesToWrite > writableFrames)
    {
        ma_uint32 framesToDrop = framesToWrite - writableFrames;
        ma_uint32 readableFrames = ma_pcm_rb_available_read(&g_captureRing);

        if (framesToDrop > readableFrames)
            framesToDrop = readableFrames;

        if (framesToDrop > 0)
            ma_pcm_rb_seek_read(&g_captureRing, framesToDrop);

        writableFrames = ma_pcm_rb_available_write(&g_captureRing);
        if (framesToWrite > writableFrames)
        {
            sourceOffset = framesToWrite - writableFrames;
            framesToWrite = writableFrames;
        }
    }

    while (framesToWrite > 0)
    {
        ma_uint32 writeFrames = framesToWrite;
        void* destination = NULL;

        if (ma_pcm_rb_acquire_write(&g_captureRing, &writeFrames, &destination) != MA_SUCCESS || writeFrames == 0)
            return;

        memcpy(destination, source + sourceOffset, writeFrames * sizeof(float));
        ma_pcm_rb_commit_write(&g_captureRing, writeFrames);
        framesToWrite -= writeFrames;
        sourceOffset += writeFrames;
    }
}

PURRVOICE_API const char* PurrVoiceNativeAudio_GetMiniaudioVersion(void)
{
    return ma_version_string();
}

PURRVOICE_API void PurrVoiceNativeAudio_GetMiniaudioVersionNumbers(int* major, int* minor, int* revision)
{
    ma_version(major, minor, revision);
}

PURRVOICE_API int PurrVoiceNativeAudio_GetLastError(char* destination, int destinationLength)
{
    return PurrVoiceNativeAudio_WriteText(destination, destinationLength, g_lastError);
}

PURRVOICE_API int PurrVoiceNativeAudio_RefreshDevices(void)
{
    ma_device_info* playbackDevices;
    ma_uint32 playbackDeviceCount;
    ma_device_info* captureDevices;
    ma_uint32 captureDeviceCount;
    ma_uint32 i;

    if (PurrVoiceNativeAudio_EnsureContext() != PURRVOICE_SUCCESS)
        return PURRVOICE_ERROR_NOT_INITIALIZED;

    if (ma_context_get_devices(&g_context, &playbackDevices, &playbackDeviceCount, &captureDevices, &captureDeviceCount) != MA_SUCCESS)
    {
        PurrVoiceNativeAudio_SetLastError("Failed to enumerate capture devices.");
        return PURRVOICE_ERROR;
    }

    free(g_captureDevices);
    g_captureDevices = NULL;
    g_captureDeviceCount = 0;
    g_defaultCaptureDeviceIndex = -1;

    if (captureDeviceCount > 0)
    {
        g_captureDevices = (ma_device_info*)malloc(sizeof(ma_device_info) * captureDeviceCount);
        if (g_captureDevices == NULL)
        {
            PurrVoiceNativeAudio_SetLastError("Failed to allocate capture device list.");
            return PURRVOICE_ERROR;
        }

        memcpy(g_captureDevices, captureDevices, sizeof(ma_device_info) * captureDeviceCount);
        g_captureDeviceCount = captureDeviceCount;

        for (i = 0; i < g_captureDeviceCount; i++)
        {
            if (g_captureDevices[i].isDefault)
            {
                g_defaultCaptureDeviceIndex = (int)i;
                break;
            }
        }
    }

    PurrVoiceNativeAudio_SetLastError("");
    return (int)g_captureDeviceCount;
}

PURRVOICE_API int PurrVoiceNativeAudio_GetDeviceCount(void)
{
    return (int)g_captureDeviceCount;
}

PURRVOICE_API int PurrVoiceNativeAudio_GetDefaultDeviceIndex(void)
{
    return g_defaultCaptureDeviceIndex;
}

PURRVOICE_API int PurrVoiceNativeAudio_GetDeviceName(int deviceIndex, char* destination, int destinationLength)
{
    if (deviceIndex < 0 || deviceIndex >= (int)g_captureDeviceCount)
        return PURRVOICE_ERROR_DEVICE_NOT_FOUND;

    return PurrVoiceNativeAudio_WriteText(destination, destinationLength, g_captureDevices[deviceIndex].name);
}

PURRVOICE_API int PurrVoiceNativeAudio_GetDeviceId(int deviceIndex, char* destination, int destinationLength)
{
    if (deviceIndex < 0 || deviceIndex >= (int)g_captureDeviceCount)
        return PURRVOICE_ERROR_DEVICE_NOT_FOUND;

    return PurrVoiceNativeAudio_DeviceIdToString(&g_captureDevices[deviceIndex].id, destination, destinationLength);
}

PURRVOICE_API int PurrVoiceNativeAudio_StartCapture(const char* deviceId, int sampleRate, int bufferMilliseconds)
{
    ma_device_config config;
    ma_uint32 ringFrames;
    const ma_device_id* captureDeviceId;

    if (g_captureRunning)
        return PURRVOICE_ERROR_ALREADY_RUNNING;

    if (PurrVoiceNativeAudio_EnsureContext() != PURRVOICE_SUCCESS)
        return PURRVOICE_ERROR_NOT_INITIALIZED;

    if (g_captureDevices == NULL && PurrVoiceNativeAudio_RefreshDevices() < 0)
        return PURRVOICE_ERROR_NOT_INITIALIZED;

    captureDeviceId = PurrVoiceNativeAudio_FindDeviceId(deviceId);
    if (deviceId != NULL && deviceId[0] != '\0' && captureDeviceId == NULL)
    {
        PurrVoiceNativeAudio_SetLastError("Capture device was not found.");
        return PURRVOICE_ERROR_DEVICE_NOT_FOUND;
    }

    if (sampleRate <= 0)
        sampleRate = 48000;

    if (bufferMilliseconds <= 0)
        bufferMilliseconds = 1000;

    ringFrames = (ma_uint32)((sampleRate * bufferMilliseconds) / 1000);
    if (ringFrames < 1024)
        ringFrames = 1024;

    if (ma_pcm_rb_init(ma_format_f32, 1, ringFrames, NULL, NULL, &g_captureRing) != MA_SUCCESS)
    {
        PurrVoiceNativeAudio_SetLastError("Failed to initialize capture buffer.");
        return PURRVOICE_ERROR;
    }

    g_captureRingInitialized = MA_TRUE;
    ma_pcm_rb_set_sample_rate(&g_captureRing, (ma_uint32)sampleRate);

    config = ma_device_config_init(ma_device_type_capture);
    config.capture.pDeviceID = captureDeviceId;
    config.capture.format = ma_format_f32;
    config.capture.channels = 1;
    config.sampleRate = (ma_uint32)sampleRate;
    config.dataCallback = PurrVoiceNativeAudio_DataCallback;

    if (ma_device_init(&g_context, &config, &g_captureDevice) != MA_SUCCESS)
    {
        ma_pcm_rb_uninit(&g_captureRing);
        g_captureRingInitialized = MA_FALSE;
        PurrVoiceNativeAudio_SetLastError("Failed to initialize capture device.");
        return PURRVOICE_ERROR;
    }

    if (ma_device_start(&g_captureDevice) != MA_SUCCESS)
    {
        ma_device_uninit(&g_captureDevice);
        ma_pcm_rb_uninit(&g_captureRing);
        g_captureRingInitialized = MA_FALSE;
        PurrVoiceNativeAudio_SetLastError("Failed to start capture device.");
        return PURRVOICE_ERROR;
    }

    g_captureSampleRate = (ma_uint32)sampleRate;
    g_captureRunning = MA_TRUE;
    PurrVoiceNativeAudio_SetLastError("");
    return PURRVOICE_SUCCESS;
}

PURRVOICE_API int PurrVoiceNativeAudio_StopCapture(void)
{
    if (!g_captureRunning)
        return PURRVOICE_ERROR_NOT_RUNNING;

    ma_device_uninit(&g_captureDevice);
    g_captureRunning = MA_FALSE;
    g_captureSampleRate = 0;

    if (g_captureRingInitialized)
    {
        ma_pcm_rb_uninit(&g_captureRing);
        g_captureRingInitialized = MA_FALSE;
    }

    return PURRVOICE_SUCCESS;
}

PURRVOICE_API int PurrVoiceNativeAudio_IsCapturing(void)
{
    return g_captureRunning ? 1 : 0;
}

PURRVOICE_API int PurrVoiceNativeAudio_GetCaptureSampleRate(void)
{
    return (int)g_captureSampleRate;
}

PURRVOICE_API int PurrVoiceNativeAudio_GetAvailableSamples(void)
{
    if (!g_captureRingInitialized)
        return 0;

    return (int)ma_pcm_rb_available_read(&g_captureRing);
}

PURRVOICE_API int PurrVoiceNativeAudio_ReadSamples(float* destination, int maxSampleCount)
{
    ma_uint32 samplesToRead;
    ma_uint32 samplesRead = 0;

    if (destination == NULL || maxSampleCount <= 0)
        return PURRVOICE_ERROR_INVALID_ARGUMENT;

    if (!g_captureRingInitialized)
        return 0;

    samplesToRead = (ma_uint32)maxSampleCount;
    while (samplesToRead > 0)
    {
        ma_uint32 readFrames = samplesToRead;
        void* source = NULL;

        if (ma_pcm_rb_acquire_read(&g_captureRing, &readFrames, &source) != MA_SUCCESS || readFrames == 0)
            break;

        memcpy(destination + samplesRead, source, readFrames * sizeof(float));
        ma_pcm_rb_commit_read(&g_captureRing, readFrames);
        samplesToRead -= readFrames;
        samplesRead += readFrames;
    }

    return (int)samplesRead;
}

PURRVOICE_API int PurrVoiceNativeAudio_ClearCaptureBuffer(void)
{
    if (!g_captureRingInitialized)
        return PURRVOICE_ERROR_NOT_RUNNING;

    ma_pcm_rb_reset(&g_captureRing);
    return PURRVOICE_SUCCESS;
}

PURRVOICE_API void PurrVoiceNativeAudio_Shutdown(void)
{
    if (g_captureRunning)
        PurrVoiceNativeAudio_StopCapture();

    free(g_captureDevices);
    g_captureDevices = NULL;
    g_captureDeviceCount = 0;
    g_defaultCaptureDeviceIndex = -1;

    if (g_contextInitialized)
    {
        ma_context_uninit(&g_context);
        g_contextInitialized = MA_FALSE;
    }

    PurrVoiceNativeAudio_SetLastError("");
}
