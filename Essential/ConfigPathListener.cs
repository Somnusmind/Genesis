using TMPro;
using UnityEngine;
using UnityEngine.Events;
using System.IO;
using System.IO.Compression;

#if UNITY_STANDALONE_WIN
using Crosstales.FB; 
#endif

public class ConfigPathListener : MonoBehaviour
{
    [Header("Events")]
    public PathUpdatedEvent OnPathUpdated;
    [System.Serializable] public class PathUpdatedEvent : UnityEvent<string> { }

    [Header("UI Elements")]
    public TMP_Text tMP_Text;

    [Header("Settings")]
    public bool loadOnStart = true;
    [SerializeField] private string StandardConfigPath;

    private DirectoryScanner directoryScanner;

    private void OnEnable()
    {
        ConfigPathEvents.OnConfigPathUpdated += HandleConfigPathUpdated;
    }

    private void OnDisable()
    {
        ConfigPathEvents.OnConfigPathUpdated -= HandleConfigPathUpdated;
    }

    private void Start()
    {
        directoryScanner = FindAnyObjectByType<DirectoryScanner>();

        if (loadOnStart)
        {
            HandleConfigPathUpdated(StandardConfigPath);
        }
    }

    public void HandleConfigPathUpdated(string newPath)
    {
        Debug.Log($"The configuration path has been updated: {newPath}");
        OnPathUpdated.Invoke(newPath);
        LoadConfigurationIntoEditor();
    }

    public void LoadConfigurationIntoEditor()
    {
        string configPath = ConfigPath.Path;
        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            string yamlContent = File.ReadAllText(configPath);
            if (tMP_Text != null)
            {
                tMP_Text.text = yamlContent;
            }
        }
        else
        {
            Debug.LogError("Invalid configuration path or file does not exist.");
            if (tMP_Text != null) tMP_Text.text = "No Configuration Selected";
        }
    }

    public void UpdateYamlContent(string newContent)
    {
        if (tMP_Text != null)
        {
            tMP_Text.text = newContent;
        }
    }

    public void OpenMyContentDirectory()
    {
        string configPath = ConfigPath.Path;

        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            string parentDirectory = Path.GetDirectoryName(configPath);

            try
            {
                Application.OpenURL("file://" + parentDirectory);
                Debug.Log($"Opening directory: {parentDirectory}");

                if (directoryScanner != null)
                {
                    directoryScanner.RefreshDirectoryList();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error opening config directory: {e.Message}");
            }
        }
        else
        {
            string myContentPath = Path.Combine(Application.persistentDataPath, "MyContent");

            if (Directory.Exists(myContentPath))
            {
                try
                {
                    Application.OpenURL("file://" + myContentPath);
                    Debug.Log($"No config loaded, opening MyContent directory: {myContentPath}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error opening MyContent directory: {e.Message}");
                }
            }
            else
            {
                string msg = "The 'MyContent' directory does not exist yet. Start a session or import a file first.";
                Debug.LogWarning(msg);
                if (GlobalErrorHandling.Instance != null)
                {
                    GlobalErrorHandling.Instance.CallGlobalErrorWithConfirmation(msg, () => { });
                }
            }
        }
    }

    public void OpenConfigFileExternal()
    {
        string configPath = ConfigPath.Path;

        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            try
            {
                Application.OpenURL("file://" + configPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error opening config file: {e.Message}");
            }
        }
        else
        {
            string errorMsg = "No configuration file selected or file does not exist. Please select a configuration from the list first.";
            Debug.LogError(errorMsg);
            if (GlobalErrorHandling.Instance != null)
            {
                GlobalErrorHandling.Instance.CallGlobalErrorWithConfirmation(errorMsg, () => { });
            }
        }
    }

    public void ShowDeleteConfigDialog()
    {
        string configPath = ConfigPath.Path;

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            string errorMsg = "No configuration selected to delete.";
            Debug.LogWarning(errorMsg);
            if (GlobalErrorHandling.Instance != null)
            {
                GlobalErrorHandling.Instance.CallGlobalErrorWithConfirmation(errorMsg, () => { });
            }
            return;
        }

        DialogManager.Instance.ShowDialog(
            "Are you sure you want to delete this configuration?\n\n<size=18>This action will permanently remove the selected directory and all its contents. This cannot be undone.",
            DeleteCurrentConfig,
            null
        );
    }

    private void DeleteCurrentConfig()
    {
        Debug.Log("DeleteCurrentConfig() called");

        string configPath = ConfigPath.Path;
        string parentDirectory = Path.GetDirectoryName(configPath);

        try
        {
            if (Directory.Exists(parentDirectory))
            {
                Directory.Delete(parentDirectory, true);
                Debug.Log($"Configuration directory successfully deleted: {parentDirectory}");

                // Clear selection and UI
                ListElementPath.currentlySelectedElement = null;
                LoadConfigurationIntoEditor();

                // Reset global path to MyContent root
                ConfigPath.UpdateBaseFolderPath("MyContent");

                directoryScanner.RefreshListAndSelectFirst(); 
            }
            else
            {
                Debug.LogError($"Directory not found: {parentDirectory}");
                string errorMsg = "Could not find the directory to delete.";
                if (GlobalErrorHandling.Instance != null)
                {
                    GlobalErrorHandling.Instance.CallGlobalErrorWithConfirmation(errorMsg, () => { });
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error deleting configuration directory: {e.Message}");
            if (GlobalErrorHandling.Instance != null)
            {
                GlobalErrorHandling.Instance.CallGlobalErrorWithConfirmation($"Error deleting configuration: {e.Message}", () => { });
            }
        }
    }

    public void ExportCurrentConfig()
    {
        string configPath = ConfigPath.Path;

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            string errorMsg = "No configuration selected to export.";
            Debug.LogWarning(errorMsg);
            if (GlobalErrorHandling.Instance != null)
            {
                GlobalErrorHandling.Instance.CallGlobalErrorWithConfirmation(errorMsg, () => { });
            }
            return;
        }

#if UNITY_ANDROID
        ExportCurrentConfigAndroid();
#elif UNITY_STANDALONE_WIN
        ExportCurrentConfigWindows();
#endif
    }

#if UNITY_ANDROID
    private void ExportCurrentConfigAndroid()
    {
        if (NativeFilePicker.IsFilePickerBusy())
            return;

        string configPath = ConfigPath.Path;
        string currentlySelectedPath = null;

        if (!Directory.Exists(Path.GetDirectoryName(configPath)))
        {
            Debug.LogError("No configuration directory exists to export");
            return;
        }
        if (ListElementPath.currentlySelectedElement != null)
        {
            currentlySelectedPath = ListElementPath.currentlySelectedElement.GetFilePath();
        }
        string sourceDirectory = Path.GetDirectoryName(configPath);
        string folderName = Path.GetFileName(Path.GetDirectoryName(configPath));
        string zipPath = Path.Combine(Application.temporaryCachePath, $"{folderName}.zip");

        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

            NativeFilePicker.ExportFile(zipPath, (success) =>
            {
                if (success)
                {
                    Debug.Log($"Configuration '{folderName}' exported successfully");
                    if (directoryScanner != null && currentlySelectedPath != null)
                    {
                        directoryScanner.RefreshDirectoryList(); 
                    }
                }
                else
                    Debug.Log("Export cancelled or failed");

                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            });
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error exporting configuration: {e.Message}");
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }
#endif

#if UNITY_STANDALONE_WIN
    private void ExportCurrentConfigWindows()
    {
        string configPath = ConfigPath.Path;
        string currentlySelectedPath = null;
        if (ListElementPath.currentlySelectedElement != null)
        {
            currentlySelectedPath = ListElementPath.currentlySelectedElement.GetFilePath();
        }

        if (!Directory.Exists(Path.GetDirectoryName(configPath)))
        {
            Debug.LogError("No configuration directory exists to export");
            return;
        }

        string sourceDirectory = Path.GetDirectoryName(configPath);
        string folderName = Path.GetFileName(Path.GetDirectoryName(configPath));
        string zipPath = Path.Combine(Application.temporaryCachePath, $"{folderName}.zip");

        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

            var fileBrowser = FileBrowser.Instance;
            string savePath = fileBrowser.SaveFile("Export Configuration", "", $"{folderName}", new ExtensionFilter[] { new ExtensionFilter("Zip Files", "zip") });

            if (!string.IsNullOrEmpty(savePath))
            {
                File.Copy(zipPath, savePath, true);
                Debug.Log($"Configuration '{folderName}' exported successfully to {savePath}");

                if (directoryScanner != null && currentlySelectedPath != null)
                {
                    directoryScanner.RefreshDirectoryList(); 
                }
            }
            else
            {
                Debug.Log("Export cancelled.");
            }

            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error exporting configuration: {e.Message}");
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }
#endif
}