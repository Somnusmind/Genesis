using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Evo.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Openrouter;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum OpenRouterReasoningMode
{
    Effort,
    MaxTokens,
}

public class SceneManager_ConfigCreator : MonoBehaviour
{
    private enum SceneState
    {
        Prompting,
        Response,
    }

    // UI Window Management
    public GameObject CanvasGroupManager;
    public Tabs tabs;
    public ModalWindow modalWindowSaveDialog;

    // Window 1: Prompting UI
    public TMP_InputField userPromptInput;
    public TMP_InputField mainModelInput;

    // Window 2: Response UI
    public TMP_InputField responseDisplayField;

    // Save Dialog UI
    public TMP_InputField configNameInput_SaveDialog;

    // General UI
    public GameObject loadingIndicator;
    public TMP_Text systemPromptStatusText;
    public TMP_Text stepIndicatorText;
    public string sceneNameMenu = "MainMenuScene";
    public GameObject advanceStateButton;

    // API Settings
    [TextArea(5, 15)]
    public string yamlGenerationSystemPrompt = "";

    [TextArea(3, 10)]
    public string optimizationSystemPrompt = "";

    // OpenRouter parameters
    public string currentOpenRouterMainModel = "z-ai/glm-5";
    public bool currentOpenRouterReasoningEnabled = true;
    public float currentOpenRouterTemperature = 1.0f;

    public OpenRouterReasoningMode currentOpenRouterReasoningMode = OpenRouterReasoningMode.Effort;

    [Tooltip("Used when ReasoningMode = Effort. Valid values: low, medium, high")]
    public string currentOpenRouterReasoningEffort = "medium";

    [Tooltip("Used when ReasoningMode = MaxTokens. Sets the max reasoning token budget.")]
    public int currentOpenRouterReasoningMaxTokens = 2000;

    // Internal state
    private SceneState currentState = SceneState.Prompting;
    private bool isLocked = false;
    private bool isResponseComplete = false;
    private GlobalErrorHandling globalErrorHandling;

    // API managers
    private OpenrouterKeyManager openrouterKeyManager;

    // API credentials
    private string openrouterApiKey;

    // Coroutine and response tracking
    private Coroutine activeApiCallCoroutine;

    // ANTI-FREEZE / PERFORMANCE FIELDS
    private StringBuilder currentResponseTextBuilder = new StringBuilder(32768);
    private StringBuilder streamBuilder = new StringBuilder(8192);

    private float lastUIUpdateTime = 0f;
    private float lastStreamProcessTime = 0f;

    private const float MIN_UI_UPDATE_INTERVAL = 0.16f;
    private const float MIN_PROCESS_INTERVAL = 0.028f;

    // REASONING DISPLAY FIELDS
    private StringBuilder reasoningTextBuilder = new StringBuilder(4096);
    private bool isReasoningPhase = false;

    // If true, reasoning tokens are shown in the response field before the main content arrives.
    // If false, the field remains empty (or shows only content) like the original behaviour.
    public bool showReasoning = true;

    // Save dialog state
    private string configurationNamePendingSave;
    private bool isSaveDialogOpened = false;
    private bool saveActionConfirmed = false;

    // System prompt loading
    private string loadedSystemPrompt = "";

    // OpenRouter Headers
    private const string HTTP_REFERER = "https://github.com/Somnusmind";
    private const string X_TITLE = "Genesis";

    void Awake()
    {
        globalErrorHandling = FindAnyObjectByType<GlobalErrorHandling>();
        openrouterKeyManager = FindAnyObjectByType<OpenrouterKeyManager>();

        if (globalErrorHandling == null)
            Debug.LogError("GlobalErrorHandling not found in scene!");
    }

    void Start()
    {
        LoadSystemPrompt();
        InitializeUIValues();
        if (CanvasGroupManager != null)
        {
            CanvasGroupManager.SetActive(true);
        }
        SwitchToPromptingWindow(true);
        if (advanceStateButton != null)
            advanceStateButton.SetActive(true);
    }

    private void LoadSystemPrompt()
    {
        try
        {
            string systemPromptFileName = "SystemPrompt.md";

            string folderName = $"SystemPrompts_v{Application.version}";
            string systemPromptPath = Path.Combine(
                Application.persistentDataPath,
                folderName,
                systemPromptFileName
            );

            if (File.Exists(systemPromptPath))
            {
                loadedSystemPrompt = File.ReadAllText(systemPromptPath);
                yamlGenerationSystemPrompt = loadedSystemPrompt;

                if (systemPromptStatusText != null)
                {
                    systemPromptStatusText.text =
                        $"SystemPrompt loaded: v{Application.version} ({systemPromptFileName})";
                }

                Debug.Log($"System prompt loaded successfully from: {systemPromptPath}");
            }
            else
            {
                loadedSystemPrompt = "";
                yamlGenerationSystemPrompt = "";

                if (systemPromptStatusText != null)
                {
                    systemPromptStatusText.text =
                        $"SystemPrompt NOT FOUND for v{Application.version}";
                }
                Debug.LogWarning($"System prompt file not found at {systemPromptPath}");
            }
        }
        catch (System.Exception ex)
        {
            loadedSystemPrompt = "";
            yamlGenerationSystemPrompt = "";
            Debug.LogError($"Error loading system prompt: {ex.Message}");
        }
    }

    private void InitializeUIValues()
    {
        if (mainModelInput != null)
            mainModelInput.text = currentOpenRouterMainModel;
    }

    private void SwitchToPromptingWindow(bool initial = false)
    {
        currentResponseTextBuilder.Clear();
        lastUIUpdateTime = 0f;
        lastStreamProcessTime = 0f;
        streamBuilder.Clear();
        reasoningTextBuilder.Clear();
        isReasoningPhase = false;

        if (initial)
        {
            currentState = SceneState.Prompting;
            if (stepIndicatorText != null)
                stepIndicatorText.text = "";
            isLocked = false;
            isResponseComplete = false;
            if (advanceStateButton != null)
                advanceStateButton.SetActive(true);
        }
        else
        {
            currentState = SceneState.Prompting;
            tabs.OpenFirstTab();
            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);
            if (stepIndicatorText != null)
                stepIndicatorText.text = "";
            isLocked = false;
            isResponseComplete = false;
            if (advanceStateButton != null)
                advanceStateButton.SetActive(true);
        }
    }

    private void SwitchToResponseWindow()
    {
        currentState = SceneState.Response;
        tabs.OpenTab(1);
        if (loadingIndicator != null)
            loadingIndicator.SetActive(true);
        UpdateStepIndicator();
        isLocked = true;
        isResponseComplete = false;
        if (advanceStateButton != null)
            advanceStateButton.SetActive(false);
    }

    public void AdvanceState()
    {
        switch (currentState)
        {
            case SceneState.Prompting:
                RequestGenerateYaml();
                break;
            case SceneState.Response:
                if (isResponseComplete)
                {
                    TriggerSaveYamlConfig();
                }
                else
                {
                    Debug.Log(
                        "ConfigCreator Info: Response is not completed yet. Please wait for the generation to finish."
                    );
                    if (globalErrorHandling != null)
                    {
                        globalErrorHandling.CallGlobalErrorWithConfirmation(
                            "Response is not completed yet. Please wait for the generation to finish.",
                            null
                        );
                    }
                }
                break;
        }
    }

    public void GoBackState()
    {
        switch (currentState)
        {
            case SceneState.Response:
                GoBackToPromptingState();
                break;
            case SceneState.Prompting:
                GoToMenuScene();
                break;
        }
    }

    private string GetYamlGenerationSystemPrompt()
    {
        string modelName = GetCurrentMainModel();

        string modelIntro =
            $@"You are an LLM called ""{modelName}"".

When generating the YAML configuration, you MUST include your model name in the metaDescription field. 
The metaDescription should end with: (Created by ""{modelName}"")

Example format for metaDescription:
metaDescription: |
  [Your description of what the configuration does]

This attribution is REQUIRED and must be included in every configuration you generate.

";

        string basePrompt = loadedSystemPrompt;

        string injectedPrompt = modelIntro + basePrompt;

        Debug.Log($"=== YAML Generation System Prompt ===");
        Debug.Log($"API Provider: OpenRouter");
        Debug.Log($"Raw Model Name: {modelName}");
        Debug.Log($"System Prompt Loaded: {!string.IsNullOrEmpty(loadedSystemPrompt)}");
        Debug.Log($"=====================================");

        return injectedPrompt;
    }

    private void UpdateStepIndicator()
    {
        if (stepIndicatorText != null)
        {
            if (isResponseComplete)
            {
                stepIndicatorText.text = "Finished";
            }
            else
            {
                bool reasoningActive = currentOpenRouterReasoningEnabled;
                bool hasContent = currentResponseTextBuilder.Length > 0;

                if (reasoningActive && !hasContent)
                {
                    stepIndicatorText.text = "Model is reasoning...";
                }
                else if (!hasContent)
                {
                    stepIndicatorText.text = "Waiting for Response...";
                }
                else
                {
                    stepIndicatorText.text = "Generating Response...";
                }
            }
        }
    }

    private string GetCurrentMainModel()
    {
        return currentOpenRouterMainModel;
    }

    public void RequestGenerateYaml()
    {
        if (isLocked)
        {
            Debug.Log("ConfigCreator Info: Operation already in progress. Please wait.");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    "Operation already in progress. Please wait.",
                    null
                );
            }
            return;
        }

        LoadSystemPrompt();

        if (string.IsNullOrEmpty(loadedSystemPrompt))
        {
            string errorMsg =
                $"System Prompt file is missing or failed to load.\n\nExpected file: SystemPrompt.md\n\nPlease ensure the file exists in your SystemPrompts directory.";

            Debug.LogError($"ConfigCreator: {errorMsg}");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(errorMsg, null);
            }
            return;
        }

        // API key is guaranteed to be available because SceneManager_SessionWizard
        // checks it before loading this scene. LoadCredentials simply populates
        // the in-memory field from the encrypted file.
        if (!LoadCredentials())
            return;

        if (userPromptInput == null || string.IsNullOrWhiteSpace(userPromptInput.text))
        {
            Debug.LogError("ConfigCreator: User prompt cannot be empty.");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    "User prompt cannot be empty.",
                    null
                );
            }
            return;
        }

        currentResponseTextBuilder.Clear();

        SwitchToResponseWindow();
        GenerateYamlConfig();
    }

    public void RequestOptimizePrompt()
    {
        if (isLocked)
        {
            Debug.Log("ConfigCreator Info: Operation already in progress. Please wait.");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    "Operation already in progress. Please wait.",
                    null
                );
            }
            return;
        }

        // API key is guaranteed to be available because SceneManager_SessionWizard
        // checks it before loading this scene. LoadCredentials simply populates
        // the in-memory field from the encrypted file.
        if (!LoadCredentials())
            return;

        if (userPromptInput == null || string.IsNullOrWhiteSpace(userPromptInput.text))
        {
            Debug.LogError("ConfigCreator: User prompt cannot be empty to optimize.");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    "User prompt cannot be empty to optimize.",
                    null
                );
            }
            return;
        }

        if (loadingIndicator != null)
            loadingIndicator.SetActive(true);
        if (advanceStateButton != null)
            advanceStateButton.SetActive(false);
        OptimizeUserPrompt();
    }

    // Loads the OpenRouter API key from the encrypted credentials file into memory.
    // Returns true if the key was loaded successfully, false otherwise.
    // No user-facing error dialog is shown here because the API key availability
    // is already verified by SceneManager_SessionWizard before this scene is loaded.
    private bool LoadCredentials()
    {
        if (string.IsNullOrEmpty(openrouterApiKey))
        {
            if (openrouterKeyManager == null)
            {
                openrouterKeyManager = FindAnyObjectByType<OpenrouterKeyManager>();
            }

            if (openrouterKeyManager != null)
            {
                string filePath = openrouterKeyManager.GetOpenrouterKeyPath();
                if (File.Exists(filePath))
                {
                    try
                    {
                        string json = File.ReadAllText(filePath);
                        OpenrouterAuth auth = JsonConvert.DeserializeObject<OpenrouterAuth>(json);
                        if (auth != null && !string.IsNullOrEmpty(auth.ApiKey))
                        {
                            string hardwareId = CryptoHelper.GetHardwareId();
                            byte[] key = CryptoHelper.GenerateAesKey(hardwareId);
                            openrouterApiKey = CryptoHelper.DecryptApiKey(auth.ApiKey, key);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"ConfigCreator: Error loading API key: {ex.Message}");
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(openrouterApiKey))
        {
            Debug.LogError("ConfigCreator: Failed to load OpenRouter API key.");
            return false;
        }
        return true;
    }

    public void GoBackToPromptingState()
    {
        if (activeApiCallCoroutine != null)
        {
            StopCoroutine(activeApiCallCoroutine);
            activeApiCallCoroutine = null;
            Debug.Log("API call aborted by user.");
        }
        ResetResponseField();
        SwitchToPromptingWindow();
        if (advanceStateButton != null)
            advanceStateButton.SetActive(true);
    }

    public void GoToMenuScene()
    {
        if (isLocked && activeApiCallCoroutine != null)
        {
            StopCoroutine(activeApiCallCoroutine);
            activeApiCallCoroutine = null;
        }
        SceneManager.LoadScene(sceneNameMenu);
    }

    public void SetMainModel(string model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            currentOpenRouterMainModel = model;
        }
    }

    public void SetReasoningEnabled(bool value)
    {
        currentOpenRouterReasoningEnabled = value;
        Debug.Log($"OpenRouter Reasoning Enabled set to: {value}");
    }

    public void ToggleReasoning(bool value)
    {
        currentOpenRouterReasoningEnabled = value;
        Debug.Log($"Global Reasoning toggled to: {value}");
    }

    public void ToggleReasoningVisible(bool value)
    {
        showReasoning = value;
        Debug.Log($"Reasoning visibility toggled to: {value}");

        if (!showReasoning)
        {
            if (isReasoningPhase)
            {
                isReasoningPhase = false;
                reasoningTextBuilder.Clear();

                if (responseDisplayField != null)
                {
                    if (currentResponseTextBuilder.Length > 0)
                    {
                        responseDisplayField.text = currentResponseTextBuilder.ToString();
                    }
                    else
                    {
                        responseDisplayField.text = "";
                    }
                }
            }
        }
    }

    public void SetOpenRouterReasoningMode(int mode)
    {
        currentOpenRouterReasoningMode = (OpenRouterReasoningMode)mode;
        Debug.Log($"OpenRouter Reasoning Mode set to: {currentOpenRouterReasoningMode}");
    }

    public void SetOpenRouterReasoningEffort(string effort)
    {
        currentOpenRouterReasoningEffort = effort;
        Debug.Log($"OpenRouter Reasoning Effort set to: {effort}");
    }

    public void SetOpenRouterReasoningMaxTokens(int maxTokens)
    {
        currentOpenRouterReasoningMaxTokens = maxTokens;
        Debug.Log($"OpenRouter Reasoning MaxTokens set to: {maxTokens}");
    }

    private void GenerateYamlConfig()
    {
        ResetResponseField();
        currentResponseTextBuilder.Clear();

        string promptToUse = userPromptInput.text;

        List<ChatMessage> messages = new List<ChatMessage>
        {
            new ChatMessage { role = "system", content = GetYamlGenerationSystemPrompt() },
            new ChatMessage { role = "user", content = promptToUse },
        };

        activeApiCallCoroutine = StartCoroutine(
            CallChatCompletionAPI(
                currentOpenRouterMainModel,
                messages,
                true,
                null,
                false,
                (chunk) =>
                {
                    currentResponseTextBuilder.Append(chunk);

                    if (Time.time - lastUIUpdateTime >= MIN_UI_UPDATE_INTERVAL)
                    {
                        if (responseDisplayField != null)
                        {
                            responseDisplayField.text = currentResponseTextBuilder.ToString();
                            ActivateResponseField();
                            ScrollToBottom();
                        }
                        lastUIUpdateTime = Time.time;
                    }
                    UpdateStepIndicator();
                },
                () =>
                {
                    isLocked = false;
                    isResponseComplete = true;
                    if (loadingIndicator != null)
                        loadingIndicator.SetActive(false);
                    if (stepIndicatorText != null)
                        stepIndicatorText.text = "Finished";

                    if (responseDisplayField != null)
                        responseDisplayField.text = currentResponseTextBuilder.ToString();

                    activeApiCallCoroutine = null;
                    ScrollToBottom();
                    if (advanceStateButton != null)
                        advanceStateButton.SetActive(true);
                    Debug.Log("YAML generation complete.");
                },
                (errorMsg) =>
                {
                    Debug.LogError(
                        $"ConfigCreator Critical Error: YAML Generation Error: {errorMsg}"
                    );
                    activeApiCallCoroutine = null;

                    if (globalErrorHandling != null)
                    {
                        globalErrorHandling.CallGlobalErrorWithConfirmation(
                            $"YAML Generation Error: {errorMsg}",
                            GoBackToPromptingState
                        );
                    }
                    else
                    {
                        GoBackToPromptingState();
                    }
                }
            )
        );
    }

    private void OptimizeUserPrompt()
    {
        isLocked = true;
        if (loadingIndicator != null)
            loadingIndicator.SetActive(true);

        string originalPrompt = userPromptInput.text;
        currentResponseTextBuilder.Clear();

        List<ChatMessage> messages = new List<ChatMessage>
        {
            new ChatMessage { role = "system", content = optimizationSystemPrompt },
            new ChatMessage { role = "user", content = originalPrompt },
        };

        activeApiCallCoroutine = StartCoroutine(
            CallChatCompletionAPI(
                currentOpenRouterMainModel,
                messages,
                true,
                false,
                true,
                (chunk) =>
                {
                    currentResponseTextBuilder.Append(chunk);
                },
                () =>
                {
                    if (userPromptInput != null)
                        userPromptInput.text = currentResponseTextBuilder.ToString();
                    isLocked = false;
                    if (loadingIndicator != null)
                        loadingIndicator.SetActive(false);
                    activeApiCallCoroutine = null;
                    if (advanceStateButton != null)
                        advanceStateButton.SetActive(true);
                    Debug.Log("Prompt optimization complete.");
                },
                (errorMsg) =>
                {
                    Debug.LogError($"ConfigCreator: Prompt Optimization Error: {errorMsg}");
                    if (globalErrorHandling != null)
                    {
                        globalErrorHandling.CallGlobalErrorWithConfirmation(
                            $"Prompt Optimization Error: {errorMsg}",
                            null
                        );
                    }
                    if (userPromptInput != null)
                        userPromptInput.text = originalPrompt;
                    isLocked = false;
                    if (loadingIndicator != null)
                        loadingIndicator.SetActive(false);
                    activeApiCallCoroutine = null;
                    if (advanceStateButton != null)
                        advanceStateButton.SetActive(true);
                }
            )
        );
    }

    private IEnumerator CallChatCompletionAPI(
        string model,
        List<ChatMessage> messages,
        bool stream,
        bool? reasoningEnabledOverride,
        bool isOptimizationMode,
        System.Action<string> onChunkReceived,
        System.Action onComplete,
        System.Action<string> onError
    )
    {
        string url = "https://openrouter.ai/api/v1/chat/completions";

        ChatRequest requestPayload = new ChatRequest
        {
            model = model,
            messages = messages,
            stream = stream,
        };

        if (
            isOptimizationMode
            && reasoningEnabledOverride.HasValue
            && reasoningEnabledOverride.Value == false
        )
        {
            requestPayload.reasoning = new ReasoningSettings { enabled = false };
        }
        else if (currentOpenRouterReasoningEnabled)
        {
            if (currentOpenRouterReasoningMode == OpenRouterReasoningMode.MaxTokens)
            {
                requestPayload.reasoning = new ReasoningSettings
                {
                    max_tokens = currentOpenRouterReasoningMaxTokens,
                    exclude = false,
                };
            }
            else
            {
                requestPayload.reasoning = new ReasoningSettings
                {
                    effort = currentOpenRouterReasoningEffort,
                    exclude = false,
                };
            }
        }

        string jsonPayload = JsonConvert.SerializeObject(
            requestPayload,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
        );

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", $"Bearer {openrouterApiKey}");
            www.SetRequestHeader("HTTP-Referer", HTTP_REFERER);
            www.SetRequestHeader("X-Title", X_TITLE);

            int lastProcessedLength = 0;
            var asyncOp = www.SendWebRequest();

            while (!asyncOp.isDone)
            {
                if (
                    www.result == UnityWebRequest.Result.ConnectionError
                    || www.result == UnityWebRequest.Result.ProtocolError
                    || www.result == UnityWebRequest.Result.DataProcessingError
                )
                {
                    string errorResponseText = www.downloadHandler?.text;
                    try
                    {
                        ErrorPayload errorPayload = JsonConvert.DeserializeObject<ErrorPayload>(
                            errorResponseText
                        );
                        if (errorPayload != null && errorPayload.error != null)
                        {
                            onError?.Invoke(
                                $"Network Error (Code {www.responseCode} / API Code {errorPayload.error.code}): {errorPayload.error.message} - Raw: {www.error}"
                            );
                        }
                        else
                        {
                            onError?.Invoke(
                                $"Network Error (Code {www.responseCode}): {www.error} - Response: {errorResponseText}"
                            );
                        }
                    }
                    catch
                    {
                        onError?.Invoke(
                            $"Network Error (Code {www.responseCode}): {www.error} - Response: {errorResponseText}"
                        );
                    }
                    yield break;
                }

                if (stream && www.downloadHandler.data != null)
                {
                    if (Time.time - lastStreamProcessTime >= MIN_PROCESS_INTERVAL)
                    {
                        string currentData = www.downloadHandler.text;
                        if (currentData.Length > lastProcessedLength)
                        {
                            string newData = currentData.Substring(lastProcessedLength);
                            ProcessStreamData(newData, onChunkReceived, onError);
                            lastProcessedLength = currentData.Length;
                            lastStreamProcessTime = Time.time;
                        }
                    }
                }
                yield return null;
            }

            if (www.result == UnityWebRequest.Result.Success)
            {
                if (stream)
                {
                    string finalData = www.downloadHandler.text;
                    if (finalData.Length > lastProcessedLength)
                    {
                        ProcessStreamData(
                            finalData.Substring(lastProcessedLength),
                            onChunkReceived,
                            onError
                        );
                    }
                }
                else
                {
                    try
                    {
                        var fullResponse = JsonConvert.DeserializeObject<ChatResponse>(
                            www.downloadHandler.text
                        );
                        if (fullResponse.error != null)
                        {
                            onError?.Invoke(
                                $"API Error (Code {fullResponse.error.code}): {fullResponse.error.message}"
                            );
                            yield break;
                        }
                        if (
                            fullResponse.choices != null
                            && fullResponse.choices.Count > 0
                            && fullResponse.choices[0].message != null
                        )
                        {
                            onChunkReceived?.Invoke(fullResponse.choices[0].message.content ?? "");
                        }
                        else
                        {
                            Debug.LogWarning(
                                "Non-streaming response structure unexpected or choices/message is null."
                            );
                        }
                    }
                    catch (System.Exception ex)
                    {
                        onError?.Invoke(
                            $"Error parsing full response: {ex.Message} - Response: {www.downloadHandler.text}"
                        );
                        yield break;
                    }
                }
                onComplete?.Invoke();
            }
            else
            {
                string errorResponseText = www.downloadHandler?.text;
                try
                {
                    ErrorPayload errorPayload = JsonConvert.DeserializeObject<ErrorPayload>(
                        errorResponseText
                    );
                    if (errorPayload != null && errorPayload.error != null)
                    {
                        onError?.Invoke(
                            $"Error (HTTP {www.responseCode} / API Code {errorPayload.error.code}): {errorPayload.error.message} - Raw: {www.error}"
                        );
                    }
                    else
                    {
                        onError?.Invoke(
                            $"Error (Code {www.responseCode}): {www.error} - Response: {errorResponseText}"
                        );
                    }
                }
                catch
                {
                    onError?.Invoke(
                        $"Error (Code {www.responseCode}): {www.error} - Response: {errorResponseText}"
                    );
                }
            }
        }
    }

    private void ProcessStreamData(
        string newData,
        System.Action<string> onChunkReceived,
        System.Action<string> onError
    )
    {
        if (Time.time - lastStreamProcessTime < MIN_PROCESS_INTERVAL)
        {
            streamBuilder.Append(newData);
            return;
        }

        lastStreamProcessTime = Time.time;
        streamBuilder.Append(newData);

        string buffer = streamBuilder.ToString();
        int processed = 0;

        while (true)
        {
            int separator = buffer.IndexOf("\n\n", processed);
            if (separator == -1)
                break;

            string line = buffer.Substring(processed, separator - processed).Trim();
            processed = separator + 2;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(":"))
                continue;
            if (line.StartsWith("data: "))
                line = line.Substring(6).Trim();
            if (line == "[DONE]")
                continue;

            try
            {
                var response = JsonConvert.DeserializeObject<ChatResponse>(line);
                if (response?.choices?.Count > 0)
                {
                    var delta = response.choices[0].delta;

                    // Handle reasoning tokens
                    if (!string.IsNullOrEmpty(delta?.reasoning))
                    {
                        if (showReasoning)
                        {
                            if (currentResponseTextBuilder.Length == 0)
                            {
                                if (!isReasoningPhase)
                                {
                                    isReasoningPhase = true;
                                    reasoningTextBuilder.Clear();
                                }
                                reasoningTextBuilder.Append(delta.reasoning);

                                if (
                                    responseDisplayField != null
                                    && Time.time - lastUIUpdateTime >= MIN_UI_UPDATE_INTERVAL
                                )
                                {
                                    responseDisplayField.text = reasoningTextBuilder.ToString();
                                    ActivateResponseField();
                                    ScrollToBottom();
                                    lastUIUpdateTime = Time.time;
                                }
                            }
                        }
                        // If showReasoning is false, we receive reasoning but do not display it.
                    }

                    // Handle content tokens
                    if (!string.IsNullOrEmpty(delta?.content))
                    {
                        if (isReasoningPhase)
                        {
                            isReasoningPhase = false;
                            reasoningTextBuilder.Clear();
                            currentResponseTextBuilder.Clear();

                            if (responseDisplayField != null && showReasoning)
                            {
                                responseDisplayField.text = "";
                            }
                        }

                        onChunkReceived?.Invoke(delta.content);
                    }
                }
            }
            catch { }
        }

        if (processed > 0)
            streamBuilder.Remove(0, processed);
    }

    private void ResetResponseField()
    {
        currentResponseTextBuilder.Clear();
        if (responseDisplayField != null)
        {
            responseDisplayField.text = "";
        }
        streamBuilder.Clear();
        reasoningTextBuilder.Clear();
        isReasoningPhase = false;
    }

    private void ActivateResponseField()
    {
        if (responseDisplayField != null)
        {
            responseDisplayField.interactable = true;
            EventSystem.current.SetSelectedGameObject(responseDisplayField.gameObject);
            responseDisplayField.ActivateInputField();
            responseDisplayField.readOnly = false;
        }
    }

    private void ScrollToBottom()
    {
        if (responseDisplayField != null)
        {
            StartCoroutine(ScrollToBottomDeferred());
        }
    }

    private IEnumerator ScrollToBottomDeferred()
    {
        yield return null;
        yield return null;

        ActivateResponseField();

        responseDisplayField.caretPosition = responseDisplayField.text.Length;
        responseDisplayField.selectionAnchorPosition = responseDisplayField.text.Length;
        responseDisplayField.selectionFocusPosition = responseDisplayField.text.Length;
        responseDisplayField.ForceLabelUpdate();

        yield return new WaitForSeconds(0.1f);

        responseDisplayField.caretPosition = responseDisplayField.text.Length;
        responseDisplayField.selectionAnchorPosition = responseDisplayField.text.Length;
        responseDisplayField.selectionFocusPosition = responseDisplayField.text.Length;
        responseDisplayField.ForceLabelUpdate();
    }

    public void TriggerSaveYamlConfig()
    {
        if (responseDisplayField == null || string.IsNullOrWhiteSpace(responseDisplayField.text))
        {
            Debug.LogError("ConfigCreator: There is no configuration content to save.");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    "There is no configuration content to save.",
                    null
                );
            }
            return;
        }
        if (configNameInput_SaveDialog != null)
            configNameInput_SaveDialog.text = SanitizeFolderName("MyNewConfig");

        StartCoroutine(OpenSaveDialogAndWaitForClose());
    }

    private IEnumerator OpenSaveDialogAndWaitForClose()
    {
        modalWindowSaveDialog.Open();
        isSaveDialogOpened = true;
        saveActionConfirmed = false;

        while (isSaveDialogOpened)
        {
            yield return null;
        }

        if (saveActionConfirmed)
        {
            PerformSaveConfiguration();
        }
        else
        {
            Debug.Log("Save operation cancelled by user.");
        }
    }

    public void ConfirmSaveConfig()
    {
        Debug.Log("ConfirmSaveConfig");
        if (
            configNameInput_SaveDialog == null
            || string.IsNullOrWhiteSpace(configNameInput_SaveDialog.text)
        )
            {
                Debug.LogError("ConfigCreator: Configuration name cannot be empty in save dialog.");
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        "Configuration name cannot be empty in save dialog.",
                        null
                    );
                }
                return;
            }
        configurationNamePendingSave = configNameInput_SaveDialog.text;
        saveActionConfirmed = true;
        isSaveDialogOpened = false;
        modalWindowSaveDialog.Close();
    }

    public void CancelSaveConfig()
    {
        saveActionConfirmed = false;
        isSaveDialogOpened = false;
        modalWindowSaveDialog.Close();
    }

    private void PerformSaveConfiguration()
    {
        string configContentToSave = responseDisplayField.text;
        configContentToSave = StripCodeFences(configContentToSave);

        bool isValid = YamlValidator.ValidateYamlSyntax(
            configContentToSave,
            out string errorMessage
        );
        if (!isValid)
        {
            Debug.LogError($"ConfigCreator: Invalid YAML Syntax.\n{errorMessage}");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    $"Invalid YAML Syntax. Cannot save.\n{errorMessage}",
                    null
                );
            }
            return;
        }

        string sanitizedName = SanitizeFolderName(configurationNamePendingSave);

        string targetFolder = Path.Combine(
            Application.persistentDataPath,
            "MyContent",
            sanitizedName
        );
        string configFilePath = Path.Combine(targetFolder, "config.yml");

        try
        {
            Directory.CreateDirectory(targetFolder);

            string cacheFolderPath = Path.Combine(targetFolder, ".cache");
            Directory.CreateDirectory(cacheFolderPath);

            string mediaFolderPath = Path.Combine(targetFolder, "media");
            Directory.CreateDirectory(mediaFolderPath);

            ConfigPath.UpdateBaseFolderPath(Path.Combine("MyContent", sanitizedName));
            ConfigManager.Instance.SaveConfigurationPlainText(configContentToSave);

            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    $"Configuration '{sanitizedName}' saved successfully!\n\nRequired folders created:\n- .cache\n- media",
                    () =>
                    {
                        SwitchToPromptingWindow();
                        if (responseDisplayField != null)
                            responseDisplayField.text = "";
                    }
                );
            }
            else
            {
                SwitchToPromptingWindow();
                if (responseDisplayField != null)
                    responseDisplayField.text = "";
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ConfigCreator: Save Failed: {e.Message}");
            if (globalErrorHandling != null)
            {
                globalErrorHandling.CallGlobalErrorWithConfirmation(
                    $"Save Failed: {e.Message}",
                    null
                );
            }
        }
    }

    private string StripCodeFences(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        content = content.Trim();

        if (content.StartsWith("```yaml", System.StringComparison.OrdinalIgnoreCase))
        {
            content = content.Substring(7);
        }
        else if (content.StartsWith("```"))
        {
            content = content.Substring(3);
        }

        if (content.EndsWith("```"))
        {
            content = content.Substring(0, content.Length - 3);
        }

        content = content.Trim();

        Debug.Log("Code fences stripped from YAML content");
        return content;
    }

    private string SanitizeFolderName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "UntitledConfig";
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "UntitledConfig" : cleaned;
    }

    void OnDestroy()
    {
        if (activeApiCallCoroutine != null)
        {
            StopCoroutine(activeApiCallCoroutine);
        }
    }

    [System.Serializable]
    private class ChatMessage
    {
        public string role;
        public object content;
        public string name;
    }

    [System.Serializable]
    public class ReasoningSettings
    {
        public string effort;
        public bool? enabled;
        public bool exclude;
        public int? max_tokens;
    }

    [System.Serializable]
    private class ChatRequest
    {
        public string model;
        public List<ChatMessage> messages;
        public bool stream = true;
        public float? temperature;
        public int? max_tokens;
        public ReasoningSettings reasoning;
    }

    [System.Serializable]
    public class ErrorDetails
    {
        public int code;
        public string message;
        public JObject metadata;
    }

    [System.Serializable]
    public class ErrorPayload
    {
        public ErrorDetails error;
    }

    [System.Serializable]
    private class ChatResponse
    {
        public string id;
        public List<Choice> choices;
        public long created;
        public string model;

        [JsonProperty("object")]
        public string ObjectType;
        public Usage usage;
        public string system_fingerprint;
        public ErrorDetails error;
    }

    [System.Serializable]
    private class Choice
    {
        public string finish_reason;
        public string native_finish_reason;
        public int index;
        public Message message;
        public Message delta;
        public ErrorDetails error;
    }

    [System.Serializable]
    private class Message
    {
        public string role;
        public string content;
        public string reasoning;
    }

    [System.Serializable]
    private class Usage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }
}