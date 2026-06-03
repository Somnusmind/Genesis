using UnityEngine;
using System.IO;
using System;

public static class SavWav
{
    // QUALITY INDEX
    // 0 = 16-bit PCM WAV (smaller file size, faster loading)
    // 1 = 32-bit IEEE Float WAV (highest quality, larger file size)
    // Change this value to switch format globally.
    public static int qualityIndex = 0;

    private const int HEADER_SIZE = 44;

    // 32-bit IEEE Float constants
    private const UInt16 AUDIO_FORMAT_FLOAT = 3;
    private const int BYTES_PER_SAMPLE_FLOAT = 4;
    private const int BITS_PER_SAMPLE_FLOAT = 32;

    // 16-bit PCM constants
    private const UInt16 AUDIO_FORMAT_PCM = 1;
    private const int BYTES_PER_SAMPLE_PCM = 2;
    private const int BITS_PER_SAMPLE_PCM = 16;

    // Keep original method signature for backwards compatibility
    public static bool Save(string fullPath, AudioClip clip)
    {
        try
        {
            if (clip == null) return false;

            // Maintain original filename handling
            if (!fullPath.ToLower().EndsWith(".wav"))
            {
                fullPath += ".wav";
            }

            using (var fileStream = CreateEmpty(fullPath))
            {
                WriteAudioData(fileStream, clip);
                WriteHeader(fileStream, clip);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving WAV file: {ex.Message}");
            return false;
        }
    }

    private static FileStream CreateEmpty(string filepath)
    {
        var fileStream = new FileStream(filepath, FileMode.Create);
        byte emptyByte = new byte();

        for (int i = 0; i < HEADER_SIZE; i++)
        {
            fileStream.WriteByte(emptyByte);
        }

        return fileStream;
    }

    private static void WriteAudioData(FileStream fileStream, AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        if (qualityIndex == 0)
        {
            // 16-bit PCM: Convert float samples to Int16
            Int16[] int16Samples = new Int16[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                float sample = Mathf.Clamp(samples[i], -1f, 1f);
                int16Samples[i] = (Int16)(sample * 32767f);
            }

            byte[] bytesData = new byte[int16Samples.Length * BYTES_PER_SAMPLE_PCM];
            Buffer.BlockCopy(int16Samples, 0, bytesData, 0, bytesData.Length);
            fileStream.Write(bytesData, 0, bytesData.Length);
        }
        else
        {
            // 32-bit IEEE Float: Original behavior
            var bytesData = new byte[samples.Length * BYTES_PER_SAMPLE_FLOAT];
            Buffer.BlockCopy(samples, 0, bytesData, 0, bytesData.Length);
            fileStream.Write(bytesData, 0, bytesData.Length);
        }
    }

    private static void WriteHeader(FileStream fileStream, AudioClip clip)
    {
        var hz = clip.frequency;
        var channels = clip.channels;
        var samples = clip.samples;

        fileStream.Seek(0, SeekOrigin.Begin);

        // RIFF chunk
        var riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        fileStream.Write(riff, 0, 4);

        // Determine format-specific values
        UInt16 audioFormat;
        int bytesPerSample;
        int bitsPerSample;

        if (qualityIndex == 0)
        {
            audioFormat = AUDIO_FORMAT_PCM;
            bytesPerSample = BYTES_PER_SAMPLE_PCM;
            bitsPerSample = BITS_PER_SAMPLE_PCM;
        }
        else
        {
            audioFormat = AUDIO_FORMAT_FLOAT;
            bytesPerSample = BYTES_PER_SAMPLE_FLOAT;
            bitsPerSample = BITS_PER_SAMPLE_FLOAT;
        }

        var chunkSize = BitConverter.GetBytes(fileStream.Length - 8);
        fileStream.Write(chunkSize, 0, 4);

        var wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        fileStream.Write(wave, 0, 4);

        // fmt chunk
        var fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        fileStream.Write(fmt, 0, 4);

        var subChunk1 = BitConverter.GetBytes(16);
        fileStream.Write(subChunk1, 0, 4);

        fileStream.Write(BitConverter.GetBytes(audioFormat), 0, 2);
        fileStream.Write(BitConverter.GetBytes((ushort)channels), 0, 2);
        fileStream.Write(BitConverter.GetBytes(hz), 0, 4);
        fileStream.Write(BitConverter.GetBytes(hz * channels * bytesPerSample), 0, 4);
        fileStream.Write(BitConverter.GetBytes((ushort)(channels * bytesPerSample)), 0, 2);
        fileStream.Write(BitConverter.GetBytes((ushort)bitsPerSample), 0, 2);

        // data chunk
        var dataString = System.Text.Encoding.UTF8.GetBytes("data");
        fileStream.Write(dataString, 0, 4);

        var subChunk2 = BitConverter.GetBytes(samples * channels * bytesPerSample);
        fileStream.Write(subChunk2, 0, 4);
    }
}