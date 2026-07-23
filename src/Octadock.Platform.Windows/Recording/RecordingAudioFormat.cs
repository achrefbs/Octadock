using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Recording;

/// <summary>
/// Media Foundation plumbing for the recording audio tracks: AAC output / PCM
/// input media-type construction and the canonical float→PCM16 conversion. The
/// attribute GUIDs are the well-known <c>mfapi.h</c> audio keys; they are kept
/// local to the recording engine instead of widening the shared interop surface.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class RecordingAudioFormat
{
    // Canonical format for every recording audio track.
    internal const uint SampleRate = 48_000;
    internal const uint Channels = 2;
    internal const uint BitsPerSample = 16;
    internal const uint AacAvgBytesPerSecond = 24_000; // 192 kbps AAC.

    // mfapi.h — major type / subtypes.
    private static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_AAC = new("00001610-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00aa00389b71");

    // mfapi.h — audio media-type attributes.
    private static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");

    /// <summary>Encoded (output) media type for one AAC audio stream. The caller owns the returned pointer.</summary>
    internal static nint CreateAacOutputMediaType()
        => CreateAudioMediaType(MFAudioFormat_AAC, avgBytesPerSecond: AacAvgBytesPerSecond, blockAlignment: 0, allSamplesIndependent: false);

    /// <summary>Uncompressed (input) media type matching the canonical PCM the audio pump feeds. The caller owns the returned pointer.</summary>
    internal static nint CreatePcmInputMediaType()
    {
        uint blockAlignment = Channels * (BitsPerSample / 8);
        return CreateAudioMediaType(
            MFAudioFormat_PCM,
            avgBytesPerSecond: SampleRate * blockAlignment,
            blockAlignment: blockAlignment,
            allSamplesIndependent: true);
    }

    private static nint CreateAudioMediaType(Guid subtype, uint avgBytesPerSecond, uint blockAlignment, bool allSamplesIndependent)
    {
        int hr = MediaFoundation.MFCreateMediaType(out nint typePtr);
        Marshal.ThrowExceptionForHR(hr);

        IMFMediaType? mediaType = null;
        try
        {
            mediaType = (IMFMediaType)Marshal.GetObjectForIUnknown(typePtr);

            Guid major = MFMediaType_Audio;
            Guid majorKey = MfGuids.MF_MT_MAJOR_TYPE;
            Marshal.ThrowExceptionForHR(mediaType.SetGUID(ref majorKey, ref major));

            Guid subtypeKey = MfGuids.MF_MT_SUBTYPE;
            Marshal.ThrowExceptionForHR(mediaType.SetGUID(ref subtypeKey, ref subtype));

            Guid rateKey = MF_MT_AUDIO_SAMPLES_PER_SECOND;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref rateKey, SampleRate));

            Guid channelsKey = MF_MT_AUDIO_NUM_CHANNELS;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref channelsKey, Channels));

            Guid bitsKey = MF_MT_AUDIO_BITS_PER_SAMPLE;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref bitsKey, BitsPerSample));

            Guid avgBytesKey = MF_MT_AUDIO_AVG_BYTES_PER_SECOND;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref avgBytesKey, avgBytesPerSecond));

            if (blockAlignment > 0)
            {
                Guid blockAlignmentKey = MF_MT_AUDIO_BLOCK_ALIGNMENT;
                Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref blockAlignmentKey, blockAlignment));
            }

            if (allSamplesIndependent)
            {
                Guid independentKey = MF_MT_ALL_SAMPLES_INDEPENDENT;
                Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref independentKey, 1));
            }

            // The caller owns the returned pointer; keep the raw pointer alive.
            return typePtr;
        }
        catch
        {
            if (typePtr != nint.Zero)
            {
                Marshal.Release(typePtr);
            }

            throw;
        }
        finally
        {
            if (mediaType is not null)
            {
                Marshal.ReleaseComObject(mediaType);
            }
        }
    }

    /// <summary>
    /// Converts interleaved float samples (−1..1) to little-endian 16-bit PCM,
    /// clamping out-of-range peaks instead of wrapping. Returns the byte count
    /// written to <paramref name="destination"/>.
    /// </summary>
    internal static int FloatToPcm16(ReadOnlySpan<float> source, int frames, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);

        int sampleCount = frames * (int)Channels;
        if (source.Length < sampleCount)
        {
            throw new ArgumentException("The source does not hold a whole chunk of frames.", nameof(source));
        }

        int byteCount = sampleCount * 2;
        if (destination.Length < byteCount)
        {
            throw new ArgumentException("The destination is too small for the chunk.", nameof(destination));
        }

        for (int i = 0; i < sampleCount; i++)
        {
            float clamped = Math.Clamp(source[i], -1f, 1f);
            short value = (short)Math.Round(clamped * short.MaxValue, MidpointRounding.AwayFromZero);
            destination[i * 2] = (byte)(value & 0xFF);
            destination[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return byteCount;
    }
}
