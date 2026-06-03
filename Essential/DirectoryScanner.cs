using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class DirectoryScanner : MonoBehaviour
{
    [Header("UI Prefabs & Parents")]
    public GameObject textMeshProPrefab;
    public Transform listViewContent;

    [Header("My Content Configuration")]
    [Tooltip("Enable extraction of MyContent.zip into the MyContent folder.")]
    public bool extractMyContentEnabled = false;

    [Tooltip("Name of the ZIP file placed under Assets/StreamingAssets")]
    [SerializeField] private string defaultZipNameMyContent = "MyContent.zip";

    private const string myContentFolder = "MyContent";

    [Header("System Prompts Configuration")]
    [Tooltip("Enable extraction of SystemPrompts.zip into a versioned folder.")]
    public bool extractSystemPromptsEnabled = false;

    [Tooltip("Name of the ZIP file placed under Assets/StreamingAssets")]
    [SerializeField] private string defaultZipNameSystemPrompts = "SystemPrompts.zip";

    // Dynamic folder name: "SystemPrompts_v0.1.0"
    private string systemPromptsFolderName => $"SystemPrompts_v{Application.version}";

    private string previouslySelectedDirectory;

    void Start()
    {
        StartCoroutine(FirstRunExtractAndScan());
    }

    private IEnumerator FirstRunExtractAndScan()
    {
        // 1. MyContent (Preservative)
        string myContentPath = Path.Combine(Application.persistentDataPath, myContentFolder);
        bool isMyContentEmpty = IsDirectoryEmptyOrMissing(myContentPath);

        if (extractMyContentEnabled && isMyContentEmpty)
        {
            Debug.Log($"DirectoryScanner: MyContent missing or empty. Extracting...");
            yield return ExtractZipFile(defaultZipNameMyContent, myContentPath);
        }
        else if (!isMyContentEmpty)
        {
            Debug.Log("DirectoryScanner: MyContent folder populated. Preserving content.");
        }

        // 2. SystemPrompts (Versioned Assets)
        string systemPromptsPath = Path.Combine(Application.persistentDataPath, systemPromptsFolderName);
        bool systemPromptsFolderExists = Directory.Exists(systemPromptsPath);

        if (extractSystemPromptsEnabled && !systemPromptsFolderExists)
        {
            Debug.Log($"DirectoryScanner: SystemPrompts for v{Application.version} not found. Extracting to {systemPromptsFolderName}...");
            yield return ExtractZipFile(defaultZipNameSystemPrompts, systemPromptsPath);
        }

        ScanDirectoriesAndShowInListView(true);
    }

    private bool IsDirectoryEmptyOrMissing(string path)
    {
        if (!Directory.Exists(path)) return true;
        return (Directory.GetFiles(path).Length == 0 && Directory.GetDirectories(path).Length == 0);
    }

    private IEnumerator ExtractZipFile(string zipName, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        string saPath = Path.Combine(Application.streamingAssetsPath, zipName);
        bool isWebRequest = saPath.Contains("://") || saPath.Contains(":///");

        if (isWebRequest)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(saPath))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"DirectoryScanner: Failed to load {zipName}: {www.error}");
                    yield break;
                }

                string tempZip = Path.Combine(Application.temporaryCachePath, zipName);
                File.WriteAllBytes(tempZip, www.downloadHandler.data);
                UnzipUtility.ExtractZipFile(tempZip, destinationPath);
                try { File.Delete(tempZip); } catch { }
            }
        }
        else
        {
            if (File.Exists(saPath))
            {
                UnzipUtility.ExtractZipFile(saPath, destinationPath);
            }
            else
            {
                Debug.LogError($"DirectoryScanner: Cannot find {saPath}.");
                yield break;
            }
        }
    }

    public void ScanDirectoriesAndShowInListView(bool selectFirst = false)
    {
        string path = Path.Combine(Application.persistentDataPath, myContentFolder);

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        List<string> directories = new List<string>(Directory.GetDirectories(path));
        ClearListView();

        ListElementPath firstElement = null;
        ListElementPath selectedElement = null;
        bool anyCompatibleFound = false;

        foreach (string directory in directories)
        {
            ListElementPath elt = InstantiateAndInitializeListItem(directory);
            if (elt == null) continue;

            anyCompatibleFound = true;

            if (firstElement == null) firstElement = elt;
            if (directory == previouslySelectedDirectory) selectedElement = elt;
        }

        if (selectedElement != null)
        {
            selectedElement.OnElementClicked();
        }
        else if (selectFirst && firstElement != null)
        {
            firstElement.OnElementClicked();
        }

        if (!anyCompatibleFound)
        {
            Debug.LogWarning("DirectoryScanner: No compatible configurations found in " + path);
            ListElementPath.currentlySelectedElement = null;
        }
    }

    public void RefreshDirectoryList()
    {
        if (ListElementPath.currentlySelectedElement != null)
            previouslySelectedDirectory = ListElementPath.currentlySelectedElement.GetFilePath();
        else
            previouslySelectedDirectory = null;

        ScanDirectoriesAndShowInListView(false);
    }

    public void RefreshListAndSelectFirst()
    {
        ScanDirectoriesAndShowInListView(true);
    }

    private void ClearListView()
    {
        foreach (Transform t in listViewContent)
            Destroy(t.gameObject);
    }

    private ListElementPath InstantiateAndInitializeListItem(string directory)
    {
        string configFilePath = Path.Combine(directory, "config.yml");

        if (!File.Exists(configFilePath))
        {
            return null;
        }

        try
        {
            Config config = ConfigManager.Instance.LoadConfiguration(configFilePath);

            if (config == null)
            {
                return null;
            }

            if (!ConfigManager.Instance.IsConfigurationCompatible(config))
            {
                Debug.Log($"DirectoryScanner: Skipping incompatible config at {directory}");
                return null;
            }

            GameObject go = Instantiate(textMeshProPrefab, listViewContent);
            var elt = go.GetComponent<ListElementPath>();
            elt.Initialize(directory, config);
            return elt;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"DirectoryScanner: Error loading config from {directory}: {ex.Message}");
            return null;
        }
    }
}