using UnityEngine;
using TMPro;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Evo.UI;

namespace Openrouter
{
    public class OpenrouterKeyManager : MonoBehaviour
    {
        [SerializeField] private TMP_InputField apiKeyInputOpenrouter;
        [SerializeField] private Button saveButtonOpenrouter;
        [SerializeField] private Button clearButtonOpenrouter;

        // OpenRouter identification headers - set these in the Inspector
        public string openrouterAppUrl = "https://github.com/somnusmind";
        public string openrouterAppName = "Genesis";

        private const string API_KEYS_FOLDER = ".api_keys";
        private const string OPENROUTER_FILE = "api_key_openrouter_encrypted.json";

        // Reference to GlobalErrorHandling for confirmation dialog
        private GlobalErrorHandling globalErrorHandling;

        private readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        };


        private void Start()
        {
            if (!CheckRequiredComponents())
                return;

            globalErrorHandling = GlobalErrorHandling.Instance;
            if (globalErrorHandling == null)
            {
                Debug.LogWarning("GlobalErrorHandling not found. Dialogs may not work properly.");
            }

            // Add button listeners programmatically
            saveButtonOpenrouter.onClick.AddListener(SaveOpenrouterApiKey);
            clearButtonOpenrouter.onClick.AddListener(ClearSavedOpenrouterApiKey);

            LoadAndDecryptOpenrouterApiKey();
        }

        private bool CheckRequiredComponents()
        {
            if (apiKeyInputOpenrouter == null)
            {
                Debug.LogWarning("OpenRouter API Key Input Field not assigned!");
                return false;
            }

            if (saveButtonOpenrouter == null)
            {
                Debug.LogWarning("OpenRouter Save Button not assigned!");
                return false;
            }

            if (clearButtonOpenrouter == null)
            {
                Debug.LogWarning("OpenRouter Clear Button not assigned!");
                return false;
            }

            return true;
        }

        private void UpdateUIState(bool isProcessing, string message = null)
        {
            if (saveButtonOpenrouter != null)
                saveButtonOpenrouter.interactable = !isProcessing;

            if (clearButtonOpenrouter != null)
                clearButtonOpenrouter.interactable = !isProcessing;

            if (apiKeyInputOpenrouter != null)
                apiKeyInputOpenrouter.interactable = !isProcessing;

            if (message != null)
                Debug.Log(message);
        }

        // Validates an OpenRouter API key by calling the authenticated /api/v1/auth/key endpoint.
        private async Task<bool> ValidateOpenrouterApiKey(string apiKey)
        {
            try
            {
                Debug.Log("Starting OpenRouter API key validation with auth endpoint...");

                // The /api/v1/auth/key endpoint is authenticated — it returns 401
                string url = "https://openrouter.ai/api/v1/auth/key";

                using (var client = new UnityWebRequest(url, "GET"))
                {
                    client.downloadHandler = new DownloadHandlerBuffer();
                    client.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                    client.SetRequestHeader("HTTP-Referer", openrouterAppUrl);
                    client.SetRequestHeader("X-Title", openrouterAppName);

                    var operation = client.SendWebRequest();
                    while (!operation.isDone)
                        await Task.Yield();

                    // 401 = Invalid authentication / incorrect API key
                    // 403 = Organization not found / insufficient permissions
                    if (client.responseCode == 401 || client.responseCode == 403)
                    {
                        Debug.LogError(
                            $"API validation failed: Invalid credentials. Response code: {client.responseCode}"
                        );
                        Debug.LogError($"Response: {client.downloadHandler.text}");
                        return false;
                    }

                    // Check for payment required error — key is valid but out of credits
                    if (client.responseCode == 402)
                    {
                        Debug.LogWarning(
                            $"API validation warning: Payment Required (402). Key is valid but out of credits. Response: {client.downloadHandler.text}"
                        );
                        return true;
                    }

                    // Successful authentication
                    if (client.responseCode == 200 || client.responseCode == 0)
                    {
                        try
                        {
                            string responseText = client.downloadHandler.text;
                            if (!string.IsNullOrEmpty(responseText))
                            {
                                JObject response = JObject.Parse(responseText);
                                if (response != null && response["data"] != null)
                                {
                                    string label = response["data"]?["label"]?.ToString() ?? "N/A";
                                    Debug.Log(
                                        $"OpenRouter API key validated successfully. Key label: '{label}'"
                                    );
                                    return true;
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            Debug.LogWarning(
                                $"Could not parse auth/key response, but request succeeded: {parseEx.Message}"
                            );
                            return true;
                        }
                    }

                    // Rate limited — key is valid, just throttled
                    if (client.responseCode == 429)
                    {
                        Debug.LogWarning(
                            "API key is valid but rate-limited (429). Proceeding with save."
                        );
                        return true;
                    }

                    // Server errors — key likely valid, server having issues
                    if (client.responseCode >= 500 && client.responseCode < 600)
                    {
                        Debug.LogWarning(
                            $"API key is valid but server returned error ({client.responseCode}). Proceeding with save."
                        );
                        return true;
                    }

                    // Any other response code we haven't handled — be conservative
                    // and reject the key rather than save a potentially invalid one
                    Debug.LogError(
                        $"API validation returned unexpected response code: {client.responseCode}. Rejecting key."
                    );
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"OpenRouter API validation exception: {ex.Message}");
                return false;
            }
        }

        // Checks whether a valid OpenRouter API key is available without modifying UI state.
        // Returns true only if the encrypted key file exists, can be decrypted, and is non-empty.
        // This does NOT make a network call — it only verifies that a key was previously
        // saved and can be decrypted. The actual validity of the key is only confirmed
        // when the user explicitly saves it (which calls ValidateOpenrouterApiKey).
        public bool IsApiKeyAvailable()
        {
            try
            {
                string filePath = GetOpenrouterKeyPath();

                if (!File.Exists(filePath))
                    return false;

                string json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                var auth = JsonConvert.DeserializeObject<OpenrouterAuth>(json, jsonSettings);
                if (auth == null || string.IsNullOrEmpty(auth.ApiKey))
                    return false;

                string hardwareId = CryptoHelper.GetHardwareId();
                byte[] key = CryptoHelper.GenerateAesKey(hardwareId);
                string decryptedApiKey = CryptoHelper.DecryptApiKey(auth.ApiKey, key);

                return !string.IsNullOrEmpty(decryptedApiKey);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error checking API key availability: {ex.Message}");
                return false;
            }
        }

        public async void SaveOpenrouterApiKey()
        {
            if (apiKeyInputOpenrouter == null)
            {
                Debug.LogError("Cannot save OpenRouter credentials - Input field not assigned!");
                return;
            }

            UpdateUIState(true, "Validating OpenRouter API key...");

            string apiKey = apiKeyInputOpenrouter.text?.Trim();

            if (string.IsNullOrEmpty(apiKey))
            {
                UpdateUIState(false, "OpenRouter API Key cannot be empty");
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        "OpenRouter API Key cannot be empty. Please enter a valid API key.",
                        () => {/* No callback needed */  }
                    );
                }
                return;
            }

            if (!await ValidateOpenrouterApiKey(apiKey))
            {
                UpdateUIState(false, "Invalid OpenRouter API Key");
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        "Invalid OpenRouter API Key. Please check your credentials and try again.",
                        () => { /* No callback needed */ }
                    );
                }
                return;
            }

            try
            {
                string hardwareId = CryptoHelper.GetHardwareId();
                byte[] key = CryptoHelper.GenerateAesKey(hardwareId);
                string encryptedApiKey = CryptoHelper.EncryptApiKey(apiKey, key);

                var auth = new OpenrouterAuth
                {
                    ApiKey = encryptedApiKey
                };

                string json = JsonConvert.SerializeObject(auth, Formatting.Indented, jsonSettings);

                EnsureDirectoryExists();
                string filePath = GetOpenrouterKeyPath();
                File.WriteAllText(filePath, json);

                Debug.Log($"OpenRouter credentials saved successfully! Path: {filePath}");
                UpdateUIState(false);

                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        "OpenRouter credentials saved successfully! Your API Key is now securely stored.",
                        () =>
                        {
                            /* No callback needed */
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                UpdateUIState(false, $"Failed to save OpenRouter credentials: {ex.Message}");
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        $"Failed to save OpenRouter credentials. Please try again.\n\nError: {ex.Message}",
                        () => { /* No callback needed */ }
                    );
                }
            }
        }

        private void LoadAndDecryptOpenrouterApiKey()
        {
            if (apiKeyInputOpenrouter == null)
                return;

            try
            {
                string filePath = GetOpenrouterKeyPath();
                Debug.Log($"Attempting to load OpenRouter credentials from: {filePath}");

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    Debug.Log($"Loaded OpenRouter credentials file content length: {json.Length}");

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Debug.LogWarning("OpenRouter credentials file is empty.");
                        return;
                    }

                    try
                    {
                        var auth = JsonConvert.DeserializeObject<OpenrouterAuth>(json, jsonSettings);

                        if (auth == null)
                        {
                            Debug.LogWarning("Failed to deserialize OpenRouter credentials data.");
                            return;
                        }

                        if (string.IsNullOrEmpty(auth.ApiKey))
                        {
                            Debug.LogWarning("OpenRouter API key is null or empty in the file.");
                            return;
                        }

                        string hardwareId = CryptoHelper.GetHardwareId();
                        byte[] key = CryptoHelper.GenerateAesKey(hardwareId);
                        string decryptedApiKey = CryptoHelper.DecryptApiKey(auth.ApiKey, key);

                        if (string.IsNullOrEmpty(decryptedApiKey))
                        {
                            Debug.LogWarning("Decrypted OpenRouter API key is empty. This may indicate a hardware ID change.");
                            return;
                        }

                        apiKeyInputOpenrouter.text = decryptedApiKey;
                        Debug.Log("OpenRouter credentials loaded successfully");
                    }
                    catch (JsonException jsonEx)
                    {
                        Debug.LogError($"JSON parsing error: {jsonEx.Message}.");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to decrypt OpenRouter API key: {e.Message}.");
                    }
                }
                else
                {
                    Debug.Log($"No OpenRouter credentials file found at {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading OpenRouter credentials: {ex.Message}");
            }
        }

        public void ClearSavedOpenrouterApiKey()
        {
            string filePath = GetOpenrouterKeyPath();
            bool hasApiKey = !string.IsNullOrEmpty(apiKeyInputOpenrouter?.text);
            bool hasFile = File.Exists(filePath);

            if (!hasApiKey && !hasFile)
            {
                Debug.Log("No OpenRouter credentials to clear.");
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        "No OpenRouter credentials found to clear.",
                        () => { }
                    );
                }
                return;
            }

            if (DialogManager.Instance != null)
            {
                string warningMessage = "Delete your saved OpenRouter credentials?\n\n" +
                                       "This will permanently remove your API Key. " +
                                       "You'll need to re-enter it to use OpenRouter services again.";
                DialogManager.Instance.ShowDialog(
                    warningMessage,
                    PerformCredentialClear,
                    null
                );
            }
            else
            {
                Debug.LogWarning("DialogManager not available. Proceeding with credential clearing directly for OpenRouter.");
                PerformCredentialClear();
            }
        }

        private void PerformCredentialClear()
        {
            try
            {
                string filePath = GetOpenrouterKeyPath();
                bool fileDeleted = false;
                bool uiCleared = false;

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    fileDeleted = true;
                    Debug.Log($"OpenRouter credentials file deleted: {filePath}");
                }

                if (apiKeyInputOpenrouter != null)
                {
                    apiKeyInputOpenrouter.text = "";
                    uiCleared = true;
                }

                if (fileDeleted || uiCleared)
                {
                    Debug.Log("OpenRouter credentials cleared successfully");
                }
                else
                {
                    Debug.Log("No OpenRouter credentials were found to clear");
                }
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        "OpenRouter credentials cleared successfully.",
                        () => { }
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to clear OpenRouter credentials: {ex.Message}");
                if (globalErrorHandling != null)
                {
                    globalErrorHandling.CallGlobalErrorWithConfirmation(
                        $"Failed to clear OpenRouter credentials:\n\n{ex.Message}\n\nPlease try again or manually delete the credentials file.",
                        () => { }
                    );
                }
            }
        }

        private string GetApiKeysDirectory()
        {
            return Path.Combine(Application.persistentDataPath, API_KEYS_FOLDER);
        }

        public string GetOpenrouterKeyPath()
        {
            return Path.Combine(GetApiKeysDirectory(), OPENROUTER_FILE);
        }

        private void EnsureDirectoryExists()
        {
            string directory = GetApiKeysDirectory();
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    [Serializable]
    public class OpenrouterAuth
    {
        [JsonProperty("apikey")]
        public string ApiKey { get; set; }
    }
}