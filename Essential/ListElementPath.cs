using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

public class ListElementPath : MonoBehaviour
{
    public TMP_Text label;
    public Image highlighterImage;
    public Image symbolImage_GO;

    [Header("Symbol Sprites")]
    public Sprite symbolImage_0; // Headphone Image
    public Sprite symbolImage_1; // Meditation/Yoga Image
    public Sprite symbolImage_2; // HypnosisSpiral Image
    public Sprite symbolImage_3; // Pearls Image
    public Sprite symbolImage_4; // EEG Image (for Brainwave Entrainment)
    public Sprite symbolImage_5; // Flask (Experimental)

    // Regex patterns for tag detection (same as in SynthesizeTTS_OpenRouter)
    private static readonly Regex cutTagRegex = new Regex(@"<cut>", RegexOptions.Compiled);
    private static readonly Regex cutAudioTagRegex = new Regex(@"<cut_audio=(?<url>[^>\s]+)>", RegexOptions.Compiled);
    private static readonly Regex cutSilenceTagRegex = new Regex(@"<cut_silence=(?<duration>[0-9]+\.?[0-9]*)>", RegexOptions.Compiled);

    // Script References for UI Indicators
    private MetaDescription metaDescription;

    // REPLACED individual indicator references with the combined one
    private SessionPreviewIndicators sessionPreviewIndicators;

    private DirectoryScanner directoryScanner;

    // Static event and currently selected element
    public static event System.Action<ListElementPath> OnElementSelected;
    public static ListElementPath currentlySelectedElement;

    private string filePath;

    public void Awake()
    {
        metaDescription = FindAnyObjectByType<MetaDescription>();

        // Initialize the combined indicator reference
        sessionPreviewIndicators = FindAnyObjectByType<SessionPreviewIndicators>();

        directoryScanner = FindAnyObjectByType<DirectoryScanner>();
    }

    // Initializes the element
    public void Initialize(string path, Config config)
    {
        filePath = path;
        label.text = System.IO.Path.GetFileName(path);
        highlighterImage.gameObject.SetActive(false);

        // Set the Symbol Image
        switch (config.symbolImage)
        {
            case 0:
                symbolImage_GO.sprite = symbolImage_0;
                break;
            case 1:
                symbolImage_GO.sprite = symbolImage_1;
                break;
            case 2:
                symbolImage_GO.sprite = symbolImage_2;
                break;
            case 3:
                symbolImage_GO.sprite = symbolImage_3;
                break;
            case 4:
                symbolImage_GO.sprite = symbolImage_4;
                break;
            case 5:
                symbolImage_GO.sprite = symbolImage_5;
                break;

            default:
                symbolImage_GO.sprite = symbolImage_0;
                break;
        }
    }

    public void OnElementClicked()
    {
        // Deselect previous element
        if (currentlySelectedElement != null)
        {
            currentlySelectedElement.highlighterImage.gameObject.SetActive(false);
        }

        // Select this element
        currentlySelectedElement = this;
        highlighterImage.gameObject.SetActive(true);

        // First, update the base folder path
        ConfigPath.UpdateBaseFolderPath(filePath);

        // Ensure config file is created before running cache check and notifications
        StartCoroutine(DelayedNotifications());

        // Notify subscribers
        OnElementSelected?.Invoke(this);
    }

    // Deletes the parent directory from the config panel
    public void DeleteDirectory()
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && Directory.Exists(filePath))
            {
                Directory.Delete(filePath, true);
                Debug.Log($"Successfully deleted directory: {filePath}");

                Destroy(gameObject);
            }
            else
            {
                Debug.LogWarning($"Directory not found or path is empty: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error deleting directory: {ex.Message}");
        }

        directoryScanner.RefreshDirectoryList();
    }

    // Checks if text contains only silence tags (no speech or audio clips).
    // This mirrors the logic in SynthesizeTTS_OpenRouter.
    private bool ContainsOnlySilenceTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        // Remove all <cut> tags (they don't produce content)
        string processedText = cutTagRegex.Replace(text, "");

        // Remove all <cut_silence=X> tags
        processedText = cutSilenceTagRegex.Replace(processedText, "");

        // Check if there are any <cut_audio=...> tags
        bool hasAudioTags = cutAudioTagRegex.IsMatch(processedText);
        if (hasAudioTags)
            return false; // Audio clips need to be downloaded

        // Remove audio tags for remaining check
        processedText = cutAudioTagRegex.Replace(processedText, "");

        // If there's any remaining non-whitespace text, it needs TTS
        return string.IsNullOrWhiteSpace(processedText);
    }

    private bool CheckStageCache(string text, Config config)
    {
        // If the text is empty or whitespace, consider it as "cached" since we'll generate a silent clip
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // If text contains only silence tags, it's always "cached" (no TTS/download needed)
        if (ContainsOnlySilenceTags(text))
        {
            return true;
        }

        using (SHA256 hash = SHA256.Create())
        {
            // Generate the hash using the SAME parameters as SynthesizeTTS_OpenRouter
            string input = $"{text}|{config.ttsModel}|{config.ttsVoice}";
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            string filename = builder.ToString();
            string path = Path.Combine(ConfigPath.GetCacheFolderPath(), filename + ".wav");
            return File.Exists(path);
        }
    }

    private bool CheckAllStagesCache()
    {
        ConfigManager configManager = new ConfigManager();
        Config config = configManager.LoadConfiguration();

        try
        {
            bool stage1Cached = CheckStageCache(config.stage1TTS, config);
            bool stage2Cached = CheckStageCache(config.stage2TTS, config);
            bool stage3Cached = CheckStageCache(config.stage3TTS, config);
            bool stage4Cached = CheckStageCache(config.stage4TTS, config);
            bool stage5Cached = CheckStageCache(config.stage5TTS, config);

            // Check mantra/subliminal track
            bool subliminalCached = CheckStageCache(config.mantraTTS, config);

            return stage1Cached && stage2Cached && stage3Cached && stage4Cached && stage5Cached && subliminalCached;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error checking cache status: {ex.Message}");
            return false;
        }
    }

    // Runs all required processes after the file has updated
    private IEnumerator DelayedNotifications()
    {
        // Wait for a frame to ensure config file is created
        yield return new WaitForEndOfFrame();

        // Calculate Cache Status
        bool isFullyCached = false;
        if (File.Exists(ConfigPath.Path))
        {
            isFullyCached = CheckAllStagesCache();
        }
        else
        {
            Debug.LogWarning("Config file not found, cannot check cache status");
        }

        // UPDATE THE COMBINED INDICATOR
        if (sessionPreviewIndicators != null)
        {
            // This single call updates Cache, SessionMode, Strobe, Subliminal, Repeat, and TTS Model
            sessionPreviewIndicators.UpdateAllIndicators(isFullyCached);
        }
        else
        {
            Debug.LogWarning("SessionPreviewIndicators reference not found in ListElementPath.");
        }

        // Update Meta Description (Separate script, kept separate as per original structure)
        if (metaDescription != null)
        {
            metaDescription.UpdateMetaDescription();
        }
    }

    public string GetFilePath()
    {
        return filePath;
    }

    private void OnDestroy()
    {
        if (currentlySelectedElement == this)
        {
            currentlySelectedElement = null;
        }
    }
}