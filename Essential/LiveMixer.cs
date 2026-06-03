using UnityEngine;
using UnityEngine.Audio;
using System;
using System.Collections.Generic;
using Evo.UI;

[Serializable]
public class MixerSliderPair
{
    public string parameterName;
    public Slider slider;
    [HideInInspector] public float initialValue; // Store first sync value
}

public class LiveMixer : MonoBehaviour
{
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private List<MixerSliderPair> mixerControls = new List<MixerSliderPair>();

    [Header("Optional Reverb Controller")]
    public LiveReverbController reverbController;

    private bool hasStoredInitialValues = false;

    // Cached parameter name to avoid string allocation in loops
    private string masterVolumeParameterName = "master_track.attenuation.volume";

    private void Awake()
    {
        InitializeMixerAndSliders();
    }


    // Called by SceneManager_SessionWizard during the initialization phase.
    public void LoadConfig()
    {
        // This calls the existing logic to reset volumes to config defaults
        // while preserving the Master track fade-in/out transitions.
        ResetToStartValuesWithoutMaster();
    }

    private void InitializeMixerAndSliders()
    {
        foreach (var control in mixerControls)
        {
            control.slider.onValueChanged.AddListener((float value) =>
            {
                audioMixer.SetFloat(control.parameterName, value);
            });
        }
    }

    private void Update()
    {
        // Only sync Master volume from mixer to slider during runtime to reflect fade progress
        // Other sliders are driven by user input or specific initialization
        foreach (var control in mixerControls)
        {
            // Check if this is the Master parameter (case-insensitive to be safe)
            if (string.Equals(control.parameterName, masterVolumeParameterName, StringComparison.OrdinalIgnoreCase))
            {
                float currentValue;
                if (audioMixer.GetFloat(control.parameterName, out currentValue))
                {
                    if (control.slider.value != currentValue)
                    {
                        control.slider.value = currentValue;
                    }
                }
            }
        }
    }

    public void SyncSlidersWithMixer()
    {
        foreach (var control in mixerControls)
        {
            float mixerValue;
            audioMixer.GetFloat(control.parameterName, out mixerValue);
            control.slider.value = mixerValue;

            // Store initial values only on the first sync
            if (!hasStoredInitialValues)
            {
                control.initialValue = mixerValue;
            }
        }

        hasStoredInitialValues = true;
    }

    public void ResetToStartValues()
    {
        // Load configuration from ConfigManager
        Config config = ConfigManager.Instance.LoadConfiguration();

        // Reset slider values to those in the config file
        foreach (var control in mixerControls)
        {
            float configValue = GetConfigValueForParameter(config, control.parameterName);
            control.slider.value = configValue;
            audioMixer.SetFloat(control.parameterName, configValue);
        }

        // Reset Master Volume to 0 (since it's not in the config file)
        ResetMasterVolume();

        // Also reset reverb if controller is assigned
        reverbController?.ResetToStartValues();
    }

    // Resets all tracks to config values EXCEPT the Master track.
    public void ResetToStartValuesWithoutMaster()
    {
        Config config = ConfigManager.Instance.LoadConfiguration();

        foreach (var control in mixerControls)
        {
            // Check if this is the Master parameter (case-insensitive)
            if (string.Equals(control.parameterName, masterVolumeParameterName, StringComparison.OrdinalIgnoreCase))
            {
                // DO NOT set the mixer value for Master to avoid interrupting the fade.
                // Instead, sync the slider UI to the current mixer value (which is being controlled by the fade).
                float currentMixerValue;
                if (audioMixer.GetFloat(control.parameterName, out currentMixerValue))
                {
                    control.slider.value = currentMixerValue;
                    control.initialValue = currentMixerValue;
                }
            }
            else
            {
                // For all other tracks (Speech, SFX, etc.), reset to config values
                float configValue = GetConfigValueForParameter(config, control.parameterName);

                // Set the mixer first
                audioMixer.SetFloat(control.parameterName, configValue);

                // Then update the slider UI
                control.slider.value = configValue;
                control.initialValue = configValue;
            }
        }

        hasStoredInitialValues = true;

        // Reset Reverb settings from config
        reverbController?.ResetToStartValues();
    }

    private void ResetMasterVolume()
    {
        if (audioMixer.GetFloat("Master_Track.Attenuation.Volume", out _)) // Check if MasterVolume is exposed
        {
            audioMixer.SetFloat("Master_Track.Attenuation.Volume", 0f);
        }
        else
        {
            Debug.LogError("Exposed parameter 'Master_Track.Attenuation.Volume' does not exist in the Audio Mixer. Please expose it in Unity.");
        }
    }

    private float GetConfigValueForParameter(Config config, string parameterName)
    {
        // Normalize the parameter name to lowercase for comparison
        string normalizedParameterName = parameterName.ToLower();

        switch (normalizedParameterName)
        {
            case "master_track.attenuation.volume":
                return 0f; // Master Volume is not in the config file
            case "speech_track.attenuation.volume":
                return config.speechAttenuationVolume;
            case "sfx_track.attenuation.volume":
                return config.sfxAttenuationVolume;
            case "bet_track.attenuation.volume":
                return config.betAttenuationVolume;
            case "whitenoise_track.attenuation.volume":
                return config.noiseAttenuationVolume;
            case "mantra_track.attenuation.volume":
                return config.mantraAttenuationVolume;
            default:
                Debug.LogWarning($"Parameter '{parameterName}' not found in config. Returning 0.");
                return 0f;
        }
    }
}