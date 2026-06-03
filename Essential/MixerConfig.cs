using UnityEngine;
using UnityEngine.Audio;
using System.IO;
using YamlDotNet.Serialization;

public class MixerConfig : MonoBehaviour
{
    public AudioMixer mixerMain;

    [Header("Master Audio")]
    [Tooltip("-1 = disabled, 0-200 Hz = highpass cutoff frequency")]
    private float masterHighpassCutoff;

    [Header("Speech Channel")]
    private float speechAttenuationVolume;
    private float speechEchoWet;
    private float speechEchoDelay;

    [Header("Mantra Channel")]
    private float mantraAttenuationVolume;
    private float mantraEchoWet;
    private float mantraEchoDelay;

    [Header("SFX Channel")]
    private float sfxAttenuationVolume;

    [Header("BET Channel")]
    private float betAttenuationVolume;

    [Header("Whitenoise Channel")]
    private float noiseAttenuationVolume;

    public void LoadConfig()
    {
        Debug.Log("Load Config of mixer has been called");
        string path = ConfigPath.Path;

        if (File.Exists(path))
        {
            string yaml = File.ReadAllText(path);

            var deserializer = new DeserializerBuilder().Build();
            Config config = deserializer.Deserialize<Config>(yaml);

            // Assign values from config
            masterHighpassCutoff = config.masterHighpassCutoff;

            speechAttenuationVolume = config.speechAttenuationVolume;
            speechEchoWet = config.speechEchoWet;
            speechEchoDelay = config.speechEchoDelay;

            mantraAttenuationVolume = config.mantraAttenuationVolume;
            mantraEchoWet = config.mantraEchoWet;
            mantraEchoDelay = config.mantraEchoDelay;

            sfxAttenuationVolume = config.sfxAttenuationVolume;
            betAttenuationVolume = config.betAttenuationVolume;
            noiseAttenuationVolume = config.noiseAttenuationVolume;

            SetMixerValues();
        }
        else
        {
            Debug.LogWarning("Configuration file not found!");
        }
    }

    public void SetMixerValues()
    {
        // Master Audio
        if (masterHighpassCutoff < 0)
        {
            // Disable highpass filter by setting to 0 (Unity's minimum)
            mixerMain.SetFloat("Master_Track.HighpassSimple.CutoffFreq", 0f);
        }
        else
        {
            mixerMain.SetFloat("Master_Track.HighpassSimple.CutoffFreq", masterHighpassCutoff);
        }

        // Speech Channel
        mixerMain.SetFloat("Speech_Track.Attenuation.Volume", speechAttenuationVolume);
        mixerMain.SetFloat("Speech_Track.Echo.Wet", speechEchoWet);
        mixerMain.SetFloat("Speech_Track.Echo.Delay", speechEchoDelay);

        // sFX Channel
        mixerMain.SetFloat("sFX_Track.Attenuation.Volume", sfxAttenuationVolume);

        // B.E.T. Channel
        mixerMain.SetFloat("BET_Track.Attenuation.Volume", betAttenuationVolume);

        // Whitenoise Channel
        mixerMain.SetFloat("Whitenoise_Track.Attenuation.Volume", noiseAttenuationVolume);

        // Mantra Channel
        mixerMain.SetFloat("Mantra_Track.Attenuation.Volume", mantraAttenuationVolume);
        mixerMain.SetFloat("Mantra_Track.Echo.Wet", mantraEchoWet);
        mixerMain.SetFloat("Mantra_Track.Echo.Delay", mantraEchoDelay);
    }
}