using UnityEngine;
using Evo.UI;
using TMPro;
using System.Collections;

[System.Serializable]
public class ReverbSliderConfig
{
    public Slider slider;
    public TMP_Text valueDisplay;  // Shows e.g. "Speech Reverb Preset: OPTIMIZED_Hypnosis <i>(27)</i>"
}

public class LiveReverbController : MonoBehaviour
{
    [Header("References")]
    public AudioSourceReverbPreset reverbPresetManager;

    [Header("Reverb Sliders")]
    public ReverbSliderConfig speechReverbSlider;
    public ReverbSliderConfig mantraReverbSlider;

    private bool isInitializing = false;
    private bool hasInitialized = false;

    private void Awake()
    {
        if (reverbPresetManager == null)
        {
            reverbPresetManager = FindAnyObjectByType<AudioSourceReverbPreset>();
        }
    }

    private void Start()
    {
        InitializeSliders();
        StartCoroutine(InitializeWithDelay());
    }

    private void OnEnable()
    {
        // LiveMixer may call ResetToStartValues directly.
        // This is kept as fallback or for standalone usage.
        if (hasInitialized)
        {
            SyncWithConfig();
        }
    }

    private IEnumerator InitializeWithDelay()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        SyncWithConfig();
        hasInitialized = true;
    }

    private void InitializeSliders()
    {
        SetupSlider(speechReverbSlider, OnSpeechValueChanged);
        SetupSlider(mantraReverbSlider, OnMantraValueChanged);
    }

    private void SetupSlider(ReverbSliderConfig config, UnityEngine.Events.UnityAction<float> callback)
    {
        if (config.slider == null)
            return;

        var slider = config.slider;
        slider.wholeNumbers = true;
        slider.minValue = AudioSourceReverbPreset.BuiltInPresetMin;
        slider.maxValue = AudioSourceReverbPreset.SupportedPresetMax; // 30

        config.slider.onValueChanged.RemoveListener(callback);
        config.slider.onValueChanged.AddListener(callback);
    }

    private void SyncWithConfig()
    {
        try
        {
            Config config = ConfigManager.Instance.LoadConfiguration();
            isInitializing = true;

            int speechPreset = SanitizePreset(config.reverbPresetSpeech);
            int mantraPreset = SanitizePreset(config.reverbPresetMantra);

            if (speechReverbSlider.slider != null)
            {
                speechReverbSlider.slider.value = speechPreset;
                UpdateValueDisplay(speechReverbSlider, speechPreset, "Speech Reverb Preset: ");
            }

            if (mantraReverbSlider.slider != null)
            {
                mantraReverbSlider.slider.value = mantraPreset;
                UpdateValueDisplay(mantraReverbSlider, mantraPreset, "Mantra Reverb Preset: ");
            }

            isInitializing = false;
        }
        catch (System.Exception e)
        {
            isInitializing = false;
            Debug.LogWarning($"LiveReverbController: Could not sync with config yet: {e.Message}");
        }
    }

    private void OnSpeechValueChanged(float value)
    {
        int preset = SanitizePreset(Mathf.RoundToInt(value));
        UpdateValueDisplay(speechReverbSlider, preset, "Speech Reverb Preset: ");

        if (!isInitializing && reverbPresetManager != null)
        {
            reverbPresetManager.SetSpeechPreset(preset);
        }
    }

    private void OnMantraValueChanged(float value)
    {
        int preset = SanitizePreset(Mathf.RoundToInt(value));
        UpdateValueDisplay(mantraReverbSlider, preset, "Mantra Reverb Preset: ");

        if (!isInitializing && reverbPresetManager != null)
        {
            reverbPresetManager.SetMantraPreset(preset);
        }
    }

    private void UpdateValueDisplay(ReverbSliderConfig config, int value, string prefix)
    {
        if (config.valueDisplay == null)
            return;

        string presetName = GetPresetName(value);
        config.valueDisplay.text = $"{prefix}{presetName} <i>({value})</i>";
    }

    private string GetPresetName(int presetIndex)
    {
        // Use the manager as the single source of truth if available
        if (reverbPresetManager != null)
        {
            return reverbPresetManager.GetPresetDisplayName(presetIndex);
        }

        // Fallback if manager is not assigned yet
        if (presetIndex >= AudioSourceReverbPreset.BuiltInPresetMin &&
            presetIndex <= AudioSourceReverbPreset.BuiltInPresetMax)
        {
            return ((AudioReverbPreset)presetIndex).ToString();
        }

        switch (presetIndex)
        {
            case AudioSourceReverbPreset.PresetHypnosis: return "OPTIMIZED_Hypnosis";
            case AudioSourceReverbPreset.PresetMeditation: return "OPTIMIZED_Meditation";
            case AudioSourceReverbPreset.PresetMantra: return "OPTIMIZED_Mantra";
            case AudioSourceReverbPreset.PresetSubliminal: return "OPTIMIZED_Subliminal";
            default: return "Unknown";
        }
    }

    private int SanitizePreset(int presetIndex)
    {
        return (presetIndex >= AudioSourceReverbPreset.BuiltInPresetMin &&
                presetIndex <= AudioSourceReverbPreset.SupportedPresetMax)
            ? presetIndex
            : (int)AudioReverbPreset.Off;
    }

    public void ResetToStartValues()
    {
        Config config = ConfigManager.Instance.LoadConfiguration();

        int speechPreset = SanitizePreset(config.reverbPresetSpeech);
        int mantraPreset = SanitizePreset(config.reverbPresetMantra);

        if (reverbPresetManager != null)
        {
            reverbPresetManager.SetSpeechPreset(speechPreset);
            reverbPresetManager.SetMantraPreset(mantraPreset);
        }

        SyncWithConfig();
    }
}