using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Evo.UI;
using Openrouter;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[System.Serializable]
public class ProgressUpdateEvent : UnityEvent<float> { }

public class SceneManager_SessionWizard : MonoBehaviour
{
    [Serializable]
    public class CheckupSuccessEvent : UnityEvent { }

    private enum SceneState
    {
        PathUpdate,
        Checkup,
        Session,
    }

    // External References
    public AudioCoordinator audioCoordinator;
    public CoroutineManager coroutineManager;

    public Tabs tabs;
    public DirectoryScanner directoryScanner;

    // Scene Navigation
    public string configScene;
    public string configCreatorScene = "ConfigCreator";

    // MonoBehaviour References for Loading
    public MonoBehaviour AudioSourceWhitenoiseManager;
    public MonoBehaviour AudioSourceSpeechReverbPreset;
    public MonoBehaviour BeatGenerator;

    // TTS Client reference: OpenRouter only
    public MonoBehaviour TTSClient_OpenRouter;

    public MonoBehaviour MixerConfig;
    public MonoBehaviour ControlledFade;
    public MonoBehaviour SpatialSubliminalizer;

    [Header("Mixer References")]
    public MonoBehaviour LiveMixer;

    // Audio Mixer Settings
    [SerializeField]
    private UnityEngine.Audio.AudioMixer audioMixer;

    [SerializeField]
    private string masterVolumeParameter = "Master_Track.Attenuation.Volume";

    // UI Elements
    public GameObject loadingIndicator;
    public GameObject Button_StartSession;

    [SerializeField]
    private Transform messageParent;

    [SerializeField]
    private GameObject messagePrefab;

    // Screen settings
    private int defaultWidth = 1600;
    private int defaultHeight = 900;
    private bool isFullscreen = false;

    private bool requireFullscreen;

    // Message queue system
    private Queue<string> messageQueue = new Queue<string>();
    private bool isProcessingMessages = false;
    private List<GameObject> progressMessagePool = new List<GameObject>();
    private int poolSize = 20;

    // Progress tracking
    private float currentProgress;
    private float progressStep;
    public ProgressUpdateEvent OnProgressUpdate;

    private int synthesisCompletedCount = 0;
    private readonly int totalSynthesisCount = 6; // 5 stages + 1 mantra
    private bool isSynthesisComplete = false;
    private bool isMessageProcessingComplete = false;

    // State management
    private SceneState currentState = SceneState.PathUpdate;

    [SerializeField]
    private bool isScanningInProgress = false;
    public bool hasAlreadyFetched = false;
    private GlobalErrorHandling globalErrorHandling;

    // Coroutine tracking for cancellation
    private Coroutine currentConfigLoadingRoutine;

    public CheckupSuccessEvent OnCheckupSuccess;

    // Regex patterns for tag detection
    private static readonly Regex cutTagRegex = new Regex(@"<cut>", RegexOptions.Compiled);
    private static readonly Regex cutAudioTagRegex = new Regex(
        @"<cut_audio=(?<url>[^>\s]+)>",
        RegexOptions.Compiled
    );
    private static readonly Regex cutSilenceTagRegex = new Regex(
        @"<cut_silence=(?<duration>[0-9]+\.?[0-9]*)>",
        RegexOptions.Compiled
    );

    public void Awake()
    {
        globalErrorHandling = FindAnyObjectByType<GlobalErrorHandling>();
    }

    void Start()
    {
        coroutineManager = FindAnyObjectByType<CoroutineManager>();
        if (coroutineManager == null)
        {
            Debug.LogError(
                "CoroutineManager not found in the scene. Please add the CoroutineManager script to a GameObject."
            );
            return;
        }

        SetWindowedMode(defaultWidth, defaultHeight);

        StartCoroutine(ResetMasterVolume(1.0f));

        for (int i = 0; i < poolSize; i++)
        {
            GameObject messageInstance = Instantiate(messagePrefab, messageParent);
            messageInstance.SetActive(false);
            progressMessagePool.Add(messageInstance);
        }
    }

    private IEnumerator ResetMasterVolume(float fadeInDuration)
    {
        if (audioMixer == null)
        {
            Debug.LogWarning("AudioMixer is not assigned, cannot reset master volume.");
            yield break;
        }

        float startVolume = -80f;
        float targetVolume = 0f;
        audioMixer.SetFloat(masterVolumeParameter, startVolume);

        float startTime = Time.time;
        while (Time.time - startTime < fadeInDuration)
        {
            float t = (Time.time - startTime) / fadeInDuration;
            float currentVolume = Mathf.Lerp(startVolume, targetVolume, t);
            audioMixer.SetFloat(masterVolumeParameter, currentVolume);
            yield return null;
        }

        audioMixer.SetFloat(masterVolumeParameter, targetVolume);
        Debug.Log(
            $"Master volume faded in to {targetVolume} dB over {fadeInDuration} seconds on scene start."
        );
    }

    public void SetWindowedMode(int width, int height)
    {
        isFullscreen = false;
        Screen.SetResolution(width, height, isFullscreen);
        Debug.Log($"Switched to windowed mode: {width}x{height}");
    }

    public void SetFullscreenMode()
    {
        isFullscreen = true;
        Screen.SetResolution(
            Screen.currentResolution.width,
            Screen.currentResolution.height,
            isFullscreen
        );
        Debug.Log("Switched to fullscreen mode.");
    }

    public void StartConfigFetching()
    {
        isScanningInProgress = true;
        Button_StartSession.SetActive(false);
        loadingIndicator.SetActive(true);

        currentConfigLoadingRoutine = coroutineManager.StartCoroutine(
            ConfigurationLoadingRoutine()
        );
        Debug.Log("From StartConfigFetching(): " + currentState);
    }

    public void CheckConfigInSeconds(float seconds)
    {
        coroutineManager.StartCoroutine(CheckConfigAfterDelay(seconds));
    }

    private bool IsStartSessionAllowed()
    {
        Debug.Log(isScanningInProgress);
        Debug.Log(currentState);
        return !isScanningInProgress;
    }

    private void CheckAudioCoordinatorConfiguration()
    {
        Debug.Log("CheckAudioCoordinatorConfiguration() called.");

        if (hasAlreadyFetched)
        {
            Debug.Log(
                "CheckAudioCoordinatorConfiguration() already completed successfully, skipping duplicate call."
            );
            return;
        }

        if (audioCoordinator != null)
        {
            bool areAllHypnosisClipsAssigned = audioCoordinator.AreAllClipsAssigned();
            bool areAllClipsAssigned = areAllHypnosisClipsAssigned;

            if (areAllClipsAssigned)
            {
                CreateProgressMessage("AudioCoordinator: All audio clips correctly assigned.");

                string sessionLength = audioCoordinator.GetFormattedSessionLength();
                CreateProgressMessage($"<b>Estimated session length: {sessionLength}</b>");

                hasAlreadyFetched = true;
            }
            else
            {
                CreateProgressMessage("AudioCoordinator: Not all audio clips are assigned!");
            }
        }
        else
        {
            Debug.LogError("AudioCoordinator script is not assigned in the SceneManager.");
            CreateProgressMessage("Error: AudioCoordinator script not assigned.");
        }
    }

    IEnumerator ConfigurationLoadingRoutine()
    {
        Config config = ConfigManager.Instance.LoadConfiguration();

        requireFullscreen = config.enableStrobe;

        currentProgress = 0f;
        synthesisCompletedCount = 0;
        isSynthesisComplete = false;
        isMessageProcessingComplete = false;
        bool hasTimeoutOccurred = false;
        float startTime = Time.time;
        const float OVERALL_TIMEOUT = 360f;

        MonoBehaviour[] scriptsToConfigure =
        {
            audioCoordinator,
            AudioSourceWhitenoiseManager,
            AudioSourceSpeechReverbPreset,
            BeatGenerator,
            MixerConfig,
            ControlledFade,
            SpatialSubliminalizer,
            LiveMixer,
        };

        MonoBehaviour selectedTTSClient = TTSClient_OpenRouter;
        Debug.Log("Selected OpenRouter TTS client.");

        int estimatedFinalMessages = 0;
        int estimatedTotalSteps =
            scriptsToConfigure.Length + 1 + totalSynthesisCount + estimatedFinalMessages;
        progressStep = 100f / estimatedTotalSteps;

        // TTS CLIENT FIRST: Subscribe to events and configure before any other script
        if (selectedTTSClient != null)
        {
            var openRouterTTS = selectedTTSClient as SynthesizeTTS_OpenRouter;
            if (openRouterTTS != null)
            {
                openRouterTTS.OnSynthesisComplete += OnSynthesisCompleted;
                Debug.Log("Subscribed to OpenRouter TTS synthesis complete event.");
            }
            else
            {
                Debug.LogWarning(
                    "OpenRouter TTS client is assigned but not of type SynthesizeTTS_OpenRouter."
                );
            }

            yield return coroutineManager.StartCoroutineWithCallback(
                LoadConfigAsync(selectedTTSClient),
                () =>
                {
                    CreateProgressMessage(
                        $"{selectedTTSClient.GetType().Name}: Configuration loaded successfully."
                    );
                }
            );
        }
        else
        {
            CreateProgressMessage("No TTS client selected due to configuration settings.");
            isSynthesisComplete = true;
            isMessageProcessingComplete = true;
            Debug.Log(
                "No TTS client selected, marking synthesis and message processing as complete."
            );
        }

        // OTHER SCRIPTS SECOND: These load while TTS synthesis runs in the background
        foreach (var script in scriptsToConfigure)
        {
            if (script != null)
            {
                yield return coroutineManager.StartCoroutineWithCallback(
                    LoadConfigAsync(script),
                    () =>
                    {
                        CreateProgressMessage(
                            $"{script.GetType().Name}: Configuration loaded successfully."
                        );
                    }
                );
            }
            else
            {
                Debug.LogWarning("A script is not assigned and skipped.");
            }
        }

        while (!isSynthesisComplete && !hasTimeoutOccurred && selectedTTSClient != null)
        {
            if (Time.time - startTime > OVERALL_TIMEOUT)
            {
                hasTimeoutOccurred = true;
                Debug.LogError(
                    $"Overall configuration loading timed out after {OVERALL_TIMEOUT} seconds"
                );

                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        $"The configuration loading process has timed out after {OVERALL_TIMEOUT} seconds. This might be caused by network issues or problems with the TTS service. Please try again later.",
                        () =>
                        {
                            ReloadScene();
                        }
                    );
                }

                break;
            }

            yield return null;
        }

        if (!hasTimeoutOccurred)
        {
            while (isSynthesisComplete && !isMessageProcessingComplete)
            {
                yield return null;
            }

            CheckAudioCoordinatorConfiguration();

            while (!isMessageProcessingComplete)
            {
                yield return null;
            }

            currentProgress = 100f;
            OnProgressUpdate.Invoke(currentProgress);

            if (hasAlreadyFetched)
            {
                Debug.Log("Synthesis and message processing complete. Finalizing checkup.");
                OnCheckupSuccess.Invoke();
                Button_StartSession.SetActive(true);
                loadingIndicator.SetActive(false);
                Debug.Log("Checkup complete: Start Session button activated.");
            }
            else
            {
                Debug.LogWarning(
                    "Synthesis complete but hasAlreadyFetched is false. Check logs for assignment errors."
                );
            }
        }
        else
        {
            Button_StartSession.SetActive(false);
            loadingIndicator.SetActive(false);
        }

        isScanningInProgress = false;

        if (selectedTTSClient != null)
        {
            var openRouterTTS = selectedTTSClient as SynthesizeTTS_OpenRouter;
            if (openRouterTTS != null)
            {
                openRouterTTS.OnSynthesisComplete -= OnSynthesisCompleted;
                Debug.Log("Unsubscribed from OpenRouter TTS synthesis complete event.");
            }
        }
    }

    IEnumerator LoadConfigAsync(MonoBehaviour script)
    {
        yield return new WaitForSeconds(0.15f);
        script.Invoke("LoadConfig", 0f);
    }

    IEnumerator CheckConfigAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        CheckAudioCoordinatorConfiguration();
    }

    IEnumerator ProcessMessageQueue()
    {
        isProcessingMessages = true;

        while (messageQueue.Count > 0)
        {
            string message = messageQueue.Dequeue();
            GameObject messageInstance = GetPooledMessage();

            if (messageInstance != null)
            {
                TextMeshProUGUI tmpComponent = messageInstance.GetComponent<TextMeshProUGUI>();
                if (tmpComponent != null)
                {
                    tmpComponent.text = message;
                    messageInstance.SetActive(true);

                    currentProgress += progressStep;
                    currentProgress = Mathf.Min(currentProgress, 100f);
                    OnProgressUpdate.Invoke(currentProgress);

                    yield return new WaitForSeconds(0.08f);
                }
            }
        }

        isProcessingMessages = false;

        if (isSynthesisComplete)
        {
            isMessageProcessingComplete = true;
            Debug.Log("All messages processed after synthesis completion.");
        }
    }

    private IEnumerator DelayedSynthesisProgress()
    {
        yield return new WaitForSeconds(0.5f);

        if (synthesisCompletedCount >= totalSynthesisCount)
            yield break;

        synthesisCompletedCount++;
        Debug.Log($"Synthesis completed: {synthesisCompletedCount}/{totalSynthesisCount}");
        CreateProgressMessage(
            $"TTS Synthesis completed: {synthesisCompletedCount}/{totalSynthesisCount}"
        );
    }

    private void OnSynthesisCompleted()
    {
        isSynthesisComplete = true;
        Debug.Log(
            $"All TTS synthesis completed. Synthesis process finished. Waiting for message processing."
        );

        if (isProcessingMessages)
        {
            Debug.Log("Waiting for message processing to complete before finalizing checkup.");
        }
        else
        {
            isMessageProcessingComplete = true;
            Debug.Log("No messages in queue, marking message processing as complete.");
        }
    }

    public void CreateProgressMessage(string message)
    {
        messageQueue.Enqueue(message);
        if (!isProcessingMessages)
        {
            StartCoroutine(ProcessMessageQueue());
        }
    }

    private GameObject GetPooledMessage()
    {
        foreach (GameObject message in progressMessagePool)
        {
            if (!message.activeInHierarchy)
            {
                return message;
            }
        }
        return null;
    }

    public void ResetProgress()
    {
        currentProgress = 0f;
        synthesisCompletedCount = 0;
        isSynthesisComplete = false;
        isMessageProcessingComplete = false;
        hasAlreadyFetched = false;

        OnProgressUpdate.Invoke(0f);

        foreach (GameObject message in progressMessagePool)
        {
            if (message != null)
            {
                message.SetActive(false);
            }
        }

        if (loadingIndicator != null)
        {
            loadingIndicator.SetActive(false);
        }
    }

    public void ResetLoadingState()
    {
        currentProgress = 0f;
        synthesisCompletedCount = 0;
        isSynthesisComplete = false;
        isMessageProcessingComplete = false;

        Button_StartSession.SetActive(false);
        loadingIndicator.SetActive(false);
        isScanningInProgress = false;

        OnProgressUpdate.Invoke(0f);
        CreateProgressMessage("Loading process reset due to timeout or error.");
    }

    private void CancelCheckupProcess()
    {
        Debug.Log("CancelCheckupProcess: Attempting to interrupt and reset.");

        if (TTSClient_OpenRouter != null)
        {
            var openRouterTTS = TTSClient_OpenRouter as SynthesizeTTS_OpenRouter;
            if (openRouterTTS != null)
            {
                openRouterTTS.OnSynthesisComplete -= OnSynthesisCompleted;
                Debug.Log("Unsubscribed from OpenRouter TTS event due to cancellation.");
            }
        }

        if (currentConfigLoadingRoutine != null)
        {
            coroutineManager.StopCoroutine(currentConfigLoadingRoutine);
            currentConfigLoadingRoutine = null;
            Debug.Log("Stopped ConfigurationLoadingRoutine coroutine.");
        }

        if (TTSClient_OpenRouter != null)
        {
            var openRouterTTS = TTSClient_OpenRouter as SynthesizeTTS_OpenRouter;
            if (openRouterTTS != null)
            {
                openRouterTTS.StopSynthesis();
            }
        }

        ResetLoadingState();
    }

    public void AdvanceState()
    {
        if (!isScanningInProgress)
        {
            switch (currentState)
            {
                case SceneState.PathUpdate:

                    if (ListElementPath.currentlySelectedElement == null)
                    {
                        Debug.LogWarning("AdvanceState blocked: No configuration selected.");
                        if (globalErrorHandling != null)
                        {
                            globalErrorHandling.CallGlobalErrorWithConfirmation(
                                "No configuration selected. Please select a configuration file from the list or import one to continue.",
                                null
                            );
                        }
                        return;
                    }

                    // Pre-check: Verify API key availability before entering checkup
                    // when TTS synthesis is required (not all stages are cached)
                    if (!IsConfigFullyCached())
                    {
                        if (!IsOpenRouterApiKeyAvailable())
                        {
                            Debug.LogWarning(
                                "AdvanceState blocked: TTS synthesis required but no OpenRouter API key is configured."
                            );
                            if (globalErrorHandling != null)
                            {
                                globalErrorHandling.CallGlobalErrorWithConfirmation(
                                    "TTS synthesis is required for this configuration but no OpenRouter API key is configured.\n\nPlease add your API key in the settings before proceeding.",
                                    null
                                );
                            }
                            return;
                        }
                    }

                    currentState = SceneState.Checkup;
                    SwitchAfterPathUpdate();
                    break;

                case SceneState.Checkup:
                    if (Mathf.Approximately(currentProgress, 100f) && hasAlreadyFetched == true)
                    {
                        currentState = SceneState.Session;
                        if (requireFullscreen)
                        {
                            SetFullscreenMode();
                        }
                        SwitchAfterCheckup();
                    }
                    break;

                case SceneState.Session:
                    break;
            }
        }
        else
        {
            Debug.LogWarning("Cannot advance state. Scanning is in progress.");
        }
    }

    // Loads the Config Creator scene after verifying that an OpenRouter API key is available.
    // The Config Creator requires an API key for all LLM operations, so this check prevents
    // the user from entering a scene that cannot function without credentials.
    public void LoadConfigCreator()
    {
        if (!IsOpenRouterApiKeyAvailable())
        {
            Debug.LogWarning("LoadConfigCreator: No OpenRouter API key is configured.");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    "An OpenRouter API key is required to use the Config Creator.\n\nPlease add your API key in the settings before proceeding.",
                    null
                );
            }
            return;
        }

        if (!string.IsNullOrEmpty(configCreatorScene))
        {
            SceneManager.LoadScene(configCreatorScene);
        }
        else
        {
            Debug.LogError("Config Creator scene name is not set in the Inspector.");
        }
    }

    public void GoBackState()
    {
        // Allow interruption if scanning is in progress
        if (isScanningInProgress)
        {
            CancelCheckupProcess();
        }

        switch (currentState)
        {
            case SceneState.PathUpdate:
                LogoutFreeVersion();
                break;

            case SceneState.Checkup:
                currentState = SceneState.PathUpdate;
                ResetProgress();

                // Ensure the advance button is visible for the next attempt
                if (Button_StartSession != null)
                {
                    Button_StartSession.SetActive(true);
                }

                SwitchBackFromCheckup();
                break;

            case SceneState.Session:
                if (audioCoordinator != null)
                {
                    audioCoordinator.StopSession();
                }

                currentState = SceneState.Checkup;
                SetWindowedMode(defaultWidth, defaultHeight);
                tabs.OpenTab(0);
                break;
        }
    }

    public void LogoutFreeVersion()
    {
        if (DialogManager.Instance != null)
        {
            DialogManager.Instance.ShowDialog(
                "Do you want to close the application?",
                QuitApp,
                null
            );
        }
        else
        {
            QuitApp();
        }
    }

    public void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void ReloadScene()
    {
        if (!string.IsNullOrEmpty(configScene))
        {
            StartCoroutine(ReloadSceneCoroutine(configScene));
        }
        else
        {
            Debug.LogError("configScene is not set in the Inspector!");
        }
    }

    private IEnumerator ReloadSceneCoroutine(string sceneName)
    {
        if (ControlledFade != null)
        {
            ControlledFade controlledFadeScript = ControlledFade.GetComponent<ControlledFade>();
            if (controlledFadeScript != null)
            {
                controlledFadeScript.StopFadeCoroutine();
                Debug.Log("Stopped ControlledFade coroutine before scene transition.");
            }
            else
            {
                Debug.LogWarning("ControlledFade component not found on referenced GameObject.");
            }
        }
        else
        {
            Debug.LogWarning("ControlledFade reference is not assigned in SceneManager.");
        }

        if (audioMixer != null)
        {
            float startVolume;
            if (!audioMixer.GetFloat(masterVolumeParameter, out startVolume))
            {
                startVolume = 0f;
            }
            float targetVolume = -80f;
            float fadeOutDuration = 1.0f;

            float startTime = Time.time;
            while (Time.time - startTime < fadeOutDuration)
            {
                float t = (Time.time - startTime) / fadeOutDuration;
                float currentVolume = Mathf.Lerp(startVolume, targetVolume, t);
                audioMixer.SetFloat(masterVolumeParameter, currentVolume);
                yield return null;
            }

            audioMixer.SetFloat(masterVolumeParameter, targetVolume);
            Debug.Log(
                $"Master volume faded out to {targetVolume} dB over {fadeOutDuration} seconds before scene reload."
            );
        }
        else
        {
            Debug.LogWarning("AudioMixer is not assigned, skipping master volume fade-out.");
        }

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
    }

    public void SwitchBackFromCheckup()
    {
        tabs.OpenTab(0);
    }

    public void SwitchAfterPathUpdate()
    {
        ResetProgress();
        tabs.OpenTab(1);
        StartConfigFetching();
    }

    public void SwitchAfterCheckup()
    {
        if (IsStartSessionAllowed())
        {
            audioCoordinator.StartPreparationPhase();
        }
        else
        {
            Debug.LogWarning(
                "Cannot start the session. Scanning is in progress or checkup is not complete."
            );
        }
    }

    private bool ContainsOnlySilenceTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        string processedText = cutTagRegex.Replace(text, "");
        processedText = cutSilenceTagRegex.Replace(processedText, "");
        if (cutAudioTagRegex.IsMatch(processedText))
            return false;
        processedText = cutAudioTagRegex.Replace(processedText, "");
        return string.IsNullOrWhiteSpace(processedText);
    }

    private bool CheckStageCache(string text, Config config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (ContainsOnlySilenceTags(text))
            return true;

        using (SHA256 hash = SHA256.Create())
        {
            string input =
                $"{text}|{config.ttsModel}|{config.ttsVoice}";
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));

            string path = System.IO.Path.Combine(
                ConfigPath.GetCacheFolderPath(),
                builder.ToString() + ".wav"
            );
            return System.IO.File.Exists(path);
        }
    }

    private bool IsConfigFullyCached()
    {
        if (!System.IO.File.Exists(ConfigPath.Path))
            return false;

        Config config = new ConfigManager().LoadConfiguration();

        return CheckStageCache(config.stage1TTS, config)
            && CheckStageCache(config.stage2TTS, config)
            && CheckStageCache(config.stage3TTS, config)
            && CheckStageCache(config.stage4TTS, config)
            && CheckStageCache(config.stage5TTS, config)
            && CheckStageCache(config.mantraTTS, config);
    }

    // Checks whether a valid OpenRouter API key is available by delegating
    // to OpenrouterKeyManager.IsApiKeyAvailable(). Returns true only if
    // the encrypted key file exists, can be decrypted, and is non-empty.
    private bool IsOpenRouterApiKeyAvailable()
    {
        OpenrouterKeyManager keyManager = FindAnyObjectByType<OpenrouterKeyManager>();
        if (keyManager == null)
        {
            Debug.LogError("OpenrouterKeyManager not found in scene.");
            return false;
        }
        return keyManager.IsApiKeyAvailable();
    }
}