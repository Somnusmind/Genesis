using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evo.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
#endif

public class AudioCoordinator : MonoBehaviour
{
    public enum RecordingType
    {
        None,
        AudioWAV,
        VideoMP4,
    }

    public static AudioCoordinator instance;

    [SerializeField]
    private AudioMixer mainMixer;

    [SerializeField]
    public AudioSource speechAudioSource;

    [SerializeField]
    public AudioSource sfxAudioSource;

    [SerializeField]
    private AudioSource mantraAudioSource;

    [SerializeField]
    private AudioSource noiseAudioSource;

    [SerializeField]
    private AudioClip stage1Clip;

    [SerializeField]
    private AudioClip stage2Clip;

    [SerializeField]
    private AudioClip stage3Clip;

    [SerializeField]
    private AudioClip stage4Clip;

    [SerializeField]
    private AudioClip stage5Clip;

    [Header("Global SFX")]
    [SerializeField]
    private AudioClip sfxClip1;

    [Header("Preloaded SFX")]
    [SerializeField]
    private List<AudioClip> preloadedSfxClips = new List<AudioClip>();

    [Header("Custom SFX")]
    [SerializeField]
    private string customSfxFilename = "";

    [Header("References")]
    [SerializeField]
    private BeatGenerator beatGenerator;

    [SerializeField]
    private SceneManager_SessionWizard sceneManager_SessionWizard;

    public ControlledFade controlledFade;
    public float fadeInTimeMasterMixer;
    public float fadeOutTimeMasterMixer;

    [Header("Quick Fade Settings")]
    [SerializeField]
    private float quickFadeOutTimeMasterMixer = 1.0f;

    [Header("Session Settings")]
    public bool isPreparationPhase = false;
    public bool useWaitSecondForPreparation = true;
    public float waitSecondsBeforePlaying = 5f;
    public GameObject blockoutPanel;
    public Countdown countdownUI;
    public GameObject countdownAdditionals;

    [Header("Audio/Video Visualization")]
    [SerializeField]
    private GameObject audioVideoVisualization;

    [SerializeField]
    private VideoPlayer videoPlayer;

    [Header("Stage Control")]
    public int stageIndex = 0;
    public float fadeDurationAudioSource = 2.0f;

    [Header("Recording")]
    public RecordingType recordingType = RecordingType.None;

    [SerializeField]
    private int mantraIterations = 0;

    [Header("Transition Settings")]
    [Tooltip("Duration in seconds to fade out menu audio before session starts.")]
    public float menuFadeOutDuration = 0.5f;

    [Header("Input Control Settings")]
    [SerializeField]
    private GameObject liveMixerUI;

    [SerializeField]
    private float maxTimeBetweenPresses = 1.0f;

    [SerializeField]
    private int requiredPresses = 5;

    [Tooltip("If true, allows toggling the Live Mixer UI during the Preparation Phase countdown.")]
    [SerializeField]
    private bool allowMixerDuringPreparation = false;

    [Header("Events")]
    [SerializeField]
    public UnityEvent onRecordingStopped;

    private AudioClip loadedCustomSfxClip = null;

    private bool isSessionActive = true;
    private bool isClipPlaying = false;
    public bool repeatSession = false;
    private bool skipPreparationWait = false;
    private bool isStoppingSession = false;
    private float targetSpeechVolume;

    private int currentMantraIteration = 0;
    private bool isCountingMantraIterations = false;
    private float mantraTimeElapsed = 0f;
    private bool mantraStarted = false;

    private int[] targetStageLoops = new int[5];
    private int[] currentStageLoops = new int[5];
    private bool stageChanged;

    private Keyboard keyboard;
    private int escapePressCount = 0;
    private float lastEscapePressTime = 0f;
    private int spacePressCount = 0;
    private float lastSpacePressTime = 0f;

    private GlobalErrorHandling globalErrorHandling;
    private bool videoIsPreparing = false;

#if UNITY_EDITOR
    private RecorderController recorderController;
    private bool isRecording = false;
    private bool canStartRecording = true;
    public bool triggerStopEvent = false;
#endif

    void Awake()
    {
        instance = this;

        globalErrorHandling = FindAnyObjectByType<GlobalErrorHandling>();

        if (targetStageLoops == null || targetStageLoops.Length != 5)
            targetStageLoops = new int[5];

        if (currentStageLoops == null || currentStageLoops.Length != 5)
            currentStageLoops = new int[5];

        GetComponent<AudioCoordinator>().enabled = true;

        // Wire the Audio/Video Visualization CanvasGroup into ControlledFade so it fades with the master
        if (controlledFade != null && audioVideoVisualization != null)
        {
            CanvasGroup cg = audioVideoVisualization.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = audioVideoVisualization.GetComponentInChildren<CanvasGroup>(true);

            if (cg != null)
                controlledFade.audioVideoCanvasGroup = cg;
            else
                Debug.LogWarning(
                    "AudioCoordinator: AudioVideoVisualization is missing a CanvasGroup. It will not fade with the master."
                );
        }
    }

    void Start()
    {
        keyboard = Keyboard.current;

        if (keyboard == null)
        {
            Debug.LogWarning("No keyboard detected. Keystroke listeners disabled.");
        }

        if (speechAudioSource == null || sfxAudioSource == null || mantraAudioSource == null)
        {
            Debug.LogError(
                "One or more AudioSources are not assigned. Please assign AudioSources in the Inspector."
            );
            globalErrorHandling?.CallGlobalErrorWithConfirmation(
                "One or more AudioSources are not assigned. Please assign AudioSources in the Inspector.",
                null
            );
            return;
        }

        for (int i = 0; i < 5; i++)
        {
            targetStageLoops[i] = 0;
            currentStageLoops[i] = 0;
        }

#if UNITY_EDITOR
        if (recordingType != RecordingType.None)
        {
            SetupRecorder();
            canStartRecording = true;
        }
#endif
    }

    void Update()
    {
        if (
            isCountingMantraIterations
            && mantraAudioSource != null
            && mantraAudioSource.isPlaying
            && mantraIterations > 0
        )
        {
            mantraTimeElapsed += Time.deltaTime;

            if (mantraTimeElapsed >= mantraAudioSource.clip.length)
            {
                currentMantraIteration++;
                mantraTimeElapsed = 0f;

                Debug.Log(
                    $"Mantra iteration {currentMantraIteration} of {mantraIterations} completed"
                );

                if (currentMantraIteration >= mantraIterations)
                {
                    Debug.Log("All mantra iterations completed, ending session");
                    EndMantraSession();
                    return;
                }
            }
        }

        HandleKeystrokeInput();
    }

#if UNITY_EDITOR
    void OnDestroy()
    {
        if (recordingType != RecordingType.None && isRecording)
        {
            StopRecording();
        }
    }
#endif

    public void StartPreparationPhase()
    {
        isPreparationPhase = true;
        Debug.Log("AudioCoordinator.StartPreparationPhase called");
        skipPreparationWait = false;

        if (countdownUI != null)
            countdownUI.gameObject.SetActive(true);

        if (countdownAdditionals != null)
            countdownAdditionals.SetActive(true);

        StartCoroutine(TransitionToPreparation());
    }

    public void SkipWaitingTime()
    {
        skipPreparationWait = true;
    }

    public void OnPhaseChange(int phaseIndex)
    {
        if (!isClipPlaying)
        {
            stageIndex = phaseIndex;
            StartPlaying();
        }
    }

    public void RestartSession()
    {
        Debug.Log("Restarting the session from the beginning.");
        stageIndex = 0;

        if (currentStageLoops != null)
        {
            for (int i = 0; i < 5; i++)
                currentStageLoops[i] = 0;
        }

        isSessionActive = true;
        isStoppingSession = false;

        isCountingMantraIterations = false;
        currentMantraIteration = 0;
        mantraTimeElapsed = 0f;
        mantraStarted = false;

        speechAudioSource.Stop();
        sfxAudioSource.Stop();
        mantraAudioSource.Stop();
        noiseAudioSource.Stop();

        speechAudioSource.volume = 0f;
        sfxAudioSource.volume = 0f;
        mantraAudioSource.volume = 0f;
        noiseAudioSource.volume = 0f;

        if (controlledFade != null)
            controlledFade.SetVolume(0f);

#if UNITY_EDITOR
        canStartRecording = true;
#endif

        ManageAudioVideoVisualization(true);
        StartPlaying();
    }

    public void StopSession()
    {
        if (isStoppingSession)
            return;

        isStoppingSession = true;
        isSessionActive = false;

        Debug.Log("Session ended by user - Initiating quick master fade out before reload.");
        StartCoroutine(StopSessionCoroutine());
    }

    public void ReloadScene()
    {
        Debug.Log("Returning to the configuration or menu.");
        sceneManager_SessionWizard.ReloadScene();
    }

    public void LoadConfig()
    {
        ConfigManager manager = new ConfigManager();
        Config config = manager.LoadConfiguration();

        targetSpeechVolume = 1.0f;

        if (targetStageLoops == null || targetStageLoops.Length != 5)
            targetStageLoops = new int[5];

        targetStageLoops[0] = config.stage1Loop;
        targetStageLoops[1] = config.stage2Loop;
        targetStageLoops[2] = config.stage3Loop;
        targetStageLoops[3] = config.stage4Loop;
        targetStageLoops[4] = config.stage5Loop;

        waitSecondsBeforePlaying = config.waitSecondsBeforePlaying;
        repeatSession = config.repeatSession;

        int sfxMode = config.sfxMode;
        customSfxFilename = config.customSfxFilename;
        Debug.Log($"Loaded sfxMode from config: {sfxMode}");

        sfxClip1 = null;
        loadedCustomSfxClip = null;

        if (sfxMode == -1)
        {
            Debug.Log("No SFX will be used (sfxMode = -1)");
        }
        else if (sfxMode >= 0)
        {
            Debug.Log($"Using preloaded SFX clip at index {sfxMode}");
            if (sfxMode < preloadedSfxClips.Count && preloadedSfxClips[sfxMode] != null)
            {
                sfxClip1 = preloadedSfxClips[sfxMode];
                Debug.Log(
                    $"Assigned preloaded SFX clip '{sfxClip1.name}' to sfxClip1 (Global SFX)"
                );
            }
            else
            {
                Debug.LogWarning($"sfxMode {sfxMode} out of range or null. No SFX will be played.");
            }
        }
        else if (sfxMode == -3)
        {
            if (!string.IsNullOrEmpty(customSfxFilename))
            {
                Debug.Log($"Custom SFX requested: {customSfxFilename}");
                StartCoroutine(LoadCustomSfxClipAsync(customSfxFilename));
            }
            else
            {
                Debug.LogWarning(
                    "sfxMode is -3 but customSfxFilename is empty. No SFX will be played."
                );
            }
        }
    }

    public void SetUseWaitSecondForPreparation(bool value)
    {
        useWaitSecondForPreparation = value;
    }

    public void SetBeatGeneratorStageIndex(int index)
    {
        if (beatGenerator != null)
            beatGenerator.stageIndexBeat = index;
    }

    public void SetClip(int stageIndex, AudioClip clip)
    {
        if (clip == null)
            return;

        if (clip.name == "Silent" || clip.name.StartsWith("Silence_"))
        {
            // Skip normalization for generated silence clips
        }
        else
        {
            clip = AudioNormalizer.NormalizeClip(clip, -18f);
        }

        switch (stageIndex)
        {
            case 0:
                stage1Clip = clip;
                break;
            case 1:
                stage2Clip = clip;
                break;
            case 2:
                stage3Clip = clip;
                break;
            case 3:
                stage4Clip = clip;
                break;
            case 4:
                stage5Clip = clip;
                break;
            default:
                Debug.LogError("StageIndex out of range: " + stageIndex);
                break;
        }
    }

    public void SetMantraClip(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("SetMantraClip: AudioClip passed is null.");
            return;
        }

        if (clip.name == "Silent" || clip.name.StartsWith("Silence_"))
        {
            // Skip normalization for generated silence clips
        }
        else
        {
            clip = AudioNormalizer.NormalizeClip(clip, -18f);
        }

        mantraAudioSource.clip = clip;
        mantraAudioSource.loop = true;
        mantraAudioSource.volume = 1.0f;
    }

    public void InitiateMasterFadeIn()
    {
        if (controlledFade != null)
        {
            controlledFade.SetVolume(-80f);
            controlledFade.FadeIn(fadeInTimeMasterMixer);
        }
        else
        {
            Debug.LogWarning("ControlledFade is not assigned. Cannot initiate master fade in.");
        }
    }

    public void InitiateMasterFadeOut()
    {
        if (controlledFade != null)
        {
            controlledFade.FadeOut(fadeOutTimeMasterMixer);
        }
        else
        {
            Debug.LogWarning("ControlledFade is not assigned. Cannot initiate master fade out.");
        }
    }

    public void InitiateMasterFadeOutQuick()
    {
        if (controlledFade != null)
        {
            controlledFade.FadeOut(quickFadeOutTimeMasterMixer);
        }
        else
        {
            Debug.LogWarning(
                "ControlledFade is not assigned. Cannot initiate quick master fade out."
            );
        }
    }

    public bool IsClipAssigned(AudioClip clip) => clip != null;

    public bool AreAllClipsAssigned()
    {
        return IsClipAssigned(stage1Clip)
            && IsClipAssigned(stage2Clip)
            && IsClipAssigned(stage3Clip)
            && IsClipAssigned(stage4Clip)
            && IsClipAssigned(stage5Clip);
    }

    public bool AreAllSfxClipsAssigned()
    {
        return IsClipAssigned(sfxClip1);
    }

    public string GetFormattedSessionLength()
    {
        if (!AreAllClipsAssigned())
        {
            Debug.LogError(
                "Not all audio clips are assigned. Session length cannot be calculated."
            );
            return "Calculation Error";
        }

        Config config = ConfigManager.Instance.LoadConfiguration();
        string sessionMode = config.sessionMode;

        Debug.Log($"Calculating session length for mode: {sessionMode}");

        if (sessionMode == "Static")
        {
            Debug.Log("Static mode detected - session length will be displayed as NaN (infinite)");
            return $"NaN (Static)";
        }

        float totalLength = 0f;
        AudioClip[] stageClips = { stage1Clip, stage2Clip, stage3Clip, stage4Clip, stage5Clip };

        int[] loops = new int[5];
        loops[0] = config.stage1Loop;
        loops[1] = config.stage2Loop;
        loops[2] = config.stage3Loop;
        loops[3] = config.stage4Loop;
        loops[4] = config.stage5Loop;

        for (int i = 0; i < 5; i++)
        {
            if (stageClips[i] != null)
            {
                int totalPlays = loops[i] + 1;
                float stageTotal = stageClips[i].length * totalPlays;
                totalLength += stageTotal;

                Debug.Log(
                    $"Stage {i + 1} length: {stageClips[i].length:F2}s x {totalPlays} plays = {stageTotal:F2}s"
                );
            }
        }

        Debug.Log($"Total session length calculated: {totalLength:F2} seconds");

        TimeSpan timeSpan = TimeSpan.FromSeconds(totalLength);
        string formattedLength;

        if (timeSpan.Hours > 0)
        {
            formattedLength = string.Format(
                "{0:D2}:{1:D2}:{2:D2}",
                timeSpan.Hours,
                timeSpan.Minutes,
                timeSpan.Seconds
            );
            Debug.Log($"Session exceeds one hour. Using HH:MM:SS format: {formattedLength}");
        }
        else
        {
            formattedLength = string.Format("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);
            Debug.Log($"Session is less than one hour. Using MM:SS format: {formattedLength}");
        }

        Debug.Log($"Formatted session length: {formattedLength}");

        return formattedLength;
    }

    private void HandleKeystrokeInput()
    {
        if (keyboard == null)
            return;

        // Escape key logic
        if (isPreparationPhase || isSessionActive)
        {
            if (keyboard[Key.Escape].wasPressedThisFrame)
            {
                if (
                    Time.time - lastEscapePressTime <= maxTimeBetweenPresses
                    || escapePressCount == 0
                )
                    escapePressCount++;
                else
                    escapePressCount = 1;

                lastEscapePressTime = Time.time;

                if (escapePressCount >= requiredPresses)
                {
                    Debug.Log("Escape pressed 5 times. Ending session.");
                    StopSession();
                    escapePressCount = 0;
                }
            }
        }

        if (escapePressCount > 0 && Time.time - lastEscapePressTime > maxTimeBetweenPresses)
            escapePressCount = 0;

        // Space key logic
        bool canToggleMixer = false;

        if (isSessionActive)
        {
            if (!isPreparationPhase)
                canToggleMixer = true;
            else if (isPreparationPhase && allowMixerDuringPreparation)
                canToggleMixer = true;
        }

        if (canToggleMixer)
        {
            if (keyboard[Key.Space].wasPressedThisFrame)
            {
                if (Time.time - lastSpacePressTime <= maxTimeBetweenPresses || spacePressCount == 0)
                    spacePressCount++;
                else
                    spacePressCount = 1;

                lastSpacePressTime = Time.time;

                if (spacePressCount >= requiredPresses)
                {
                    Debug.Log("Space pressed 5 times. Toggling LiveMixer.");
                    ToggleLiveMixerUI();
                    spacePressCount = 0;
                }
            }
        }

        if (spacePressCount > 0 && Time.time - lastSpacePressTime > maxTimeBetweenPresses)
            spacePressCount = 0;
    }

    private void ToggleLiveMixerUI()
    {
        if (liveMixerUI == null)
        {
            Debug.LogWarning("LiveMixerUI reference not set in AudioCoordinator.");
            return;
        }

        bool isActive = liveMixerUI.activeSelf;
        liveMixerUI.SetActive(!isActive);
    }

    private void ManageAudioVideoVisualization(bool shouldBeActive)
    {
        if (audioVideoVisualization == null)
            return;

        if (shouldBeActive)
        {
            bool strobeEnabled = beatGenerator != null && beatGenerator.enableStrobe;

            if (strobeEnabled)
            {
                if (audioVideoVisualization.activeSelf)
                    audioVideoVisualization.SetActive(false);

                if (videoPlayer != null && videoPlayer.isPlaying)
                    videoPlayer.Stop();

                return;
            }

            audioVideoVisualization.SetActive(true);

            // Ensure we begin from fully invisible so the master fade-in is smooth
            CanvasGroup cg = audioVideoVisualization.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = audioVideoVisualization.GetComponentInChildren<CanvasGroup>(true);
            if (cg != null)
                cg.alpha = 0f;

            if (videoPlayer != null)
            {
                if (videoPlayer.isPrepared)
                {
                    videoPlayer.Play();
                }
                else if (!videoIsPreparing)
                {
                    videoIsPreparing = true;
                    videoPlayer.prepareCompleted += OnVideoReadyToPlay;
                    videoPlayer.Prepare();
                }
            }
        }
        else
        {
            if (audioVideoVisualization.activeSelf)
                audioVideoVisualization.SetActive(false);

            if (videoPlayer != null && videoPlayer.isPlaying)
                videoPlayer.Stop();
        }
    }

    private void OnVideoReadyToPlay(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnVideoReadyToPlay;
        videoIsPreparing = false;
        vp.Play();
    }

    private void StartPlayPhase()
    {
        isPreparationPhase = false;

        if (countdownUI != null)
            countdownUI.gameObject.SetActive(false);

        if (countdownAdditionals != null)
            countdownAdditionals.SetActive(false);

        ManageAudioVideoVisualization(true);
        StartPlaying();

#if UNITY_EDITOR
        if (recordingType != RecordingType.None && !isRecording && canStartRecording)
            StartRecording();
#endif
    }

    public void StartPlaying()
    {
        if (!isSessionActive || isClipPlaying)
            return;

        Config config = ConfigManager.Instance.LoadConfiguration();
        float delay = 0f;

        if (stageIndex == 0 && currentStageLoops[0] == 0)
        {
            mantraStarted = false;

            if (config.sessionMode == "Sequential")
                beatGenerator.StartSequentialMode();
            else if (config.sessionMode == "Static")
                beatGenerator.StartStaticMode();

            InitiateMasterFadeIn();
            delay = fadeInTimeMasterMixer + 0.2f;
        }

        if (!noiseAudioSource.isPlaying)
        {
            noiseAudioSource.volume = 1.0f;
            noiseAudioSource.Play();
        }

        if (sfxClip1 != null && !sfxAudioSource.isPlaying)
        {
            sfxAudioSource.clip = sfxClip1;
            sfxAudioSource.loop = true;
            sfxAudioSource.volume = 1.0f;
            sfxAudioSource.Play();
            Debug.Log("Global SFX started immediately with White Noise.");
        }

        StartCoroutine(PlaySessionContentDelayed(delay, config));
    }

    public void AddStage()
    {
        Config config = ConfigManager.Instance.LoadConfiguration();

        if (!isSessionActive || config.sessionMode == "Static")
            return;

        if (currentStageLoops[stageIndex] < targetStageLoops[stageIndex])
        {
            currentStageLoops[stageIndex]++;
            Debug.Log(
                $"Looping Stage {stageIndex + 1}: Iteration {currentStageLoops[stageIndex]} of {targetStageLoops[stageIndex]}"
            );

            SetBeatGeneratorStageIndex(stageIndex);
            StartPlaying();
            return;
        }

        currentStageLoops[stageIndex] = 0;
        stageIndex = (stageIndex + 1) % 5;

        if (stageChanged)
        {
            beatGenerator?.UpdateStageIndexBeat();
            SetBeatGeneratorStageIndex(stageIndex);
        }

        if (stageIndex == 0)
            HandleSessionEnd();
        else
            StartPlaying();
    }

    private void EvaluateStageTransition()
    {
        if (IsReadyForNextStage())
        {
            stageChanged = true;
            AddStage();
        }
        else
        {
            stageChanged = false;
            Debug.Log(
                $"Repeating Stage {stageIndex + 1}. Loop count: {currentStageLoops[stageIndex]} + 1"
            );
            AddStage();
        }
    }

    private bool IsReadyForNextStage()
    {
        if (stageIndex >= 0 && stageIndex < targetStageLoops.Length)
        {
            if (currentStageLoops[stageIndex] < targetStageLoops[stageIndex])
                return false;
        }
        return true;
    }

    private AudioClip GetClipForCurrentStage()
    {
        switch (stageIndex)
        {
            case 0:
                return stage1Clip;
            case 1:
                return stage2Clip;
            case 2:
                return stage3Clip;
            case 3:
                return stage4Clip;
            case 4:
                return stage5Clip;
            default:
                Debug.LogError("StageIndex out of range: " + stageIndex);
                return null;
        }
    }

    private void SetSfxClip(int clipIndex, AudioClip clip)
    {
        sfxClip1 = clip;
    }

    private void EndMantraSession()
    {
        isCountingMantraIterations = false;
        isSessionActive = false;

        if (mantraAudioSource != null && mantraAudioSource.isPlaying)
        {
            mantraAudioSource.volume = 0f;
            StartCoroutine(FadeOutAudioSource(mantraAudioSource, fadeDurationAudioSource));
        }

        InitiateMasterFadeOut();
        StartCoroutine(StopAfterFade());
    }

    private void HandleSessionEnd()
    {
        isSessionActive = false;
        Config config = ConfigManager.Instance.LoadConfiguration();

        if (beatGenerator != null)
        {
            InitiateMasterFadeOut();
            StartCoroutine(FadeOutAudioSource(sfxAudioSource, fadeOutTimeMasterMixer));

            if (config.sessionMode == "Sequential")
                StartCoroutine(StopAfterFade());
            else
                StartCoroutine(TrainingModeFadeOut());
        }
        else
        {
            Debug.LogError("BeatGenerator is not assigned.");
        }
    }

    private IEnumerator StopSessionCoroutine()
    {
        // 1. Start the paired audio + visual quick fade
        InitiateMasterFadeOutQuick();

        // 2. Wait for the quick fade to complete
        yield return new WaitForSeconds(quickFadeOutTimeMasterMixer);

        // 3. Tear down everything after fade is done
        ManageAudioVideoVisualization(false);
        beatGenerator?.StopGenerator();

        speechAudioSource.Stop();
        sfxAudioSource.Stop();
        mantraAudioSource.Stop();
        noiseAudioSource.Stop();

        sfxAudioSource.volume = 0f;
        mantraAudioSource.volume = 0f;

#if UNITY_EDITOR
        if (recordingType != RecordingType.None && isRecording)
            StopRecording();
#endif

        // 4. Reload scene
        sceneManager_SessionWizard.ReloadScene();
    }

    private IEnumerator TransitionToPreparation()
    {
        if (controlledFade != null)
        {
            controlledFade.FadeOut(menuFadeOutDuration);
            yield return new WaitForSeconds(menuFadeOutDuration);
        }
        else
        {
            yield return null;
        }

        yield return PreparationPhaseCoroutine();
    }

    private IEnumerator PreparationPhaseCoroutine()
    {
        if (blockoutPanel != null)
            blockoutPanel.SetActive(true);

        if (useWaitSecondForPreparation)
        {
            if (countdownUI != null)
            {
                int totalSecs = Mathf.CeilToInt(waitSecondsBeforePlaying);
                int h = totalSecs / 3600;
                int m = (totalSecs % 3600) / 60;
                int s = totalSecs % 60;

                countdownUI.SetTime(h, m, s);
                countdownUI.StartTimer();

                while (countdownUI.IsRunning() && !skipPreparationWait)
                    yield return null;

                if (skipPreparationWait)
                    countdownUI.PauseTimer();
            }
            else
            {
                float remainingTime = waitSecondsBeforePlaying;
                while (remainingTime > 0 && !skipPreparationWait)
                {
                    yield return new WaitForSeconds(0.05f);
                    remainingTime -= 0.05f;
                }
            }
        }

        StartPlayPhase();
    }

    private IEnumerator PlaySessionContentDelayed(float delay, Config config)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (config.sessionMode == "Sequential")
        {
            isClipPlaying = true;
            StartCoroutine(PlayStage());

            if (
                !mantraStarted
                && stageIndex == 0
                && mantraAudioSource.clip != null
                && mantraAudioSource.clip.name != "Silent"
            )
            {
                mantraAudioSource.volume = 1.0f;
                mantraAudioSource.Play();
                mantraStarted = true;
                isCountingMantraIterations = false;
                Debug.Log("Mantra started immediately in Sequential mode after fade.");
            }
        }
        else if (config.sessionMode == "Static")
        {
            if (
                !mantraStarted
                && mantraAudioSource.clip != null
                && mantraAudioSource.clip.name != "Silent"
            )
            {
                mantraAudioSource.volume = 1.0f;
                mantraAudioSource.Play();
                mantraStarted = true;
                isCountingMantraIterations =
                    (recordingType != RecordingType.None) && mantraIterations > 0;

                if (isCountingMantraIterations)
                {
                    currentMantraIteration = 0;
                    mantraTimeElapsed = 0f;
                    Debug.Log(
                        $"Mantra started with iteration counting ({mantraIterations} iterations) in Static mode after fade"
                    );
                }
                else
                {
                    Debug.Log(
                        "Mantra started continuously without iteration counting in Static mode after fade"
                    );
                }
            }

            isClipPlaying = true;
        }
    }

    private IEnumerator PlayStage()
    {
        AudioClip currentSpeechClip = GetClipForCurrentStage();

        if (
            mantraAudioSource.clip != null
            && mantraAudioSource.clip.name != "Silent"
            && !mantraAudioSource.isPlaying
            && !mantraStarted
        )
        {
            Config config = ConfigManager.Instance.LoadConfiguration();
            bool shouldCountIterations = config.sessionMode == "Static";

            mantraAudioSource.volume = 1.0f;
            mantraAudioSource.Play();
            mantraStarted = true;
            isCountingMantraIterations =
                (recordingType != RecordingType.None)
                && shouldCountIterations
                && mantraIterations > 0;

            if (isCountingMantraIterations)
            {
                currentMantraIteration = 0;
                mantraTimeElapsed = 0f;
                Debug.Log(
                    $"Mantra playback started in PlayStage with {mantraIterations} iterations"
                );
            }
            else
            {
                Debug.Log("Mantra playback started in PlayStage without iteration counting");
            }
        }

        if (currentSpeechClip != null)
        {
            Debug.Log(
                $"Playing stage {stageIndex + 1} clip: {currentSpeechClip.name}, length: {currentSpeechClip.length}s"
            );

            speechAudioSource.clip = currentSpeechClip;
            speechAudioSource.volume = targetSpeechVolume;
            speechAudioSource.Play();

            float waitTime = currentSpeechClip.length;

            Debug.Log($"Waiting {waitTime}s for clip to finish playing");

            yield return new WaitForSeconds(waitTime);

            Debug.Log($"Stage {stageIndex + 1} playback complete, setting isClipPlaying = false");
            isClipPlaying = false;

            EvaluateStageTransition();
        }
        else
        {
            Debug.LogError("No hypnosis clip set for current stage: " + stageIndex);
            isClipPlaying = false;
        }
    }

    private IEnumerator FadeInAudioSource(AudioSource source, float duration)
    {
        float currentTime = 0;
        float startVolume = 0f;
        float endVolume = (source == speechAudioSource) ? targetSpeechVolume : 1f;

        source.volume = startVolume;
        source.Play();

        while (currentTime < duration)
        {
            currentTime += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, endVolume, currentTime / duration);
            yield return null;
        }

        source.volume = endVolume;
    }

    private IEnumerator FadeOutAudioSource(AudioSource source, float duration)
    {
        if (source == null || !source.isPlaying)
            yield break;

        float startVolume = source.volume;
        float currentTime = 0;

        while (currentTime < duration)
        {
            currentTime += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, currentTime / duration);
            yield return null;
        }

        source.Stop();
        source.volume = 0f;
    }

    private IEnumerator StopAfterFade()
    {
        if (mantraAudioSource != null && mantraAudioSource.isPlaying)
        {
            mantraAudioSource.volume = 0f;
            mantraAudioSource.Stop();
        }

        // Wait for the master fade-out to finish before tearing down visuals
        yield return new WaitForSeconds(fadeOutTimeMasterMixer);

        ManageAudioVideoVisualization(false);
        beatGenerator.StopGenerator();
        speechAudioSource.Stop();
        sfxAudioSource.Stop();
        mantraAudioSource.Stop();
        noiseAudioSource.Stop();

        sfxAudioSource.volume = 0f;
        mantraAudioSource.volume = 0f;

#if UNITY_EDITOR
        if (recordingType != RecordingType.None && isRecording)
            StopRecording();
#endif

        if (repeatSession)
            RestartSession();
        else
            ReloadScene();
    }

    private IEnumerator TrainingModeFadeOut()
    {
        if (mantraAudioSource != null && mantraAudioSource.isPlaying)
        {
            mantraAudioSource.volume = 0f;
            mantraAudioSource.Stop();
        }

        // Wait for the master fade-out to finish before tearing down visuals
        yield return new WaitForSeconds(fadeOutTimeMasterMixer);

        ManageAudioVideoVisualization(false);

        speechAudioSource.Stop();
        sfxAudioSource.Stop();
        mantraAudioSource.Stop();
        noiseAudioSource.Stop();

        sfxAudioSource.volume = 0f;
        mantraAudioSource.volume = 0f;

#if UNITY_EDITOR
        if (recordingType != RecordingType.None && isRecording)
            StopRecording();
#endif

        if (repeatSession)
            RestartSession();
        else
            ReloadScene();
    }

    private IEnumerator LoadCustomSfxClipAsync(string filename)
    {
        string path = ConfigPath.GetMediaFilePath(filename);

        Debug.Log($"[CustomSFX] Attempting to load: {path}");

        if (!File.Exists(path))
        {
            string errorMsg = $"Custom SFX file not found: '{filename}' at path: {path}";
            Debug.LogError($"[CustomSFX] {errorMsg}");

            globalErrorHandling?.CallGlobalErrorWithConfirmation(
                $"The custom SFX file '{filename}' could not be found.\n\nPlease ensure the file exists in your configuration's 'media' folder.",
                null
            );
            yield break;
        }

        AudioType audioType = GetAudioTypeFromExtension(filename);
        string fileUrl = path.StartsWith("file://") ? path : "file://" + path;

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(fileUrl, audioType))
        {
            www.timeout = 30;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                string errorMsg = $"Failed to load custom SFX '{filename}': {www.error}";
                Debug.LogError($"[CustomSFX] {errorMsg}");

                globalErrorHandling?.CallGlobalErrorWithConfirmation(
                    $"Failed to load custom SFX file '{filename}'.\n\nThe file may be corrupted or in an unsupported format.\nSupported formats: WAV, MP3, OGG",
                    null
                );
                yield break;
            }

            AudioClip rawClip = null;
            try
            {
                rawClip = DownloadHandlerAudioClip.GetContent(www);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CustomSFX] Exception while extracting AudioClip: {ex.Message}");
                globalErrorHandling?.CallGlobalErrorWithConfirmation(
                    $"Error processing custom SFX file '{filename}'.",
                    null
                );
                yield break;
            }

            if (rawClip == null)
            {
                Debug.LogError("[CustomSFX] AudioClip is null after loading.");
                globalErrorHandling?.CallGlobalErrorWithConfirmation(
                    $"Custom SFX file '{filename}' could not be decoded as audio.",
                    null
                );
                yield break;
            }

            while (rawClip.loadState == AudioDataLoadState.Loading)
                yield return null;

            if (rawClip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError(
                    $"[CustomSFX] AudioClip failed to load properly. State: {rawClip.loadState}"
                );
                globalErrorHandling?.CallGlobalErrorWithConfirmation(
                    $"Custom SFX file '{filename}' failed to load properly.",
                    null
                );
                yield break;
            }

            AudioClip normalizedClip = AudioNormalizer.NormalizeClip(rawClip, -18f);

            if (normalizedClip == null)
            {
                Debug.LogWarning(
                    "[CustomSFX] Normalization returned null, using raw clip without normalization."
                );
                normalizedClip = rawClip;
            }

            loadedCustomSfxClip = normalizedClip;
            sfxClip1 = normalizedClip;

            Debug.Log(
                $"[CustomSFX] Successfully loaded and normalized custom SFX: {filename} "
                    + $"(Duration: {normalizedClip.length:F2}s, Sample Rate: {normalizedClip.frequency}Hz, Channels: {normalizedClip.channels})"
            );
        }
    }

    private AudioType GetAudioTypeFromExtension(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();

        switch (extension)
        {
            case ".wav":
                return AudioType.WAV;
            case ".mp3":
                return AudioType.MPEG;
            case ".ogg":
            case ".oga":
                return AudioType.OGGVORBIS;
            case ".aiff":
            case ".aif":
                return AudioType.AIFF;
            default:
                Debug.LogWarning(
                    $"[CustomSFX] Unknown audio extension '{extension}', defaulting to WAV"
                );
                return AudioType.WAV;
        }
    }

#if UNITY_EDITOR
    private void SetupRecorder()
    {
        if (recordingType == RecordingType.None)
            return;

        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();

        if (recordingType == RecordingType.AudioWAV)
        {
            var audioRecorderSettings = ScriptableObject.CreateInstance<AudioRecorderSettings>();

            string audioDirectory = Path.Combine(Application.persistentDataPath, "AudioRecordings");
            if (!Directory.Exists(audioDirectory))
            {
                try
                {
                    Directory.CreateDirectory(audioDirectory);
                    Debug.Log($"Created audio recording directory at: {audioDirectory}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to create audio recording directory: {e.Message}");
                    recordingType = RecordingType.None;
                    return;
                }
            }

            audioRecorderSettings.OutputFile = Path.Combine(audioDirectory, "Genesis_Session");
            controllerSettings.AddRecorderSettings(audioRecorderSettings);
            Debug.Log("Audio recorder initialized successfully");
        }
        else if (recordingType == RecordingType.VideoMP4)
        {
            var movieRecorderSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();

            movieRecorderSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 3840,
                OutputHeight = 2160,
            };

            movieRecorderSettings.OutputFormat = MovieRecorderSettings
                .VideoRecorderOutputFormat
                .MP4;
            movieRecorderSettings.CaptureAlpha = false;

            if (movieRecorderSettings.GetType().GetProperty("VideoBitRateMode") != null)
            {
                var highQualityValue = 2;
                movieRecorderSettings
                    .GetType()
                    .GetProperty("VideoBitRateMode")
                    .SetValue(movieRecorderSettings, highQualityValue);
            }

            movieRecorderSettings.FrameRate = 60;
            movieRecorderSettings.CaptureAudio = true;
            movieRecorderSettings.CapFrameRate = true;

            string videoDirectory = Path.Combine(Application.persistentDataPath, "VideoRecordings");
            if (!Directory.Exists(videoDirectory))
            {
                try
                {
                    Directory.CreateDirectory(videoDirectory);
                    Debug.Log($"Created video recording directory at: {videoDirectory}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to create video recording directory: {e.Message}");
                    recordingType = RecordingType.None;
                    return;
                }
            }

            movieRecorderSettings.OutputFile = Path.Combine(
                videoDirectory,
                "HypnosisPriming_Session"
            );
            controllerSettings.AddRecorderSettings(movieRecorderSettings);
            Debug.Log("Video recorder initialized successfully");
        }

        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 60;

        recorderController = new RecorderController(controllerSettings);
    }

    private void StartRecording()
    {
        if (recordingType == RecordingType.None || recorderController == null || !canStartRecording)
            return;

        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string recordingDirectory =
                recordingType == RecordingType.AudioWAV
                    ? Path.Combine(Application.persistentDataPath, "AudioRecordings")
                    : Path.Combine(Application.persistentDataPath, "VideoRecordings");

            string outputFilePath = Path.Combine(
                recordingDirectory,
                $"HypnosisPriming_Session_{timestamp}"
            );
            var settings = recorderController.Settings.RecorderSettings.FirstOrDefault();

            if (settings != null)
                settings.OutputFile = outputFilePath;

            recorderController.PrepareRecording();
            recorderController.StartRecording();
            isRecording = true;
            canStartRecording = false;

            Debug.Log($"Started {recordingType} recording to: {outputFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start recording: {e.Message}");
            isRecording = false;
            canStartRecording = true;
        }
    }

    private void StopRecording()
    {
        if (recordingType == RecordingType.None || recorderController == null || !isRecording)
            return;

        try
        {
            recorderController.StopRecording();
            isRecording = false;
            Debug.Log($"{recordingType} recording stopped successfully");

            if (triggerStopEvent)
                onRecordingStopped?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error stopping recording: {e.Message}");
            isRecording = false;
            canStartRecording = true;
        }
    }
#endif
}