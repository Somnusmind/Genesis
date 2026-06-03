using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BeatGenerator : MonoBehaviour
{
    public enum TransitionCurveType
    {
        Linear,
        Logarithmic,
        EaseInOut
    }

    public static BeatGenerator instance;

    // External References
    public AudioCoordinator audioCoordinator;
    public AudioSource whitenoiseAudioSource;

    // Frequency Transition Control
    public float transitionTime = 30.0f;
    public bool enableBeatFrequencyTransitions = true;
    public bool enableBaseFrequencyTransitions = false;
    [Tooltip("The type of curve used for transitions between frequencies.")]
    public TransitionCurveType transitionCurveType = TransitionCurveType.Linear;

    // Audio Generation
    [Header("Audio Generation")]
    public int beatMode;
    public volatile float beatFrequency;
    public volatile float baseFrequency = 432f;

    private float phaseLeft, phaseRight, phase;
    private float beatPhase = 0f;
    private int outputSampleRate;

    [SerializeField] private AudioSource audioSource;

    // Volume Control
    [Header("Volume Control")]
    public bool volumeControlEnabled = true;
    public float volume = 0.5f;

    // Loudness Standardization
    // Binaural and Binaural Golden Ratio are the loudness reference because the Unity mixer was balanced against the standard binaural beat.
    // The compensation values below match the current synthesis formulas so the other beat types sit at the same effective loudness as the reference.
    [Header("Loudness Standardization")]
    public float standardizeLoudnessFactorBinaural = 1.0f;
    public float standardizeLoudnessFactorBinauralGoldenRatio = 1.0f;
    public float standardizeLoudnessFactorPanning = 1.11f;
    public float standardizeLoudnessFactorIsochronic = 1.31f;

    // Flickering
    [Header("Flickering")]
    public volatile bool enableStrobe = false;
    [Tooltip("If true, uses GameObject.SetActive On/Off for hard strobe. If false, uses transparency fading.")]
    public bool hardStrobe = false;
    public GameObject primaryImage;
    public string hexCodePrimaryImage = "#FF0000";
    public float strobeOpacity = 1f;
    public GameObject secondaryImage;
    public string hexCodeSecondaryImage = "#FF0000";
    public GameObject singleImage;
    public bool useSingleImage = true;
    public string hexCodeSingleImage = "#FF0000";

    private Coroutine activeFlickerCoroutine = null;
    private volatile float visualPhase = 0f;

    // Startup and UI
    [Header("Startup, Events, and UI")]
    public bool autoStart = false;
    private const int SAMPLE_RATE = 48000;

    public int stageIndexBeat = -1;
    private float panPhase = 0f;

    void Awake()
    {
        AudioSettings.speakerMode = AudioSpeakerMode.Stereo;
        Application.targetFrameRate = 60;
    }

    void Start()
    {
        outputSampleRate = AudioSettings.outputSampleRate;
        InitializePhase();

        if (autoStart)
        {
            audioSource.Play();
        }

        instance = this;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        int length = data.Length;
        float sampleRate = outputSampleRate;

        // If disabled, we use 1.0 (full volume). If enabled, we use the 'volume' variable.
        float effectiveVolume = volumeControlEnabled ? volume : 1.0f;

        int currentBeatMode = beatMode;
        float loudnessStandardizationFactor = GetLoudnessStandardizationFactor(currentBeatMode);

        for (int i = 0; i < length; i += channels)
        {
            float leftSample = 0f;
            float rightSample = 0f;

            switch (currentBeatMode)
            {
                case 0: // Binaural Beat
                    GenerateBinauralBeat(ref leftSample, ref rightSample, sampleRate);
                    break;
                case 1: // Binaural Beat (Golden Ratio)
                    GenerateBinauralBeatGoldenRatio(ref leftSample, ref rightSample, sampleRate);
                    break;
                case 2: // Synchronized Panning
                    GeneratePanningBeat(ref leftSample, ref rightSample, sampleRate);
                    break;
                case 3: // Isochronic Beat
                    GenerateIsochronicBeat(ref leftSample, ref rightSample, sampleRate);
                    break;
                default:
                    leftSample = rightSample = 0f; // Silence for invalid beatMode
                    break;
            }

            leftSample *= loudnessStandardizationFactor;
            rightSample *= loudnessStandardizationFactor;

            // Apply the calculated effective volume
            leftSample = Mathf.Clamp(leftSample * effectiveVolume, -1f, 1f);
            rightSample = Mathf.Clamp(rightSample * effectiveVolume, -1f, 1f);

            if (channels == 2)
            {
                data[i] = leftSample;
                data[i + 1] = rightSample;
            }
            else if (channels == 1)
            {
                data[i] = (leftSample + rightSample) * 0.5f;
            }
        }
    }

    public void LoadConfig()
    {
        Config config = ConfigManager.Instance.LoadConfiguration();

        beatMode = config.beatMode;
        volume = config.volume;

        baseFrequency = config.baseFrequency;
        beatFrequency = config.beatFrequency;

        beatFrequency = config.stage1BeatFrequency;
        enableBeatFrequencyTransitions = true;
        enableBaseFrequencyTransitions = true;
        transitionTime = config.transitionTime;

        enableStrobe = config.enableStrobe;
        hardStrobe = config.hardStrobe;
        useSingleImage = config.useSingleImage;
        hexCodeSingleImage = config.hexCodeSingleImage;
        hexCodePrimaryImage = config.hexCodePrimaryImage;
        hexCodeSecondaryImage = config.hexCodeSecondaryImage;
        strobeOpacity = config.strobeOpacity;

        if (Enum.TryParse(config.transitionCurveType, true, out TransitionCurveType parsedCurveType))
        {
            transitionCurveType = parsedCurveType;
        }
        else
        {
            Debug.LogWarning($"Unknown transition curve type '{config.transitionCurveType}', defaulting to Linear.");
            transitionCurveType = TransitionCurveType.Linear;
        }
    }

    public void StartSequentialMode()
    {
        Debug.Log("Start Sequential Called");

        if (audioSource != null)
        {
            Config config = ConfigManager.Instance.LoadConfiguration();

            if (enableBeatFrequencyTransitions || enableBaseFrequencyTransitions)
            {
                if (enableBeatFrequencyTransitions)
                {
                    beatFrequency = config.stage1BeatFrequency;
                }
                if (enableBaseFrequencyTransitions)
                {
                    baseFrequency = config.stage1BaseFrequency;
                }
            }
            else
            {
                beatFrequency = config.beatFrequency;
                baseFrequency = config.baseFrequency;
            }

            enableStrobe = config.enableStrobe;
            hardStrobe = config.hardStrobe;

            if (enableStrobe)
            {
                StartFlickering();
            }

            audioSource.Play();
        }
        else
        {
            Debug.LogError("BeatGenerator: AudioSource is not initialized.");
        }
    }

    public void StartStaticMode()
    {
        Debug.Log("Start Static Called");

        if (audioSource != null)
        {
            Config config = ConfigManager.Instance.LoadConfiguration();

            beatFrequency = config.beatFrequency;
            baseFrequency = config.baseFrequency;

            enableStrobe = config.enableStrobe;
            hardStrobe = config.hardStrobe;

            if (enableStrobe)
            {
                StartFlickering();
            }

            audioSource.Play();
        }
        else
        {
            Debug.LogError("BeatGenerator: AudioSource is not initialized.");
        }
    }

    public void StopGenerator()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
        }

        StopAllCoroutines();
        StopFlickering();

        if (primaryImage != null) primaryImage.SetActive(false);
        if (secondaryImage != null) secondaryImage.SetActive(false);
        if (singleImage != null) singleImage.SetActive(false);
    }

    public void UpdateStageIndexBeat()
    {
        stageIndexBeat = (stageIndexBeat + 1) % 5;

        StartCoroutine(TransitionToStage(stageIndexBeat));
    }

    public void GoToNextStage()
    {
        stageIndexBeat = (stageIndexBeat + 1) % 5;

        StartCoroutine(TransitionToStage(stageIndexBeat));
    }

    private void InitializePhase()
    {
        UpdateFrequenciesForCurrentStage();
    }

    private void UpdateFrequenciesForCurrentStage()
    {
        Config config = ConfigManager.Instance.LoadConfiguration();
        stageIndexBeat = (stageIndexBeat == -1) ? 0 : stageIndexBeat;

        switch (stageIndexBeat)
        {
            case 0:
                beatFrequency = config.stage1BeatFrequency;
                break;
            case 1:
                beatFrequency = config.stage2BeatFrequency;
                break;
            case 2:
                beatFrequency = config.stage3BeatFrequency;
                break;
            case 3:
                beatFrequency = config.stage4BeatFrequency;
                break;
            case 4:
                beatFrequency = config.stage5BeatFrequency;
                break;
            default:
                Debug.LogWarning($"Unknown stage: {stageIndexBeat}. Using default beat frequency.");
                break;
        }
    }

    private float GetLoudnessStandardizationFactor(int currentBeatMode)
    {
        switch (currentBeatMode)
        {
            case 0:
                return standardizeLoudnessFactorBinaural;
            case 1:
                return standardizeLoudnessFactorBinauralGoldenRatio;
            case 2:
                return standardizeLoudnessFactorPanning;
            case 3:
                return standardizeLoudnessFactorIsochronic;
            default:
                return 1.0f;
        }
    }

    private void GenerateBinauralBeat(ref float leftSample, ref float rightSample, float sampleRate)
    {
        float frequencyLeft = baseFrequency - beatFrequency / 2f;
        float frequencyRight = baseFrequency + beatFrequency / 2f;

        phaseLeft += 2f * Mathf.PI * frequencyLeft / sampleRate;
        phaseRight += 2f * Mathf.PI * frequencyRight / sampleRate;

        phaseLeft %= 2f * Mathf.PI;
        phaseRight %= 2f * Mathf.PI;

        leftSample = Mathf.Sin(phaseLeft);
        rightSample = Mathf.Sin(phaseRight);
    }

    private void GenerateBinauralBeatGoldenRatio(ref float leftSample, ref float rightSample, float sampleRate)
    {
        const float goldenRatio = 1.618f;

        float leftOffset = (beatFrequency / (1f + goldenRatio));
        float rightOffset = beatFrequency - leftOffset;

        float frequencyLeft = baseFrequency - leftOffset;
        float frequencyRight = baseFrequency + rightOffset;

        phaseLeft += 2f * Mathf.PI * frequencyLeft / sampleRate;
        phaseRight += 2f * Mathf.PI * frequencyRight / sampleRate;

        phaseLeft %= 2f * Mathf.PI;
        phaseRight %= 2f * Mathf.PI;

        leftSample = Mathf.Sin(phaseLeft);
        rightSample = Mathf.Sin(phaseRight);
    }

    private void GeneratePanningBeat(ref float leftSample, ref float rightSample, float sampleRate)
    {
        float panStrength = 0.8f;

        panPhase += 2f * Mathf.PI * beatFrequency / sampleRate;
        panPhase %= 2f * Mathf.PI;

        float panPosition = Mathf.Sin(panPhase) * Mathf.Clamp01(panStrength);

        float leftPanFactor = Mathf.Clamp01(1 - panPosition);
        float rightPanFactor = Mathf.Clamp01(1 + panPosition);

        float frequency = baseFrequency;
        phase += frequency * 2f * Mathf.PI / sampleRate;
        phase %= 2f * Mathf.PI;

        float tone = Mathf.Sin(phase);

        leftSample = tone * leftPanFactor;
        rightSample = tone * rightPanFactor;
    }

    private void GenerateIsochronicBeat(ref float leftSample, ref float rightSample, float sampleRate)
    {
        beatPhase += beatFrequency * 2f * Mathf.PI / sampleRate;
        beatPhase %= 2f * Mathf.PI;

        float modulationFactor = Mathf.Clamp01((Mathf.Sin(beatPhase) + 1f) / 2f);
        modulationFactor = Mathf.SmoothStep(0f, 1f, modulationFactor);

        phase += baseFrequency * 2f * Mathf.PI / sampleRate;
        phase %= 2f * Mathf.PI;

        float tone = Mathf.Sin(phase);
        float modulatedTone = tone * modulationFactor;

        leftSample = rightSample = modulatedTone;
    }

    private IEnumerator TransitionToStage(int newStageIndex)
    {
        float elapsedTime = 0f;
        float transitionDuration = transitionTime;

        float oldBeatFrequency = beatFrequency;
        float oldBaseFrequency = baseFrequency;

        var (targetBeatFreq, targetBaseFreq) = GetTargetFrequenciesForStage(newStageIndex);

        while (elapsedTime < transitionDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / transitionDuration);

            float curveT = ApplyTransitionCurve(t);

            if (enableBeatFrequencyTransitions)
            {
                beatFrequency = Mathf.Lerp(oldBeatFrequency, targetBeatFreq, curveT);
            }

            if (enableBaseFrequencyTransitions)
            {
                baseFrequency = Mathf.Lerp(oldBaseFrequency, targetBaseFreq, curveT);
            }

            yield return null;
        }

        if (enableBeatFrequencyTransitions)
        {
            beatFrequency = targetBeatFreq;
        }

        if (enableBaseFrequencyTransitions)
        {
            baseFrequency = targetBaseFreq;
        }

        if (beatMode != 1)
        {
            visualPhase = beatPhase;
            if (enableStrobe && activeFlickerCoroutine != null)
            {
                StopFlickering();
                StartFlickering();
            }
        }

        if (audioCoordinator != null)
        {
            audioCoordinator.OnPhaseChange(newStageIndex);
        }
    }

    private float ApplyTransitionCurve(float t)
    {
        switch (transitionCurveType)
        {
            case TransitionCurveType.Linear:
                return t;
            case TransitionCurveType.Logarithmic:
                return Mathf.Log10(1 + 9 * t);
            case TransitionCurveType.EaseInOut:
                return EaseInOutSmoothStep(t);
            default:
                Debug.LogWarning($"Unsupported transition curve: {transitionCurveType}. Defaulting to Linear.");
                return t;
        }
    }

    private (float beatFreq, float baseFreq) GetTargetFrequenciesForStage(int stageIndex)
    {
        Config config = ConfigManager.Instance.LoadConfiguration();
        switch (stageIndex)
        {
            case 0:
                return (config.stage1BeatFrequency, config.stage1BaseFrequency);
            case 1:
                return (config.stage2BeatFrequency, config.stage2BaseFrequency);
            case 2:
                return (config.stage3BeatFrequency, config.stage3BaseFrequency);
            case 3:
                return (config.stage4BeatFrequency, config.stage4BaseFrequency);
            case 4:
                return (config.stage5BeatFrequency, config.stage5BaseFrequency);
            default:
                Debug.LogWarning($"Unknown stage: {stageIndex}. Using default frequencies.");
                return (beatFrequency, baseFrequency);
        }
    }

    private void StartFlickering()
    {
        if (activeFlickerCoroutine != null)
        {
            StopCoroutine(activeFlickerCoroutine);
        }

        InitializeFlickerImages();
        activeFlickerCoroutine = StartCoroutine(ContinuousFlickerProcess());
    }

    private void StopFlickering()
    {
        if (activeFlickerCoroutine != null)
        {
            StopCoroutine(activeFlickerCoroutine);
            activeFlickerCoroutine = null;
        }

        if (hardStrobe)
        {
            // Ensure objects are disabled for hard strobe off state
            if (singleImage != null) singleImage.SetActive(false);
            if (primaryImage != null) primaryImage.SetActive(false);
            if (secondaryImage != null) secondaryImage.SetActive(false);
        }
        else
        {
            // Ensure transparency is 0 for smooth strobe off state
            if (singleImage != null) SetImageAlpha(singleImage, 0);
            if (primaryImage != null) SetImageAlpha(primaryImage, 0);
            if (secondaryImage != null) SetImageAlpha(secondaryImage, 0);
        }
    }

    private void InitializeFlickerImages()
    {
        if (useSingleImage && singleImage != null)
        {
            Color singleColor = HexToColor(hexCodeSingleImage, 0);
            ApplyColorToImage(singleImage, singleColor);

            // For hard strobe, we start with it disabled, then let the coroutine toggle it.
            // For smooth strobe, we enable it but set alpha to 0.
            if (!hardStrobe) singleImage.SetActive(true);

            if (primaryImage != null) primaryImage.SetActive(false);
            if (secondaryImage != null) secondaryImage.SetActive(false);
        }
        else
        {
            if (singleImage != null) singleImage.SetActive(false);

            if (primaryImage != null)
            {
                Color primaryColor = HexToColor(hexCodePrimaryImage, 0);
                ApplyColorToImage(primaryImage, primaryColor);

                if (!hardStrobe) primaryImage.SetActive(true);
            }

            if (secondaryImage != null)
            {
                Color secondaryColor = HexToColor(hexCodeSecondaryImage, 0);
                ApplyColorToImage(secondaryImage, secondaryColor);

                if (!hardStrobe) secondaryImage.SetActive(true);
            }
        }
    }

    private IEnumerator ContinuousFlickerProcess()
    {
        visualPhase = beatPhase;

        while (enableStrobe)
        {
            float deltaTime = Time.deltaTime;
            float flickerFrequency = beatFrequency;

            visualPhase += 2f * Mathf.PI * flickerFrequency * deltaTime;
            visualPhase %= 2f * Mathf.PI;

            float modulation = (Mathf.Sin(visualPhase) + 1f) * 0.5f;

            if (hardStrobe)
            {
                // Hard Strobe Logic: Toggle GameObject Active/Inactive
                // Using > 0.5f threshold for a 50% duty cycle square wave
                bool shouldBeActive = modulation > 0.5f;

                if (useSingleImage)
                {
                    if (singleImage != null)
                    {
                        if (singleImage.activeSelf != shouldBeActive)
                            singleImage.SetActive(shouldBeActive);
                    }
                }
                else
                {
                    if (primaryImage != null && secondaryImage != null)
                    {
                        // Primary on when modulation > 0.5, Secondary on when < 0.5
                        bool primaryActive = modulation > 0.5f;
                        bool secondaryActive = modulation < 0.5f;

                        if (primaryImage.activeSelf != primaryActive)
                            primaryImage.SetActive(primaryActive);

                        if (secondaryImage.activeSelf != secondaryActive)
                            secondaryImage.SetActive(secondaryActive);
                    }
                }
            }
            else
            {
                // Smooth Strobe Logic: Transparency Fading
                if (useSingleImage)
                {
                    if (singleImage != null)
                    {
                        float opacity = modulation * strobeOpacity;
                        SetImageAlpha(singleImage, opacity);
                    }
                }
                else
                {
                    if (primaryImage != null && secondaryImage != null)
                    {
                        float primaryOpacity = Mathf.SmoothStep(0, strobeOpacity, modulation);
                        float secondaryOpacity = Mathf.SmoothStep(0, strobeOpacity, 1f - modulation);

                        SetImageAlpha(primaryImage, primaryOpacity);
                        SetImageAlpha(secondaryImage, secondaryOpacity);
                    }
                }
            }

            yield return null;
        }

        // Cleanup when loop ends (enableStrobe becomes false)
        if (hardStrobe)
        {
            if (singleImage != null) singleImage.SetActive(false);
            if (primaryImage != null) primaryImage.SetActive(false);
            if (secondaryImage != null) secondaryImage.SetActive(false);
        }
        else
        {
            if (singleImage != null) SetImageAlpha(singleImage, 0);
            if (primaryImage != null) SetImageAlpha(primaryImage, 0);
            if (secondaryImage != null) SetImageAlpha(secondaryImage, 0);
        }

        activeFlickerCoroutine = null;
    }

    private float EaseInOutSmoothStep(float t)
    {
        return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
    }

    private Color HexToColor(string hex, float opacity)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
        {
            color.a = opacity;
            return color;
        }
        return Color.white;
    }

    private void SetImageAlpha(GameObject imageObj, float alpha)
    {
        if (imageObj == null) return;

        UnityEngine.UI.Image uiImage = imageObj.GetComponent<UnityEngine.UI.Image>();
        if (uiImage != null)
        {
            Color c = uiImage.color;
            c.a = alpha;
            uiImage.color = c;
            return;
        }

        SpriteRenderer spriteRenderer = imageObj.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = alpha;
            spriteRenderer.color = c;
            return;
        }

        CanvasGroup canvasGroup = imageObj.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
        }
    }

    private void ApplyColorToImage(GameObject imageObj, Color color)
    {
        if (imageObj == null) return;

        UnityEngine.UI.Image uiImage = imageObj.GetComponent<UnityEngine.UI.Image>();
        if (uiImage != null)
        {
            float currentAlpha = uiImage.color.a;
            color.a = currentAlpha;
            uiImage.color = color;
            return;
        }

        SpriteRenderer spriteRenderer = imageObj.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            float currentAlpha = spriteRenderer.color.a;
            color.a = currentAlpha;
            spriteRenderer.color = color;
        }
    }
}