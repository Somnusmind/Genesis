using UnityEngine;
using UnityEngine.Audio;

public class AudioSourceNoiseManager : MonoBehaviour
{
    public AudioSource audioSourceNoise;
    public AudioReverbFilter audioReverbFilter;

    [Header("Audio Clips")]
    public AudioClip whitenoiseClip;
    public AudioClip pinknoiseClip;
    public AudioClip brownnoiseClip;

    private const float SILENCE_DURATION_SECONDS = 10f; // 10 seconds silence
    
    public void LoadConfig()
    {
        ConfigManager manager = new ConfigManager();
        Config config = manager.LoadConfiguration();

        // Set Whitenoise Clip
        switch (config.noiseMode)
        {
            case -1:
                audioSourceNoise.clip = GenerateSilenceClip(SILENCE_DURATION_SECONDS);
                break;
            case 0:
                audioSourceNoise.clip = whitenoiseClip;
                break;
            case 1:
                audioSourceNoise.clip = pinknoiseClip;
                break;
            case 2:
                audioSourceNoise.clip = brownnoiseClip;
                break;
            default:
                audioSourceNoise.clip = whitenoiseClip;
                break;
        }
    }

    private AudioClip GenerateSilenceClip(float durationSeconds)
    {
        int sampleRate = 48000; 
        int channels = 1;       

        int sampleCount = Mathf.CeilToInt(durationSeconds * sampleRate) * channels;
        float[] samples = new float[sampleCount];   // Initialized to 0.0 by default (silence)

        AudioClip silentClip = AudioClip.Create("SilentClip", sampleCount / channels, channels, sampleRate, false);
        silentClip.SetData(samples, 0);
        return silentClip;
    }

    private bool IsValidReverbPreset(int presetIndex)
    {
        return presetIndex >= 0 && presetIndex < System.Enum.GetValues(typeof(AudioReverbPreset)).Length;
    }
}
