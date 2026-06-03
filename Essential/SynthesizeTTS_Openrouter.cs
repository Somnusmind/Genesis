using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Openrouter;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class SynthesizeTTS_OpenRouter : MonoBehaviour
{
    private enum SegmentType
    {
        Speech,
        CutAudio,
        CutSilence,
    }

    private struct Segment
    {
        public SegmentType Type;
        public string Speech;
        public string AudioUrl;
        public float SilenceDuration;
    }

    private readonly string apiUrl = "https://openrouter.ai/api/v1/audio/speech";

    // TTS Configuration
    [SerializeField]
    private string model = "openai/gpt-4o-mini-tts-2025-12-15";

    [SerializeField]
    private string voice = "alloy";

    // NOTE: speed and responseFormat have been removed from user configuration.
    // The API always receives response_format: "mp3" and no speed parameter
    // (defaults to 1.0 on the provider side). This guarantees maximum audio
    // quality by eliminating lossy re-encoding and sample-rate mismatches.
    // Unity's MP3 decoder auto-detects the native sample rate from the
    // MP3 frame headers, so the AudioClip.frequency is always correct
    // regardless of which provider or model generated the audio.

    // Injected Audio
    [Tooltip("If true, only allow https:// URLs for cut_audio.")]
    [SerializeField]
    private bool cutAudioRequireHttps = true;

    [Tooltip("Optional allowlist for hostnames. If empty, any host is allowed.")]
    [SerializeField]
    private string[] cutAudioAllowedHosts = Array.Empty<string>();

    [Tooltip("Timeout in seconds for downloading injected audio clips.")]
    [SerializeField]
    private int cutAudioDownloadTimeoutSeconds = 60;

    [Tooltip("Apply a tiny fade-in/out at clip boundaries to reduce clicks.")]
    [SerializeField]
    private bool useBoundaryDeClick = true;

    [Tooltip("De-click duration in seconds (typical: 0.005 to 0.02).")]
    [SerializeField]
    private float boundaryDeClickSeconds = 0.01f;

    // Normalization
    [Tooltip(
        "Normalize each speech segment individually to -18 LUFS before combining. Recommended for consistent voice levels."
    )]
    [SerializeField]
    private bool normalizeSpeechSegmentsIndividually = false;

    // Debug Options
    [SerializeField]
    private bool verboseLogging = true;

    // Text Settings for All Stages
    [SerializeField]
    private string stage1TTS;

    [SerializeField]
    private string stage2TTS;

    [SerializeField]
    private string stage3TTS;

    [SerializeField]
    private string stage4TTS;

    [SerializeField]
    private string stage5TTS;

    [SerializeField]
    private string mantraTTS;

    // References
    public AudioCoordinator audioCoordinator;
    public SceneManager_SessionWizard sceneManager;

    // Private fields
    private string apiKey = "";
    private bool[] startAutomatic = new bool[6];
    private bool[] isAudioLoaded = new bool[6];
    private GlobalErrorHandling globalErrorHandling;
    private OpenrouterKeyManager openrouterKeyManager;

    // Synthesis event and timeout handling
    public event Action OnSynthesisComplete;
    public event Action OnCacheSaveComplete;
    private const int MAX_CHARS = 4096;
    private const float SYNTHESIS_TIMEOUT = 60f;
    private Dictionary<string, Coroutine> timeoutCoroutines = new Dictionary<string, Coroutine>();
    private int synthesisCompletedCount = 0;
    private readonly int totalSynthesisCount = 6;
    private bool synthesisCompletionInvoked = false;

    // Regex Tags
    private static readonly Regex cutTagRegex = new Regex(@"<cut>", RegexOptions.Compiled);
    private static readonly Regex cutAudioTagRegex = new Regex(
        @"<cut_audio=(?<url>[^>\s]+)>",
        RegexOptions.Compiled
    );
    private static readonly Regex cutSilenceTagRegex = new Regex(
        @"<cut_silence=(?<duration>[0-9]+\.?[0-9]*)>",
        RegexOptions.Compiled
    );

    private void Awake()
    {
        globalErrorHandling = FindAnyObjectByType<GlobalErrorHandling>();
        openrouterKeyManager = FindAnyObjectByType<OpenrouterKeyManager>();
        sceneManager = FindAnyObjectByType<SceneManager_SessionWizard>();
    }

    public void LoadConfig()
    {
        ConfigManager configManager = new ConfigManager();
        Config config = configManager.LoadConfiguration();

        LoadApiKey();

        model = config.ttsModel;
        voice = config.ttsVoice;

        // NOTE: config.ttsSpeed and config.ttsResponseFormat are intentionally
        // no longer consumed here. Speed is omitted from API requests (provider
        // defaults to 1.0) and response_format is hardcoded to "mp3" to
        // guarantee Unity can auto-detect the native sample rate from the
        // MP3 frame headers, ensuring maximum audio quality without any
        // manual sample-rate configuration or lossy re-encoding.

        stage1TTS = config.stage1TTS;
        stage2TTS = config.stage2TTS;
        stage3TTS = config.stage3TTS;
        stage4TTS = config.stage4TTS;
        stage5TTS = config.stage5TTS;
        mantraTTS = config.mantraTTS;

        for (int i = 0; i < totalSynthesisCount; i++)
        {
            isAudioLoaded[i] = false;
            startAutomatic[i] = true;
        }

        bool shouldSynthesize = false;
        for (int i = 0; i < startAutomatic.Length; i++)
        {
            if (startAutomatic[i])
            {
                shouldSynthesize = true;
                break;
            }
        }

        if (shouldSynthesize)
        {
            SynthesizeTextFromConfig();
        }
    }

    private string GetStageName(int index)
    {
        switch (index)
        {
            case 0:
                return "Stage1";
            case 1:
                return "Stage2";
            case 2:
                return "Stage3";
            case 3:
                return "Stage4";
            case 4:
                return "Stage5";
            case 5:
                return "Mantra/Subliminal Track";
            default:
                return "Unknown Stage";
        }
    }

    private void LoadApiKey()
    {
        try
        {
            if (openrouterKeyManager == null)
            {
                Debug.LogError("[OpenRouter TTS] OpenrouterKeyManager not found in the scene");
                return;
            }

            string filePath = openrouterKeyManager.GetOpenrouterKeyPath();

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                if (verboseLogging)
                    Debug.Log($"[OpenRouter TTS] Loaded JSON content length: {json.Length}");

                var auth = JsonConvert.DeserializeObject<OpenrouterAuth>(json);

                if (auth == null || string.IsNullOrEmpty(auth.ApiKey))
                {
                    Debug.LogError("[OpenRouter TTS] Invalid OpenRouter API key data found.");
                    return;
                }

                try
                {
                    string hardwareId = CryptoHelper.GetHardwareId();
                    byte[] key = CryptoHelper.GenerateAesKey(hardwareId);
                    string decryptedApiKey = CryptoHelper.DecryptApiKey(auth.ApiKey, key);

                    if (string.IsNullOrEmpty(decryptedApiKey))
                    {
                        throw new Exception("Decrypted OpenRouter API key is empty");
                    }

                    apiKey = decryptedApiKey;

                    string maskedKey =
                        apiKey.Substring(0, 4) + "..." + apiKey.Substring(apiKey.Length - 4);
                    if (verboseLogging)
                        Debug.Log(
                            $"[OpenRouter TTS] OpenRouter API key loaded successfully: {maskedKey}"
                        );
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[OpenRouter TTS] Failed to decrypt OpenRouter API key: {e.Message}"
                    );
                }
            }
            else
            {
                Debug.LogError($"[OpenRouter TTS] No OpenRouter API key file found at {filePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OpenRouter TTS] Error loading OpenRouter API key: {ex.Message}");
        }
    }

    public void SynthesizeTextFromConfig()
    {
        synthesisCompletedCount = 0;
        synthesisCompletionInvoked = false;

        for (int i = 0; i < totalSynthesisCount; i++)
        {
            if (!startAutomatic[i])
                continue;

            string text = i == 5 ? mantraTTS : GetStageTTS(i);

            // We proceed directly to SynthesizeText.
            // Character limits will be checked only if synthesis is required (cache miss).
            SynthesizeText(text, i);
        }
    }

    private string GetStageTTS(int index)
    {
        switch (index)
        {
            case 0:
                return stage1TTS;
            case 1:
                return stage2TTS;
            case 2:
                return stage3TTS;
            case 3:
                return stage4TTS;
            case 4:
                return stage5TTS;
            default:
                return string.Empty;
        }
    }

    public void SynthesizeText(string text, int index)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (verboseLogging)
                Debug.Log(
                    $"[OpenRouter TTS] No text given for synthesis for {GetStageName(index)}. Creating silent clip."
                );
            StartCoroutine(CreateSilentClip(index));
            return;
        }

        if (ContainsOnlySilenceTags(text))
        {
            if (verboseLogging)
                Debug.Log(
                    $"[OpenRouter TTS] Stage contains only silence tags, no TTS or download needed for {GetStageName(index)}. Processing directly."
                );
            string uniqueName = GenerateUniqueNameFromText(text);

            StartCoroutine(
                AudioCacheHelperOpenRouter.LoadAudioClip(
                    uniqueName,
                    (audioClip) =>
                    {
                        if (audioClip != null)
                        {
                            if (verboseLogging)
                                Debug.Log(
                                    $"[OpenRouter TTS] Loaded silence-only AudioClip from cache: {uniqueName}"
                                );
                            PlayAudio(audioClip, false, index);
                            isAudioLoaded[index] = true;

                            if (sceneManager != null)
                                sceneManager.CreateProgressMessage(
                                    $"Pre-Cached (Silence Only) for {GetStageName(index)}"
                                );

                            IncrementSynthesisCompleted();
                        }
                        else
                        {
                            if (verboseLogging)
                                Debug.Log(
                                    $"[OpenRouter TTS] No cache found for silence-only content, creating new clip for: {GetStageName(index)}"
                                );
                            StartCoroutine(ProcessTextAndSynthesize(text, uniqueName, index));
                        }
                    },
                    null
                )
            );

            return;
        }

        string uniqueName2 = GenerateUniqueNameFromText(text);
        if (verboseLogging)
            Debug.Log($"[OpenRouter TTS] Generated unique ID for text: {uniqueName2}");

        if (timeoutCoroutines.ContainsKey(uniqueName2))
        {
            StopCoroutine(timeoutCoroutines[uniqueName2]);
        }

        timeoutCoroutines[uniqueName2] = StartCoroutine(
            SynthesisTimeoutCoroutine(uniqueName2, index)
        );
        StartCoroutine(DelayedSynthesisProcess(text, uniqueName2, index));
    }

    private List<Segment> SplitTextIntoSegments(string text)
    {
        var segments = new List<Segment>();
        if (string.IsNullOrEmpty(text))
            return segments;

        int pos = 0;

        while (pos < text.Length)
        {
            Match mCut = cutTagRegex.Match(text, pos);
            Match mAudio = cutAudioTagRegex.Match(text, pos);
            Match mSilence = cutSilenceTagRegex.Match(text, pos);

            bool hasCut = mCut.Success;
            bool hasAudio = mAudio.Success;
            bool hasSilence = mSilence.Success;

            if (!hasCut && !hasAudio && !hasSilence)
                break;

            Match next = null;
            if (hasCut)
                next = mCut;
            if (hasAudio && (next == null || mAudio.Index < next.Index))
                next = mAudio;
            if (hasSilence && (next == null || mSilence.Index < next.Index))
                next = mSilence;

            if (next.Index > pos)
            {
                string speech = text.Substring(pos, next.Index - pos);
                if (!string.IsNullOrWhiteSpace(speech))
                {
                    segments.Add(
                        new Segment
                        {
                            Type = SegmentType.Speech,
                            Speech = speech.Trim(),
                            AudioUrl = null,
                            SilenceDuration = 0f,
                        }
                    );
                }
            }

            if (next == mAudio)
            {
                string url = mAudio.Groups["url"].Value.Trim();
                if (!string.IsNullOrEmpty(url))
                {
                    segments.Add(
                        new Segment
                        {
                            Type = SegmentType.CutAudio,
                            Speech = null,
                            AudioUrl = url,
                            SilenceDuration = 0f,
                        }
                    );
                }
            }
            else if (next == mSilence)
            {
                string durationStr = mSilence.Groups["duration"].Value.Trim();
                if (
                    float.TryParse(
                        durationStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float duration
                    )
                )
                {
                    if (duration > 0f)
                    {
                        segments.Add(
                            new Segment
                            {
                                Type = SegmentType.CutSilence,
                                Speech = null,
                                AudioUrl = null,
                                SilenceDuration = duration,
                            }
                        );
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[OpenRouter TTS] <cut_silence> duration must be positive, got: {duration}"
                        );
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"[OpenRouter TTS] Invalid <cut_silence> duration format: {durationStr}"
                    );
                }
            }

            pos = next.Index + next.Length;
        }

        if (pos < text.Length)
        {
            string speech = text.Substring(pos);
            if (!string.IsNullOrWhiteSpace(speech))
            {
                segments.Add(
                    new Segment
                    {
                        Type = SegmentType.Speech,
                        Speech = speech.Trim(),
                        AudioUrl = null,
                        SilenceDuration = 0f,
                    }
                );
            }
        }

        if (segments.Count == 0)
        {
            if (verboseLogging)
                Debug.Log(
                    "[OpenRouter TTS] Text is empty after parsing or contains only cut tags."
                );
        }
        else
        {
            if (verboseLogging)
                Debug.Log(
                    $"[OpenRouter TTS] Parsed text into {segments.Count} segments (speech + cut_audio + cut_silence)."
                );
        }

        return segments;
    }

    private bool ContainsOnlySilenceTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        List<Segment> segments = SplitTextIntoSegments(text);

        if (segments.Count == 0)
            return true;

        foreach (var seg in segments)
        {
            if (seg.Type == SegmentType.Speech || seg.Type == SegmentType.CutAudio)
            {
                return false;
            }
        }

        return true;
    }

    private string GenerateUniqueNameFromText(string text)
    {
        using (SHA256 hash = SHA256.Create())
        {
            // Cache key includes only the parameters that affect the synthesized audio.
            // Speed and response format have been removed since speed is no longer
            // sent to the API (provider defaults to 1.0) and format is always "mp3".
            string input = $"{text}|{model}|{voice}";
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }
    }

    private (bool isWithinLimit, int textLength) IsTextWithinCharLimit(string text)
    {
        int textLength = string.IsNullOrEmpty(text) ? 0 : text.Length;
        return (textLength <= MAX_CHARS, textLength);
    }

    private IEnumerator SynthesisTimeoutCoroutine(string uniqueName, int index)
    {
        yield return new WaitForSeconds(SYNTHESIS_TIMEOUT);

        Debug.LogError(
            $"[OpenRouter TTS] TTS synthesis for {uniqueName} timed out after {SYNTHESIS_TIMEOUT} seconds."
        );
        timeoutCoroutines.Remove(uniqueName);

        string stageName = GetStageName(index);
        string errorMessage =
            $"The Text-to-Speech synthesis for {stageName} timed out after {SYNTHESIS_TIMEOUT} seconds.\n\nThis may be due to network issues or problems with the OpenRouter service. Please try again later.";

        if (globalErrorHandling != null)
        {
            globalErrorHandling.CallGlobalErrorWithConfirmation(
                errorMessage,
                () =>
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                    );
                }
            );
        }

        IncrementSynthesisCompleted();
    }

    private IEnumerator DelayedSynthesisProcess(string text, string uniqueName, int index)
    {
        yield return new WaitForSeconds(0.5f);

        bool synthesisCompleted = false;

        StartCoroutine(
            AudioCacheHelperOpenRouter.LoadAudioClip(
                uniqueName,
                (audioClip) =>
                {
                    if (synthesisCompleted)
                        return;
                    synthesisCompleted = true;

                    if (timeoutCoroutines.ContainsKey(uniqueName))
                    {
                        StopCoroutine(timeoutCoroutines[uniqueName]);
                        timeoutCoroutines.Remove(uniqueName);
                    }

                    if (audioClip != null)
                    {
                        if (verboseLogging)
                            Debug.Log(
                                $"[OpenRouter TTS] Loaded AudioClip from cache: {uniqueName}"
                            );
                        PlayAudio(audioClip, false, index);
                        isAudioLoaded[index] = true;

                        if (sceneManager != null)
                        {
                            sceneManager.CreateProgressMessage(
                                $"Synthesis / Cache Import Successful for {GetStageName(index)}"
                            );
                        }
                        IncrementSynthesisCompleted();
                    }
                    else
                    {
                        if (verboseLogging)
                            Debug.Log(
                                $"[OpenRouter TTS] No AudioClip found in cache, processing text and initiating OpenRouter TTS request for: {text.Substring(0, Math.Min(50, text.Length))}..."
                            );
                        StartCoroutine(ProcessTextAndSynthesize(text, uniqueName, index));
                    }
                },
                null
            )
        );
    }

    private IEnumerator ProcessTextAndSynthesize(string text, string uniqueName, int index)
    {
        List<Segment> segments = SplitTextIntoSegments(text);

        // Check limits only when synthesis is actually happening (cache miss)
        bool allSegmentsValid = true;
        int speechCount = 0;
        foreach (var seg in segments)
        {
            if (seg.Type != SegmentType.Speech)
                continue;
            speechCount++;

            var (isWithinLimit, textLength) = IsTextWithinCharLimit(seg.Speech);
            if (!isWithinLimit)
            {
                string stageName = GetStageName(index);
                string errorMessage =
                    $"Speech segment {speechCount} for '{stageName}' exceeds the maximum character limit. "
                    + $"Current length: {textLength} characters. "
                    + $"Maximum allowed: {MAX_CHARS} characters. "
                    + $"Please shorten the text or add more <cut> / <cut_audio=...> / <cut_silence=...> tags to break it into smaller segments.";

                Debug.LogError($"[OpenRouter TTS] {errorMessage}");

                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        errorMessage,
                        () =>
                        {
                            UnityEngine.SceneManagement.SceneManager.LoadScene(
                                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                            );
                        }
                    );
                }

                allSegmentsValid = false;
                break;
            }
        }

        if (!allSegmentsValid)
        {
            IncrementSynthesisCompleted();
            yield break;
        }

        if (segments.Count == 0)
        {
            yield return StartCoroutine(CreateSilentClip(index));
            yield break;
        }

        List<AudioClip> clipPieces = new List<AudioClip>();

        // targetSampleRate determines the output format for all combined clips.
        // It starts at 0 (unset) and is locked to the native sample rate of
        // the first real audio clip loaded (TTS or cut_audio). This guarantees
        // that we always preserve the provider's native sample rate without any
        // unnecessary downsampling or hardcoded 24000 Hz ceiling. Unity's
        // MP3 decoder reads the sample rate directly from the MP3 frame
        // headers, so AudioClip.frequency is always the true native rate.
        int targetSampleRate = 0;

        foreach (var seg in segments)
        {
            if (seg.Type == SegmentType.Speech)
            {
                if (string.IsNullOrWhiteSpace(seg.Speech))
                    continue;

                AudioClip ttsClip = null;
                bool succeeded = false;

                yield return StartCoroutine(
                    SynthesizeWithOpenRouter(
                        seg.Speech,
                        (clip, success) =>
                        {
                            ttsClip = clip;
                            succeeded = success;
                        }
                    )
                );

                if (!succeeded || ttsClip == null)
                {
                    Debug.LogError("[OpenRouter TTS] Failed to synthesize a speech segment.");
                    IncrementSynthesisCompleted();
                    yield break;
                }

                // Lock targetSampleRate to the native rate of the first clip loaded.
                if (targetSampleRate == 0 && ttsClip != null)
                {
                    targetSampleRate = ttsClip.frequency;
                    if (verboseLogging)
                        Debug.Log(
                            $"[OpenRouter TTS] Locked target sample rate to {targetSampleRate}Hz from first TTS clip (provider native rate)"
                        );
                }

                var normalized = EnsureFormat(ttsClip, targetSampleRate, 1);

                if (normalizeSpeechSegmentsIndividually)
                {
                    if (verboseLogging)
                        Debug.Log(
                            $"[OpenRouter TTS] Normalizing speech segment individually to -18 LUFS (length: {normalized.length:F2}s)"
                        );
                    normalized = AudioNormalizer.NormalizeClip(normalized, -18f);
                }

                clipPieces.Add(normalized);

                if (verboseLogging)
                    Debug.Log(
                        $"[OpenRouter TTS] Added TTS segment. Length: {normalized.length:F2}s, Native rate: {ttsClip.frequency}Hz"
                    );
            }
            else if (seg.Type == SegmentType.CutAudio)
            {
                if (string.IsNullOrWhiteSpace(seg.AudioUrl))
                    continue;

                AudioClip injected = null;
                bool ok = false;

                yield return StartCoroutine(
                    DownloadCutAudioClip(
                        seg.AudioUrl,
                        (clip, success) =>
                        {
                            injected = clip;
                            ok = success;
                        }
                    )
                );

                if (!ok || injected == null)
                {
                    Debug.LogError(
                        $"[OpenRouter TTS] Failed to download injected audio: {seg.AudioUrl}"
                    );
                    IncrementSynthesisCompleted();
                    yield break;
                }

                // If this is the first clip in the session, lock the target
                // sample rate to the injected audio's native rate.
                if (targetSampleRate == 0 && injected != null)
                {
                    targetSampleRate = injected.frequency;
                    if (verboseLogging)
                        Debug.Log(
                            $"[OpenRouter TTS] Locked target sample rate to {targetSampleRate}Hz from first cut_audio clip"
                        );
                }

                var normalized = EnsureFormat(injected, targetSampleRate, 1);

                clipPieces.Add(normalized);

                if (verboseLogging)
                    Debug.Log(
                        $"[OpenRouter TTS] Inserted cut_audio clip. Length: {normalized.length:F2}s URL: {seg.AudioUrl}"
                    );
            }
            else if (seg.Type == SegmentType.CutSilence)
            {
                if (seg.SilenceDuration <= 0f)
                    continue;

                // For silence segments before any real clip has been loaded,
                // use 24000 Hz as a neutral default. Silence is
                // sample-rate-agnostic so the exact rate does not affect quality.
                if (targetSampleRate == 0)
                {
                    targetSampleRate = 24000;
                    if (verboseLogging)
                        Debug.Log(
                            $"[OpenRouter TTS] Using 24000Hz default for silence segment (no real clip loaded yet)"
                        );
                }

                AudioClip silenceClip = CreateSilenceClip(seg.SilenceDuration, targetSampleRate);
                clipPieces.Add(silenceClip);

                if (verboseLogging)
                    Debug.Log(
                        $"[OpenRouter TTS] Inserted cut_silence clip. Length: {silenceClip.length:F2}s Duration: {seg.SilenceDuration}s"
                    );
            }
        }

        if (clipPieces.Count == 0)
        {
            yield return StartCoroutine(CreateSilentClip(index));
            yield break;
        }

        AudioClip finalClip = CombineAudioClips(clipPieces, targetSampleRate, 1);

        if (finalClip == null)
        {
            Debug.LogError("[OpenRouter TTS] Failed to create combined audio clip.");
            IncrementSynthesisCompleted();
            yield break;
        }

        if (verboseLogging)
            Debug.Log(
                $"[OpenRouter TTS] Final combined clip length: {finalClip.length:F2}s, sample rate: {finalClip.frequency}Hz"
            );

        yield return null; // Spread work across frames to avoid UI hangs

        AudioCacheHelperOpenRouter.SaveAudioClip(finalClip, uniqueName);

        yield return null; // Ensure UI updates before AudioCoordinator normalization/assignment

        PlayAudio(finalClip, false, index);

        if (sceneManager != null)
            sceneManager.CreateProgressMessage(
                $"Synthesis / Cache Import Successful for {GetStageName(index)}"
            );

        if (timeoutCoroutines.ContainsKey(uniqueName))
        {
            StopCoroutine(timeoutCoroutines[uniqueName]);
            timeoutCoroutines.Remove(uniqueName);
        }

        IncrementSynthesisCompleted();
    }

    public void StopSynthesis()
    {
        Debug.Log(
            "[OpenRouter TTS] StopSynthesis called. Stopping all coroutines and resetting state."
        );
        StopAllCoroutines();

        foreach (var coroutine in timeoutCoroutines.Values)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);
        }
        timeoutCoroutines.Clear();

        synthesisCompletionInvoked = false;
        synthesisCompletedCount = 0;
    }

    private bool IsCutAudioUrlAllowed(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            Debug.LogError($"[OpenRouter TTS] cut_audio URL is invalid: {url}");
            return false;
        }

        if (cutAudioRequireHttps && uri.Scheme != Uri.UriSchemeHttps)
        {
            Debug.LogError($"[OpenRouter TTS] cut_audio URL must be https: {url}");
            return false;
        }

        if (cutAudioAllowedHosts != null && cutAudioAllowedHosts.Length > 0)
        {
            bool hostAllowed = false;
            foreach (var host in cutAudioAllowedHosts)
            {
                if (string.IsNullOrWhiteSpace(host))
                    continue;
                if (string.Equals(uri.Host, host.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    hostAllowed = true;
                    break;
                }
            }

            if (!hostAllowed)
            {
                Debug.LogError($"[OpenRouter TTS] cut_audio host not allowed: {uri.Host}");
                return false;
            }
        }

        string clean = url.Split('?')[0];
        bool okExt =
            clean.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);

        if (!okExt)
        {
            Debug.LogError(
                $"[OpenRouter TTS] cut_audio file extension not allowed (use .wav/.mp3/.ogg): {url}"
            );
            return false;
        }

        return true;
    }

    private AudioType DetermineAudioTypeFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return AudioType.WAV;

        string clean = url.Split('?')[0];

        if (clean.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            return AudioType.MPEG;
        if (clean.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return AudioType.WAV;
        if (clean.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return AudioType.OGGVORBIS;

        Debug.LogWarning($"[OpenRouter TTS] Unknown audio file format: {url}, defaulting to WAV");
        return AudioType.WAV;
    }

    private IEnumerator DownloadCutAudioClip(string url, Action<AudioClip, bool> callback)
    {
        if (!IsCutAudioUrlAllowed(url))
        {
            callback?.Invoke(null, false);
            yield break;
        }

        AudioType type = DetermineAudioTypeFromUrl(url);

        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, type))
        {
            req.timeout = Mathf.Max(1, cutAudioDownloadTimeoutSeconds);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OpenRouter TTS] cut_audio download failed: {url} :: {req.error}");
                callback?.Invoke(null, false);
                yield break;
            }

            AudioClip clip = null;
            try
            {
                clip = DownloadHandlerAudioClip.GetContent(req);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenRouter TTS] cut_audio GetContent failed: {ex.Message}");
                callback?.Invoke(null, false);
                yield break;
            }

            if (clip == null)
            {
                Debug.LogError(
                    $"[OpenRouter TTS] cut_audio downloaded but AudioClip is null: {url}"
                );
                callback?.Invoke(null, false);
                yield break;
            }

            while (clip.loadState == AudioDataLoadState.Loading)
                yield return null;

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError(
                    $"[OpenRouter TTS] cut_audio clip failed to load properly: {clip.loadState} url={url}"
                );
                callback?.Invoke(null, false);
                yield break;
            }

            callback?.Invoke(clip, true);
        }
    }

    private IEnumerator SynthesizeWithOpenRouter(string text, Action<AudioClip, bool> callback)
    {
        // API key availability is verified by SceneManager_SessionWizard before
        // entering checkup, so this should never happen in normal flow.
        // Kept as a silent safety net only.
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[OpenRouter TTS] API key is empty. Pre-check should have caught this.");
            callback(null, false);
            yield break;
        }

        var requestData = new OpenRouterTTSRequest
        {
            model = this.model,
            input = text,
            voice = this.voice,
            // response_format defaults to "mp3" in OpenRouterTTSRequest class.
            // Speed is not sent so the provider defaults to 1.0 (normal speed).
        };

        string jsonBody = JsonConvert.SerializeObject(requestData);
        if (verboseLogging)
            Debug.Log($"[OpenRouter TTS] Creating OpenRouter TTS request with payload: {jsonBody}");

        using (UnityWebRequest www = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            www.SetRequestHeader("HTTP-Referer", "https://github.com/somnusmind");
            www.SetRequestHeader("X-Title", "Genesis");

            if (verboseLogging)
                Debug.Log(
                    $"[OpenRouter TTS] Sending request with Authorization: Bearer {apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}"
                );

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[OpenRouter TTS] HTTP error during OpenRouter API call: {www.error}"
                );
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        $"Failed to connect to OpenRouter TTS API: {www.error}",
                        () =>
                        {
                            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                        }
                    );
                }
                callback(null, false);
                yield break;
            }

            byte[] audioData = www.downloadHandler.data;

            if (audioData == null || audioData.Length == 0)
            {
                Debug.LogError("[OpenRouter TTS] No audio data received from OpenRouter API");
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        "No audio data received from OpenRouter TTS API. Please try again later.",
                        () =>
                        {
                            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                        }
                    );
                }
                callback(null, false);
                yield break;
            }

            // Always save as .mp3 since we request MP3 from the API and write the
            // raw response bytes directly to a temporary file. Unity's MP3 decoder
            // auto-detects the native sample rate from the MP3 frame headers.
            string tempFilePath = Path.Combine(
                Application.temporaryCachePath,
                $"openrouter_tts_temp_{Guid.NewGuid()}.mp3"
            );

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tempFilePath));
                File.WriteAllBytes(tempFilePath, audioData);
                if (verboseLogging)
                    Debug.Log(
                        $"[OpenRouter TTS] Audio data saved to temporary file: {tempFilePath}"
                    );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenRouter TTS] Error saving temporary audio file: {ex.Message}");
                callback(null, false);
                yield break;
            }

            string loadUrl = "file:///" + tempFilePath.Replace("\\", "/");

            UnityWebRequest loadRequest = UnityWebRequestMultimedia.GetAudioClip(
                loadUrl,
                AudioType.MPEG
            );

            yield return loadRequest.SendWebRequest();

            if (loadRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[OpenRouter TTS] Failed to load audio from temporary file: {loadRequest.error}"
                );
                try
                {
                    if (File.Exists(tempFilePath))
                        File.Delete(tempFilePath);
                }
                catch { }
                callback(null, false);
                yield break;
            }

            AudioClip audioClip = DownloadHandlerAudioClip.GetContent(loadRequest);

            if (audioClip == null)
            {
                Debug.LogError("[OpenRouter TTS] Failed to create AudioClip from downloaded data");
                try
                {
                    if (File.Exists(tempFilePath))
                        File.Delete(tempFilePath);
                }
                catch { }
                callback(null, false);
                yield break;
            }

            while (audioClip.loadState == AudioDataLoadState.Loading)
            {
                yield return null;
            }

            if (audioClip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError(
                    $"[OpenRouter TTS] Audio clip failed to load properly: {audioClip.loadState}"
                );
                try
                {
                    if (File.Exists(tempFilePath))
                        File.Delete(tempFilePath);
                }
                catch { }
                callback(null, false);
                yield break;
            }

            if (verboseLogging)
                Debug.Log(
                    $"[OpenRouter TTS] Loaded MP3 audio clip. Native sample rate: {audioClip.frequency}Hz, Channels: {audioClip.channels}, Length: {audioClip.length:F2}s"
                );

            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenRouter TTS] Failed to delete temporary file: {ex.Message}");
            }

            callback(audioClip, true);
        }
    }

    private IEnumerator CreateSilentClip(int index)
    {
        // 24000 Hz is used for silent clips as a neutral default. Silence has
        // no frequency content, so the sample rate does not affect quality.
        int sampleRate = 24000;
        AudioClip silentClip = AudioClip.Create("Silent", sampleRate, 1, sampleRate, false);

        float[] silentData = new float[sampleRate];
        silentClip.SetData(silentData, 0);

        yield return null; // Spread load: let UI update before AudioCoordinator assignment

        PlayAudio(silentClip, true, index);

        if (sceneManager != null)
            sceneManager.CreateProgressMessage($"Created silent clip for {GetStageName(index)}");

        IncrementSynthesisCompleted();
        yield break;
    }

    private AudioClip CreateSilenceClip(float duration, int sampleRate)
    {
        int samples = Mathf.Max(1, Mathf.RoundToInt(duration * sampleRate));
        AudioClip silenceClip = AudioClip.Create(
            $"Silence_{duration}s",
            samples,
            1,
            sampleRate,
            false
        );

        float[] silentData = new float[samples];

        silenceClip.SetData(silentData, 0);
        return silenceClip;
    }

    private AudioClip EnsureFormat(AudioClip clip, int targetSampleRate, int targetChannels)
    {
        if (clip == null)
            return null;
        if (targetChannels != 1)
        {
            Debug.LogWarning(
                "[OpenRouter TTS] EnsureFormat currently enforces mono output. Forcing mono."
            );
            targetChannels = 1;
        }

        float[] mono = GetMonoData(clip);

        int srcRate = clip.frequency;
        if (srcRate != targetSampleRate)
        {
            if (verboseLogging)
                Debug.Log(
                    $"[OpenRouter TTS] Resampling clip from {srcRate}Hz to {targetSampleRate}Hz (clip: {clip.name}, length: {clip.length:F2}s)"
                );
            mono = ResampleLinear(mono, srcRate, targetSampleRate);
        }

        AudioClip outClip = AudioClip.Create(
            clip.name + "_mono",
            mono.Length,
            1,
            targetSampleRate,
            false
        );
        outClip.SetData(mono, 0);
        return outClip;
    }

    private float[] GetMonoData(AudioClip clip)
    {
        int frames = clip.samples;
        int ch = clip.channels;

        float[] data = new float[frames * ch];
        clip.GetData(data, 0);

        if (ch == 1)
        {
            return data;
        }

        float[] mono = new float[frames];
        int idx = 0;

        for (int i = 0; i < frames; i++)
        {
            float sum = 0f;
            for (int c = 0; c < ch; c++)
            {
                sum += data[idx++];
            }
            mono[i] = sum / ch;
        }

        return mono;
    }

    private float[] ResampleLinear(float[] src, int srcRate, int dstRate)
    {
        if (src == null || src.Length == 0)
            return src;
        if (srcRate == dstRate)
        {
            float[] copy = new float[src.Length];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        float ratio = (float)dstRate / srcRate;
        int dstLen = Mathf.Max(1, Mathf.RoundToInt(src.Length * ratio));
        float[] dst = new float[dstLen];

        float invRatio = (float)srcRate / dstRate;

        for (int i = 0; i < dstLen; i++)
        {
            float srcPos = i * invRatio;
            int i0 = Mathf.FloorToInt(srcPos);
            int i1 = Mathf.Min(i0 + 1, src.Length - 1);
            float t = srcPos - i0;
            dst[i] = Mathf.Lerp(src[i0], src[i1], t);
        }

        return dst;
    }

    private AudioClip CombineAudioClips(
        List<AudioClip> clips,
        int targetSampleRate,
        int targetChannels
    )
    {
        if (clips == null || clips.Count == 0)
            return null;
        if (clips.Count == 1)
            return clips[0];

        if (targetChannels != 1)
        {
            Debug.LogWarning(
                "[OpenRouter TTS] CombineAudioClips currently enforces mono output. Forcing mono."
            );
            targetChannels = 1;
        }

        int totalSamples = 0;
        foreach (var clip in clips)
        {
            if (clip == null)
                continue;
            if (clip.frequency != targetSampleRate || clip.channels != targetChannels)
            {
                Debug.LogWarning(
                    $"[OpenRouter TTS] Clip format mismatch before combine. Expected {targetSampleRate}Hz/{targetChannels}ch but got {clip.frequency}Hz/{clip.channels}ch"
                );
            }
            totalSamples += clip.samples;
        }

        AudioClip combinedClip = AudioClip.Create(
            "CombinedAudio",
            totalSamples,
            targetChannels,
            targetSampleRate,
            false
        );
        float[] combinedData = new float[totalSamples];
        int position = 0;

        int deClickSamples = 0;
        if (useBoundaryDeClick && boundaryDeClickSeconds > 0f)
        {
            deClickSamples = Mathf.Clamp(
                Mathf.RoundToInt(boundaryDeClickSeconds * targetSampleRate),
                1,
                2048
            );
        }

        for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
        {
            var clip = clips[clipIndex];
            if (clip == null)
                continue;

            float[] clipData = new float[clip.samples];
            clip.GetData(clipData, 0);

            if (deClickSamples > 0)
            {
                if (clipIndex > 0)
                {
                    int n = Mathf.Min(deClickSamples, clipData.Length);
                    for (int i = 0; i < n; i++)
                    {
                        float t = (i + 1) / (float)n;
                        clipData[i] *= t;
                    }

                    int available = Mathf.Min(deClickSamples, position);
                    for (int i = 0; i < available; i++)
                    {
                        float t = (i + 1) / (float)available;
                        int idx = position - available + i;
                        combinedData[idx] *= (1f - t);
                    }
                }
            }

            int remaining = combinedData.Length - position;
            int toCopy = Mathf.Min(clipData.Length, remaining);
            Array.Copy(clipData, 0, combinedData, position, toCopy);
            position += toCopy;

            if (position >= combinedData.Length)
                break;
        }

        combinedClip.SetData(combinedData, 0);
        return combinedClip;
    }

    private void PlayAudio(AudioClip audioClip, bool isSilentClip = false, int index = 0)
    {
        if (audioClip == null)
        {
            Debug.LogError("[OpenRouter TTS] PlayAudio: AudioClip is null");
            OnCacheSaveComplete?.Invoke();
            return;
        }

        if (!isSilentClip)
        {
            string uniqueName = GenerateUniqueNameFromText(
                index == 5 ? mantraTTS : GetStageTTS(index)
            );
            try
            {
                AudioCacheHelperOpenRouter.SaveAudioClip(audioClip, uniqueName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenRouter TTS] Failed to cache audio clip: {ex.Message}");
            }
        }

        if (audioCoordinator != null)
        {
            try
            {
                if (index == 5)
                {
                    audioCoordinator.SetMantraClip(audioClip);
                }
                else
                {
                    audioCoordinator.SetClip(index, audioClip);
                }

                OnCacheSaveComplete?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenRouter TTS] Failed to set audio clip: {ex.Message}");
                OnCacheSaveComplete?.Invoke();
            }
        }
        else
        {
            Debug.LogError("[OpenRouter TTS] AudioCoordinator reference is not set.");
            OnCacheSaveComplete?.Invoke();
        }
    }

    private void IncrementSynthesisCompleted()
    {
        if (synthesisCompletionInvoked)
            return;

        synthesisCompletedCount++;
        if (verboseLogging)
            Debug.Log(
                $"[OpenRouter TTS] Synthesis completed: {synthesisCompletedCount}/{totalSynthesisCount}"
            );

        if (synthesisCompletedCount >= totalSynthesisCount)
        {
            synthesisCompletionInvoked = true;
            if (verboseLogging)
                Debug.Log(
                    $"[OpenRouter TTS] All synthesis tasks completed ({synthesisCompletedCount}/{totalSynthesisCount}). Invoking OnSynthesisComplete."
                );
            OnSynthesisComplete?.Invoke();
        }
    }

    private void OnDestroy()
    {
        foreach (var coroutine in timeoutCoroutines.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        timeoutCoroutines.Clear();
    }

    [System.Serializable]
    private class OpenRouterTTSRequest
    {
        public string model;
        public string input;
        public string voice;
        // response_format is hardcoded to "mp3". This ensures Unity's MP3
        // decoder can auto-detect the native sample rate from the frame
        // headers, guaranteeing maximum audio quality without any manual
        // sample-rate configuration. The speed parameter is omitted so the
        // provider defaults to 1.0 (normal speed).
        public string response_format = "mp3";
    }
}

public static class AudioCacheHelperOpenRouter
{
    public static void SaveAudioClip(AudioClip clip, string filename)
    {
        string cacheFolderPath = ConfigPath.GetCacheFolderPath();
        if (!Directory.Exists(cacheFolderPath))
        {
            Directory.CreateDirectory(cacheFolderPath);
            Debug.Log($"[OpenRouter TTS] Cache directory created: {cacheFolderPath}");
        }

        string path = Path.Combine(cacheFolderPath, filename + ".wav");
        if (!File.Exists(path))
        {
            SavWav.Save(path, clip);
            Debug.Log($"[OpenRouter TTS] AudioClip successfully saved to: {path}");
        }
        else
        {
            Debug.Log($"[OpenRouter TTS] AudioClip already exists in cache: {path}");
        }
    }

    public static IEnumerator LoadAudioClip(
        string filename,
        Action<AudioClip> callback,
        Action<bool> cacheStatusCallback
    )
    {
        string cacheFolderPath = ConfigPath.GetCacheFolderPath();
        if (!Directory.Exists(cacheFolderPath))
        {
            Directory.CreateDirectory(cacheFolderPath);
            Debug.Log($"[OpenRouter TTS] Cache directory created for loading: {cacheFolderPath}");
        }

        string sourceFilePath = Path.Combine(cacheFolderPath, filename + ".wav");
        Debug.Log("[OpenRouter TTS] Cache Path: " + sourceFilePath);

        if (File.Exists(sourceFilePath))
        {
            string tempDir = Path.Combine(Application.temporaryCachePath, "AudioCache");
            if (!Directory.Exists(tempDir))
            {
                try
                {
                    Directory.CreateDirectory(tempDir);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[OpenRouter TTS] Failed to create temp directory: {ex.Message}"
                    );
                    callback?.Invoke(null);
                    cacheStatusCallback?.Invoke(false);
                    yield break;
                }
            }

            string tempFile = Path.Combine(tempDir, "temp_audio_" + DateTime.Now.Ticks + ".wav");

            try
            {
                File.Copy(sourceFilePath, tempFile, true);
                Debug.Log($"[OpenRouter TTS] Copied cache file to temporary location: {tempFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenRouter TTS] Failed to copy cache file: {ex.Message}");
                callback?.Invoke(null);
                cacheStatusCallback?.Invoke(false);
                yield break;
            }

            Uri fileUri = new Uri(tempFile);
            string uriPath = fileUri.AbsoluteUri;
            Debug.Log("[OpenRouter TTS] Loading from temporary URI: " + uriPath);

            using (
                UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uriPath, AudioType.WAV)
            )
            {
                yield return www.SendWebRequest();

                try
                {
                    File.Delete(tempFile);
                    Debug.Log("[OpenRouter TTS] Temporary file deleted");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[OpenRouter TTS] Could not delete temporary file: {ex.Message}"
                    );
                }

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip audioClip = DownloadHandlerAudioClip.GetContent(www);
                    Debug.Log(
                        $"[OpenRouter TTS] Successfully loaded AudioClip from cache via temp file"
                    );
                    yield return null; // Prevent UI hang before handing long clip to AudioCoordinator normalization
                    callback?.Invoke(audioClip);
                    cacheStatusCallback?.Invoke(true);
                }
                else
                {
                    Debug.LogError(
                        $"[OpenRouter TTS] Error loading audio file: {www.error}, URI: {uriPath}"
                    );
                    callback?.Invoke(null);
                    cacheStatusCallback?.Invoke(false);
                }
            }
        }
        else
        {
            Debug.Log($"[OpenRouter TTS] AudioClip not found in cache: {filename}");
            callback?.Invoke(null);
            cacheStatusCallback?.Invoke(false);
        }
    }
}