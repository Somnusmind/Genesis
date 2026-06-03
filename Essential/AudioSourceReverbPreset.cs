using UnityEngine;

public class AudioSourceReverbPreset : MonoBehaviour
{
    public const int BuiltInPresetMin = 0;
    public const int BuiltInPresetMax = 26;
    public const int PresetHypnosis = 27;
    public const int PresetMeditation = 28;
    public const int PresetMantra = 29;
    public const int PresetSubliminal = 30;
    public const int SupportedPresetMax = 30;

    private struct ReverbValues
    {
        public readonly float dryLevel;
        public readonly float room;
        public readonly float roomHF;
        public readonly float roomLF;
        public readonly float decayTime;
        public readonly float decayHFRatio;
        public readonly float reflectionsLevel;
        public readonly float reflectionsDelay;
        public readonly float reverbLevel;
        public readonly float reverbDelay;
        public readonly float hfReference;
        public readonly float lfReference;
        public readonly float diffusion;
        public readonly float density;

        public ReverbValues(
            float dryLevel,
            float room,
            float roomHF,
            float roomLF,
            float decayTime,
            float decayHFRatio,
            float reflectionsLevel,
            float reflectionsDelay,
            float reverbLevel,
            float reverbDelay,
            float hfReference,
            float lfReference,
            float diffusion,
            float density)
        {
            this.dryLevel = dryLevel;
            this.room = room;
            this.roomHF = roomHF;
            this.roomLF = roomLF;
            this.decayTime = decayTime;
            this.decayHFRatio = decayHFRatio;
            this.reflectionsLevel = reflectionsLevel;
            this.reflectionsDelay = reflectionsDelay;
            this.reverbLevel = reverbLevel;
            this.reverbDelay = reverbDelay;
            this.hfReference = hfReference;
            this.lfReference = lfReference;
            this.diffusion = diffusion;
            this.density = density;
        }
    }

    [Header("References")]
    public AudioSource audioSourceSpeech;
    public AudioReverbFilter audioReverbFilterSpeech;
    public AudioReverbFilter audioReverbFilterMantra;

    [HideInInspector] public int currentSpeechPreset = (int)AudioReverbPreset.Off;
    [HideInInspector] public int currentMantraPreset = (int)AudioReverbPreset.Off;

    // 27 = OPTIMIZED_Hypnosis
    // Dark, intimate trance chamber.
    // Smooth late bloom with restrained early reflections, so the voice stays close
    // while the space feels inward, dreamy, and immersive.
    private static readonly ReverbValues HypnosisPresetValues = new ReverbValues(
        0f,        // dryLevel
        -1300f,    // room
        -1700f,    // roomHF
        -1050f,    // roomLF
        2.20f,     // decayTime
        0.56f,     // decayHFRatio
        -2100f,    // reflectionsLevel
        0.014f,    // reflectionsDelay
        -600f,     // reverbLevel
        0.021f,    // reverbDelay
        4600f,     // hfReference
        250f,      // lfReference
        99f,       // diffusion
        94f        // density
    );

    // 28 = OPTIMIZED_Meditation
    // Open, calm, and cleaner than hypnosis.
    // Spacious enough to avoid dryness, but deliberately clearer, more natural,
    // and less dreamlike so awareness stays forward.
    private static readonly ReverbValues MeditationPresetValues = new ReverbValues(
        0f,        // dryLevel
        -1450f,    // room
        -700f,     // roomHF
        -1150f,    // roomLF
        1.40f,     // decayTime
        0.86f,     // decayHFRatio
        -2050f,    // reflectionsLevel
        0.008f,    // reflectionsDelay
        -950f,     // reverbLevel
        0.013f,    // reverbDelay
        7000f,     // hfReference
        250f,      // lfReference
        80f,       // diffusion
        60f        // density
    );

    // 29 = OPTIMIZED_Mantra
    // Short, centered, warm chamber.
    // Designed to keep affirmations and repeated phrases forward, confident,
    // smooth, and articulate without long-tail buildup.
    private static readonly ReverbValues MantraPresetValues = new ReverbValues(
        0f,        // dryLevel
        -800f,     // room
        -900f,     // roomHF
        -600f,     // roomLF
        0.78f,     // decayTime
        0.64f,     // decayHFRatio
        -550f,     // reflectionsLevel
        0.0035f,   // reflectionsDelay
        -1700f,    // reverbLevel
        0.006f,    // reverbDelay
        5200f,     // hfReference
        250f,      // lfReference
        82f,       // diffusion
        86f        // density
    );

    // 30 = OPTIMIZED_Subliminal
    // Near-dry micro-room.
    // Intelligibility-first and almost transparent, with only trace smoothing
    // so the signal does not feel unnaturally dead if auditioned in isolation.
    private static readonly ReverbValues SubliminalPresetValues = new ReverbValues(
        0f,        // dryLevel
        -3800f,    // room
        -2000f,    // roomHF
        -2600f,    // roomLF
        0.18f,     // decayTime
        0.60f,     // decayHFRatio
        -5000f,    // reflectionsLevel
        0.001f,    // reflectionsDelay
        -5600f,    // reverbLevel
        0.0015f,   // reverbDelay
        6800f,     // hfReference
        250f,      // lfReference
        55f,       // diffusion
        45f        // density
    );

    public void LoadConfig()
    {
        if (ConfigManager.Instance == null)
        {
            Debug.LogWarning("AudioSourceReverbPreset: ConfigManager.Instance is null.");
            return;
        }

        Config config = ConfigManager.Instance.LoadConfiguration();
        if (config == null)
        {
            Debug.LogWarning("AudioSourceReverbPreset: Config is null.");
            return;
        }

        currentSpeechPreset = SanitizePreset(config.reverbPresetSpeech);
        currentMantraPreset = SanitizePreset(config.reverbPresetMantra);

        ApplyPresetToFilter(currentSpeechPreset, audioReverbFilterSpeech);
        ApplyPresetToFilter(currentMantraPreset, audioReverbFilterMantra);
    }

    public void SetSpeechPreset(int presetIndex)
    {
        currentSpeechPreset = SanitizePreset(presetIndex);
        ApplyPresetToFilter(currentSpeechPreset, audioReverbFilterSpeech);
    }

    public void SetMantraPreset(int presetIndex)
    {
        currentMantraPreset = SanitizePreset(presetIndex);
        ApplyPresetToFilter(currentMantraPreset, audioReverbFilterMantra);
    }

    private void ApplyPresetToFilter(int presetIndex, AudioReverbFilter filter)
    {
        if (filter == null)
            return;

        filter.enabled = true;

        switch (presetIndex)
        {
            case PresetHypnosis:
                ApplyCustomReverbSettings(filter, HypnosisPresetValues);
                break;

            case PresetMeditation:
                ApplyCustomReverbSettings(filter, MeditationPresetValues);
                break;

            case PresetMantra:
                ApplyCustomReverbSettings(filter, MantraPresetValues);
                break;

            case PresetSubliminal:
                ApplyCustomReverbSettings(filter, SubliminalPresetValues);
                break;

            default:
                if (IsBuiltInReverbPreset(presetIndex))
                {
                    filter.reverbPreset = (AudioReverbPreset)presetIndex;
                }
                else
                {
                    Debug.LogWarning($"Invalid reverb preset index: {presetIndex}. Using Off.");
                    filter.reverbPreset = AudioReverbPreset.Off;
                }
                break;
        }
    }

    private void ApplyCustomReverbSettings(AudioReverbFilter filter, ReverbValues settings)
    {
        filter.reverbPreset = AudioReverbPreset.User;

        filter.dryLevel = settings.dryLevel;
        filter.room = settings.room;
        filter.roomHF = settings.roomHF;
        filter.roomLF = settings.roomLF;
        filter.decayTime = settings.decayTime;
        filter.decayHFRatio = settings.decayHFRatio;
        filter.reflectionsLevel = settings.reflectionsLevel;
        filter.reflectionsDelay = settings.reflectionsDelay;
        filter.reverbLevel = settings.reverbLevel;
        filter.reverbDelay = settings.reverbDelay;
        filter.hfReference = settings.hfReference;
        filter.lfReference = settings.lfReference;
        filter.diffusion = settings.diffusion;
        filter.density = settings.density;
    }

    private bool IsBuiltInReverbPreset(int presetIndex)
    {
        return presetIndex >= BuiltInPresetMin && presetIndex <= BuiltInPresetMax;
    }

    public bool IsSupportedPreset(int presetIndex)
    {
        return presetIndex >= BuiltInPresetMin && presetIndex <= SupportedPresetMax;
    }

    private int SanitizePreset(int presetIndex)
    {
        return IsSupportedPreset(presetIndex) ? presetIndex : (int)AudioReverbPreset.Off;
    }

    public string GetPresetDisplayName(int presetIndex)
    {
        switch (presetIndex)
        {
            case PresetHypnosis: return "OPTIMIZED_Hypnosis";
            case PresetMeditation: return "OPTIMIZED_Meditation";
            case PresetMantra: return "OPTIMIZED_Mantra";
            case PresetSubliminal: return "OPTIMIZED_Subliminal";
            default:
                return IsBuiltInReverbPreset(presetIndex)
                    ? ((AudioReverbPreset)presetIndex).ToString()
                    : "Unknown";
        }
    }
}