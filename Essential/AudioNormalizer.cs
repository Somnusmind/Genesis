using UnityEngine;
using System;
using System.Runtime.InteropServices;

public static class AudioNormalizer
{
    private static bool useRMS = false;
    private const float DefaultTargetRmsDb = -18.0f;
    private static float targetLUFS = -18.0f;

    public static AudioClip NormalizeClip(AudioClip clip, float? targetDbOrLUFS = null)
    {
        if (clip == null)
        {
            Debug.LogError("AudioNormalizer: Clip is null!");
            return null;
        }

        if (clip.samples == 0 || clip.length <= 0)
        {
            Debug.LogWarning("AudioNormalizer: Clip is empty or has no samples!");
            return clip;
        }

        return useRMS
            ? NormalizeClipRMS(clip, targetDbOrLUFS ?? DefaultTargetRmsDb)
            : NormalizeClipLUFS_Lib(clip, targetDbOrLUFS ?? targetLUFS);
    }

    private static AudioClip NormalizeClipRMS(AudioClip clip, float targetRmsDb)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        if (samples.Length == 0)
        {
            Debug.LogWarning("RMS Normalization: No samples found!");
            return clip;
        }

        float currentRms = CalculateRMS(samples);
        if (currentRms < 1e-10f)
        {
            Debug.LogWarning("RMS Normalization: Clip is silent!");
            return clip;
        }

        float targetRmsLinear = Mathf.Pow(10f, targetRmsDb / 20f);
        float gain = targetRmsLinear / currentRms;

        float[] normalizedSamples = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            normalizedSamples[i] = Mathf.Clamp(samples[i] * gain, -1.0f, 1.0f);
        }

        AudioClip normalizedClip = AudioClip.Create(
            clip.name + "_NormalizedRMS",
            clip.samples,
            clip.channels,
            clip.frequency,
            false
        );
        normalizedClip.SetData(normalizedSamples, 0);

        Debug.Log($"RMS Normalized: {clip.name} | " +
                 $"Original: {LinearToDb(currentRms):F2}dB -> " +
                 $"Target: {targetRmsDb:F2}dB | " +
                 $"Gain: {LinearToDb(gain):F2}dB");

        return normalizedClip;
    }

    private static AudioClip NormalizeClipLUFS_Lib(AudioClip clip, float targetIntegratedLUFS)
    {
        float[] samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0))
        {
            Debug.LogError("LUFS Normalization: Failed to get audio data!");
            return clip;
        }

        float maxAbsSample = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            maxAbsSample = Mathf.Max(maxAbsSample, Mathf.Abs(samples[i]));
        }

        if (maxAbsSample < 0.0001f)
        {
            Debug.LogWarning("LUFS Normalization: Audio is too quiet for measurement");

            float[] quietSamples = new float[samples.Length];
            float quietAudioGain = 0.1f;

            for (int i = 0; i < samples.Length; i++)
            {
                quietSamples[i] = Mathf.Clamp(samples[i] * quietAudioGain, -1.0f, 1.0f);
            }

            AudioClip quietClip = AudioClip.Create(
                clip.name + "_NormalizedQuiet",
                clip.samples,
                clip.channels,
                clip.frequency,
                false
            );
            quietClip.SetData(quietSamples, 0);

            Debug.Log($"LUFS Normalization: Applied fixed gain to quiet audio: {clip.name}");
            return quietClip;
        }

        int mode = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3);

        Ebur128Integration.Ebur128StatePtr statePtr =
            Ebur128Integration.ebur128_init((uint)clip.channels, (uint)clip.frequency, mode);

        if (statePtr.Ptr == IntPtr.Zero)
        {
            Debug.LogError("LUFS Normalization: Failed to initialize libebur128!");
            return clip;
        }

        double measuredLUFS = -23.0;
        bool success = false;

        try
        {
            const int CHUNK_SIZE = 4800;
            uint frames = (uint)(samples.Length / clip.channels);

            for (uint offset = 0; offset < frames; offset += CHUNK_SIZE)
            {
                uint chunkFrames = Math.Min(CHUNK_SIZE, frames - offset);
                if (chunkFrames == 0) break;

                float[] chunkSamples = new float[chunkFrames * clip.channels];
                Array.Copy(samples, offset * clip.channels, chunkSamples, 0, chunkFrames * clip.channels);

                int chunkResult = Ebur128Integration.ebur128_add_frames_float(statePtr, chunkSamples, chunkFrames);
                if (chunkResult != 0)
                {
                    Debug.LogWarning($"LUFS Normalization: Failed to add chunk {offset}/{frames}");
                }
            }

            int globalResult = Ebur128Integration.ebur128_loudness_global(statePtr, out measuredLUFS);
            if (globalResult != 0)
            {
                double shortTermLoudness = 0;
                if (Ebur128Integration.ebur128_loudness_shortterm(statePtr, out shortTermLoudness) == 0)
                {
                    measuredLUFS = shortTermLoudness;
                    success = true;
                    Debug.Log("LUFS Normalization: Used short-term loudness as fallback");
                }
                else
                {
                    double momentaryLoudness = 0;
                    if (Ebur128Integration.ebur128_loudness_momentary(statePtr, out momentaryLoudness) == 0)
                    {
                        measuredLUFS = momentaryLoudness;
                        success = true;
                        Debug.Log("LUFS Normalization: Used momentary loudness as fallback");
                    }
                    else
                    {
                        Debug.LogWarning("LUFS Normalization: All loudness measurements failed");
                        success = false;
                    }
                }
            }
            else
            {
                success = true;
            }
        }
        finally
        {
            Ebur128Integration.ebur128_destroy(ref statePtr);
        }

        if (!success || double.IsNegativeInfinity(measuredLUFS) || measuredLUFS < -70.0)
        {
            Debug.LogWarning($"LUFS Normalization: Invalid measurement ({measuredLUFS:F2} LUFS), using default level");
            measuredLUFS = -23.0;
        }

        double gainDb = targetIntegratedLUFS - measuredLUFS;

        if (Math.Abs(gainDb) > 40.0)
        {
            Debug.LogWarning($"LUFS Normalization: Limiting excessive gain ({gainDb:F2}dB) to +/-40dB");
            gainDb = Math.Sign(gainDb) * 40.0;
        }

        float gainLinear = DbToLinear((float)gainDb);

        float[] lufsNormalizedSamples = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            lufsNormalizedSamples[i] = Mathf.Clamp(samples[i] * gainLinear, -1.0f, 1.0f);
        }

        AudioClip lufsNormalizedClip = AudioClip.Create(
            clip.name + "_NormalizedLUFS",
            clip.samples,
            clip.channels,
            clip.frequency,
            false
        );
        lufsNormalizedClip.SetData(lufsNormalizedSamples, 0);

        Debug.Log($"LUFS Normalized: {clip.name} | " +
                $"Original: {measuredLUFS:F2} LUFS -> " +
                $"Target: {targetIntegratedLUFS:F2} LUFS | " +
                $"Gain: {gainDb:F2}dB");

        return lufsNormalizedClip;
    }

    private static float CalculateRMS(float[] samples)
    {
        double sum = 0;
        foreach (float sample in samples)
        {
            sum += sample * sample;
        }
        return Mathf.Sqrt((float)(sum / samples.Length));
    }

    private static float LinearToDb(float linear) =>
        linear <= 1e-10f ? -200f : 20f * Mathf.Log10(linear);

    private static float DbToLinear(float db) =>
        Mathf.Pow(10f, db / 20f);
}

public static class Ebur128Integration
{
    private const string LibName = "libebur128";

    public struct Ebur128StatePtr
    {
        public IntPtr Ptr;
    }

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern Ebur128StatePtr ebur128_init(uint channels, uint samplerate, int mode);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ebur128_destroy(ref Ebur128StatePtr st);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ebur128_add_frames_float(Ebur128StatePtr st, float[] src, uint frames);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ebur128_loudness_global(Ebur128StatePtr st, out double loudness);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ebur128_loudness_momentary(Ebur128StatePtr st, out double loudness);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ebur128_loudness_shortterm(Ebur128StatePtr st, out double loudness);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ebur128_true_peak(Ebur128StatePtr st, uint channel, out double peak);

    public static bool CheckResult(int result, string functionName)
    {
        if (result != 0)
        {
            Debug.LogError($"libebur128 error in {functionName}: Code {result}");
            return false;
        }
        return true;
    }
}