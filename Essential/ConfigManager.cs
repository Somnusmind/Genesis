using System;
using System.IO;
using UnityEngine;
using YamlDotNet.Serialization;

public class ConfigManager
{
    private static ConfigManager instance;

    public static ConfigManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new ConfigManager();
            }
            return instance;
        }
    }

    // INTERNAL SCHEMA VERSION (Integer)
    // Increment this ONLY when you add new parameters or change the data structure.
    // 1 = v0.1.0
    public const int CURRENT_COMPATIBILITY_VERSION = 1;

    // Event triggered specifically when symbolImage might change
    public static event Action<int> OnSymbolImageChanged;

    // Loads the configuration from the YAML file specified by ConfigPath.Path.
    public Config LoadConfiguration()
    {
        return LoadConfiguration(ConfigPath.Path);
    }

    // NEW: Loads a configuration from a specific file path without altering global ConfigPath state.
    public Config LoadConfiguration(string configFilePath)
    {
        if (!File.Exists(configFilePath))
        {
            Debug.LogError($"ConfigManager: Config file not found at {configFilePath}");
            return null;
        }

        string yamlText = File.ReadAllText(configFilePath);
        var deserializer = new DeserializerBuilder().Build();
        Config config = deserializer.Deserialize<Config>(yamlText);

        // Trigger the event, passing the new symbolImage value
        OnSymbolImageChanged?.Invoke(config.symbolImage);

        return config;
    }

    // Checks if a configuration is compatible
    public bool IsConfigurationCompatible(Config config)
    {
        if (config == null)
        {
            return false;
        }

        // BLOCK FUTURE CONFIGS
        // If the config version is HIGHER than our app version, we can't load it safely.
        if (config.compatibilityVersion > CURRENT_COMPATIBILITY_VERSION)
        {
            Debug.LogError(
                $"Config version ({config.compatibilityVersion}) is newer than App version ({CURRENT_COMPATIBILITY_VERSION}). Cannot load."
            );
            return false;
        }

        // ALLOW CURRENT AND OLDER CONFIGS
        return true;
    }


    // Converts a relative path to a full path based on the provided base path.
    private string ConvertToFullPath(string basePath, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || string.IsNullOrEmpty(relativePath))
        {
            return relativePath;
        }
        return Path.Combine(basePath, "media", relativePath);
    }

    // Adjusts the media paths in the Config object to be full paths relative to the ConfigPath.
    private void AdjustMediaPaths(Config config)
    {
        string basePath = Path.GetDirectoryName(ConfigPath.Path);
        // Adjust media paths logic here if needed
    }

    // Saves the provided Config object to the YAML file specified by ConfigPath.Path.
    public void SaveConfiguration(Config config)
    {
        var serializer = new SerializerBuilder().Build();
        string yaml = serializer.Serialize(config);
        File.WriteAllText(ConfigPath.Path, yaml);
    }

    // Saves the provided YAML content as plain text to the file specified by ConfigPath.Path.
    public void SaveConfigurationPlainText(string yamlContent)
    {
        File.WriteAllText(ConfigPath.Path, yamlContent);
    }
}

[System.Serializable]
public class Config
{
    // COMPATIBILITY VERSION (Integer)
    public int compatibilityVersion = 2;

    // METADATA & SESSION CONFIGURATION
    public string metaDescription =
        "This hypnosis file is a TEMPLATE and demonstrates how to use the Genesis System.\n(Created by \"anthropic/claude-3.7-sonnet\")";
    public int symbolImage = 2;

    // Session playback configuration
    public string sessionMode = "Sequential"; // Sequential or Static
    public float waitSecondsBeforePlaying = 5f;
    public bool repeatSession = false;

    // TTS CONFIGURATION (OpenRouter)
    public string ttsModel = "";
    public string ttsVoice = "";

    // DEPRECATED: No longer consumed by the TTS pipeline. Speed defaults to 1.0
    // on the provider side. Format is always "mp3" to guarantee auto-detected
    // native sample rates. Retained for YAML backward compatibility only.
    public float ttsSpeed = 1.0f;
    public string ttsResponseFormat = "mp3";
    

    // Stage content and Loop settings
    public string stage1TTS;
    public int stage1Loop = 0;

    public string stage2TTS;
    public int stage2Loop = 0;

    public string stage3TTS;
    public int stage3Loop = 0;

    public string stage4TTS;
    public int stage4Loop = 1;

    public string stage5TTS;
    public int stage5Loop = 0;

    // Mantra content
    public string mantraTTS;

    // AUDIO MIXER SETTINGS

    // Master Channel Highpass
    public float masterHighpassCutoff = 30.0f;

    // Audio Levels
    public float speechAttenuationVolume = -10.0f;
    public float sfxAttenuationVolume = -30.0f;
    public float betAttenuationVolume = -12.0f;
    public float noiseAttenuationVolume = -30.0f;
    public float mantraAttenuationVolume = -40.0f;

    // Echo and pitch effects
    public float speechEchoWet = -20.00f;
    public float speechEchoDelay = 230.0f;
    public float mantraEchoWet = -20.00f;
    public float mantraEchoDelay = 230.0f;

    // REVERB PRESET SETTINGS
    public int reverbPresetSpeech = 0;

    public int reverbPresetMantra = 0;

    // SFX SETTINGS
    public int sfxMode = -1;

    // Custom SFX filename for sfxMode = -3
    public string customSfxFilename = "";

    // MANTRA SETTINGS / SUBLIMINAL
    public bool enableSpatialSubliminizer = false;

    // BRAINWAVE ENTRAINMENT SETTINGS
    public int beatMode = 0;
    public float baseFrequency = 144f;
    public float beatFrequency = 9f;
    public float volume = 0.5f;

    // Transition settings
    public string transitionCurveType = "Linear";
    public float transitionTime = 30f;

    // Stage-specific beat frequencies
    public float stage1BeatFrequency = 9f;
    public float stage2BeatFrequency = 8f;
    public float stage3BeatFrequency = 7f;
    public float stage4BeatFrequency = 5f;
    public float stage5BeatFrequency = 8f;

    // Stage-specific carrier frequencies
    public float stage1BaseFrequency = 144f;
    public float stage2BaseFrequency = 128f;
    public float stage3BaseFrequency = 112f;
    public float stage4BaseFrequency = 80f;
    public float stage5BaseFrequency = 128f;

    // VISUAL SETTINGS
    public bool enableStrobe = false;
    public bool hardStrobe = false;
    public bool useSingleImage = true;
    public string hexCodeSingleImage = "#FF0000";
    public string hexCodePrimaryImage = "#FF0000";
    public string hexCodeSecondaryImage = "#0000FF";
    public float strobeOpacity = 1.0f;

    // WHITENOISE SETTINGS
    public int noiseMode = 1;
}
