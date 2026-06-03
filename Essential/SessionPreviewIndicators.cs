using UnityEngine;
using TMPro;

public class SessionPreviewIndicators : MonoBehaviour
{
    [Header("Value Text References")]
    public TMP_Text cacheStatusValueText;
    public TMP_Text sessionModeValueText;
    public TMP_Text strobeValueText;
    public TMP_Text subliminalEnabledValueText;
    public TMP_Text repeatSessionValueText;
    public TMP_Text ttsModeValueText;

    private const string NoSelectionText = "-";

    private void Awake()
    {
        ResetIndicatorsToDefault();
    }

    public void ResetIndicatorsToDefault()
    {
        UpdateText(cacheStatusValueText, NoSelectionText);
        UpdateText(sessionModeValueText, NoSelectionText);
        UpdateText(strobeValueText, NoSelectionText);
        UpdateText(subliminalEnabledValueText, NoSelectionText);
        UpdateText(repeatSessionValueText, NoSelectionText);
        UpdateText(ttsModeValueText, NoSelectionText);
    }

    public void UpdateAllIndicators(bool cacheStatus)
    {
        if (ListElementPath.currentlySelectedElement == null || !System.IO.File.Exists(ConfigPath.Path))
        {
            ResetIndicatorsToDefault();
            return;
        }

        UpdateCacheCheck(cacheStatus);
        UpdateSessionModeIndicator();
        UpdateStrobeIndicator();
        UpdateSubliminalEnabledIndicator();
        UpdateRepeatSessionIndicator();
        UpdateTTSModelIndicator();
    }

    public void UpdateCacheCheck(bool value)
    {
        if (!System.IO.File.Exists(ConfigPath.Path))
        {
            UpdateText(cacheStatusValueText, NoSelectionText);
            return;
        }

        string result = value ? "True -> No API Key required" : "False -> API Key required";
        UpdateText(cacheStatusValueText, result);
    }

    public void UpdateSessionModeIndicator()
    {
        if (sessionModeValueText == null) return;

        Config config = ConfigManager.Instance.LoadConfiguration();
        if (config == null)
        {
            UpdateText(sessionModeValueText, NoSelectionText);
            return;
        }

        string result = config.sessionMode switch
        {
            "Static" => "Static",
            "Sequential" => "Sequential",
            _ => "Unknown"
        };
        UpdateText(sessionModeValueText, result);
    }

    public void UpdateStrobeIndicator()
    {
        if (strobeValueText == null) return;

        Config config = ConfigManager.Instance.LoadConfiguration();
        if (config == null)
        {
            UpdateText(strobeValueText, NoSelectionText);
            return;
        }

        string result;
        if (config.enableStrobe && config.useSingleImage)
            result = "True(SingleImage)";
        else if (config.enableStrobe && !config.useSingleImage)
            result = "True(DualImage)";
        else
            result = "False";

        UpdateText(strobeValueText, result);
    }

    public void UpdateSubliminalEnabledIndicator()
    {
        if (subliminalEnabledValueText == null) return;

        Config config = ConfigManager.Instance.LoadConfiguration();
        if (config == null)
        {
            UpdateText(subliminalEnabledValueText, NoSelectionText);
            return;
        }

        string result = !string.IsNullOrEmpty(config.mantraTTS) ? "True" : "False";
        UpdateText(subliminalEnabledValueText, result);
    }

    public void UpdateRepeatSessionIndicator()
    {
        if (repeatSessionValueText == null) return;

        Config config = ConfigManager.Instance.LoadConfiguration();
        if (config == null)
        {
            UpdateText(repeatSessionValueText, NoSelectionText);
            return;
        }

        string result = config.repeatSession ? "True" : "False";
        UpdateText(repeatSessionValueText, result);
    }

    public void UpdateTTSModelIndicator()
    {
        if (ttsModeValueText == null) return;

        Config config = ConfigManager.Instance.LoadConfiguration();
        if (config == null)
        {
            UpdateText(ttsModeValueText, NoSelectionText);
            return;
        }

        string result = string.IsNullOrEmpty(config.ttsModel) ? "No TTS Model" : config.ttsModel;
        UpdateText(ttsModeValueText, result);
    }

    private void UpdateText(TMP_Text textElement, string value)
    {
        if (textElement != null)
        {
            textElement.text = value;
        }
    }
}