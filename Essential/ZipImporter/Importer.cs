using UnityEngine;
using System.IO;
using UnityEngine.Events;
using System.Collections;

#if UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
using Crosstales.FB;
#endif

public class Importer : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Enable detailed debug logs")]
    public bool verboseLogging = true;

    private DirectoryScanner directoryScanner;

    public void Awake()
    {
        directoryScanner = FindAnyObjectByType<DirectoryScanner>();
    }

    public void OpenFilePickerAndExtractZip()
    {
#if UNITY_ANDROID
        OpenFilePickerAndroid();
#elif UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
        OpenFilePickerWindowsMultiple();
#endif
    }

#if UNITY_ANDROID
    private void OpenFilePickerAndroid()
    {
        if (NativeFilePicker.IsFilePickerBusy())
            return;

        // Use PickMultipleFiles for bulk import
        NativeFilePicker.Permission permission = NativeFilePicker.PickMultipleFiles(
            (paths) =>
            {
                if (paths == null || paths.Length == 0)
                {
                    Debug.Log("Operation cancelled or no permission granted");
                    return;
                }

                Debug.Log($"Selected {paths.Length} files for import.");
                StartCoroutine(ProcessMultipleZips(paths));
            },
            new string[] { "application/zip" } // Allow only ZIP files
        );

        Debug.Log("Permission result: " + permission);
    }
#endif

#if UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
    private void OpenFilePickerWindowsMultiple()
    {
        if (verboseLogging) Debug.Log("Opening file browser for multiple ZIP files...");
        
        try 
        {
            // Use OpenFiles (plural) for bulk selection
            string[] paths = FileBrowser.Instance.OpenFiles(
                "Select ZIP File(s)",
                "",
                "ZIP Files",
                new ExtensionFilter[] { new ExtensionFilter("ZIP Files", "zip") }
            );

            if (paths != null)
            {
                if (verboseLogging) Debug.Log($"File browser returned {paths.Length} file(s)");
                
                if (paths.Length > 0)
                {
                    // Log all selected files
                    if (verboseLogging)
                    {
                        foreach (string path in paths)
                        {
                            Debug.Log($"Selected file: {path}");
                        }
                    }
                    
                    StartCoroutine(ProcessMultipleZips(paths));
                }
            }
            else
            {
                Debug.Log("No files selected or operation was cancelled");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in file browser: {e.Message}\n{e.StackTrace}");
        }
    }
#endif

    private IEnumerator ProcessMultipleZips(string[] paths)
    {
        int successCount = 0;

        foreach (string path in paths)
        {
            if (verboseLogging) Debug.Log($"Processing file: {path}");
            bool success = ExtractAndVerifyZip(path);

            if (success)
                successCount++;

            // Small delay between processing files to avoid UI freezing
            yield return null;
        }

        Debug.Log($"Import completed. Successfully imported {successCount} out of {paths.Length} files.");
        directoryScanner.RefreshListAndSelectFirst();
    }

    private bool ExtractAndVerifyZip(string path)
    {
        string directoryName = Path.GetFileNameWithoutExtension(path);
        string finalExtractPath = Path.Combine(Application.persistentDataPath, "MyContent", directoryName);

        try
        {
            // Ensure the extraction directory exists
            if (!Directory.Exists(finalExtractPath))
            {
                Directory.CreateDirectory(finalExtractPath);
            }

            // Extract the zip file
            UnzipUtility.ExtractZipFile(path, finalExtractPath);
            Debug.Log($"ZIP file extracted to: {finalExtractPath}");

            // Verify the archive structure by checking for a config.yml file
            string configFilePath = Path.Combine(finalExtractPath, "config.yml");
            if (!File.Exists(configFilePath))
            {
                Debug.LogError("Archive does not contain a config.yml file");
                CleanupExtractedDirectory(finalExtractPath);

                // Show error message to user
                GlobalErrorHandling errorHandler = FindAnyObjectByType<GlobalErrorHandling>();
                if (errorHandler != null)
                {
                    errorHandler.CallGlobalErrorWithConfirmation(
                        $"The imported archive '{Path.GetFileName(path)}' does not contain a valid configuration file (config.yml).",
                        () => { /* No action needed on confirmation */ }
                    );
                }

                return false;
            }

            // Store current path for restoration
            string originalPath = ConfigPath.Path;

            try
            {
                // Temporarily set path to the new config
                ConfigPath.UpdateBaseFolderPath(finalExtractPath);

                // Load and check compatibility using the temporary path
                Config config = ConfigManager.Instance.LoadConfiguration(configFilePath);
                // Note: LoadConfiguration inside ConfigManager now supports path parameter, 
                // but ConfigPath.UpdateBaseFolderPath is needed to set the global context for IsConfigurationCompatible check
                // which uses ConfigPath.Path if not passed specifically. 
                // ConfigManager.Instance.IsConfigurationCompatible checks config object, so it doesn't need global path set.

                if (config == null || !ConfigManager.Instance.IsConfigurationCompatible(config))
                {
                    string versionInfo = config != null ? config.compatibilityVersion.ToString() : "Unknown";
                    Debug.LogError($"Archive version {versionInfo} is not compatible with current version {ConfigManager.CURRENT_COMPATIBILITY_VERSION}");

                    // Show error message to user
                    GlobalErrorHandling errorHandler = FindAnyObjectByType<GlobalErrorHandling>();
                    if (errorHandler != null)
                    {
                        errorHandler.CallGlobalErrorWithConfirmation(
                            $"The imported configuration '{Path.GetFileName(path)}' (version {versionInfo}) is not compatible with the current app version ({ConfigManager.CURRENT_COMPATIBILITY_VERSION}).",
                            () => { /* No action needed on confirmation */ }
                        );
                    }

                    CleanupExtractedDirectory(finalExtractPath);
                    return false;
                }
            }
            finally
            {
                // Reset to previous path
                ConfigPath.UpdateBaseFolderPath(Path.GetDirectoryName(originalPath));
            }

            directoryScanner.RefreshListAndSelectFirst();
            Debug.Log($"Archive '{Path.GetFileName(path)}' successfully extracted, verified, and found to be compatible.");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during extraction of '{Path.GetFileName(path)}': {e.Message}");
            CleanupExtractedDirectory(finalExtractPath);

            // Show error message to user
            GlobalErrorHandling errorHandler = FindAnyObjectByType<GlobalErrorHandling>();
            if (errorHandler != null)
            {
                errorHandler.CallGlobalErrorWithConfirmation(
                    $"Error extracting archive '{Path.GetFileName(path)}': {e.Message}",
                    () => { /* No action needed on confirmation */ }
                );
            }

            return false;
        }
    }

    private void CleanupExtractedDirectory(string path)
    {
        // Delete extracted directory if it exists
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
                Debug.Log($"Cleaned up invalid extraction directory: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to clean up directory: {e.Message}");
            }
        }
    }
}