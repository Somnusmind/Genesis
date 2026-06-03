using UnityEngine;
using System.IO;

public static class ConfigPath
{
    private static string baseFolderPath = "MyContent";
    private static string rootFolderPath = "";

    public static string Path => GetConfigFilePath();

    public static void UpdateBaseFolderPath(string newBaseFolderName)
    {
        baseFolderPath = newBaseFolderName;
        EnsureConfigExists();
        ConfigPathEvents.RaiseConfigPathUpdated(GetConfigFilePath());
    }

    public static string GetConfigFilePath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, baseFolderPath, "config.yml");
    }

    public static string GetMediaFolderPath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, baseFolderPath, "media");
    }

    public static string GetMediaFilePath(string mediaFileName)
    {
        return System.IO.Path.Combine(GetMediaFolderPath(), mediaFileName);
    }

    private static void EnsureConfigExists()
    {
        string folderPath = System.IO.Path.Combine(Application.persistentDataPath, baseFolderPath);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Debug.Log($"Folder created at: {folderPath}");
        }

        // REMOVED: Automatic creation of config.yml with "initial_content: true"
        // This logic was creating invalid config files in root directories when scanning.
        // Configs should only be created by the ConfigCreator or Importer.
    }

    public static string GetCacheFolderPath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, baseFolderPath, ".cache");
    }

    public static void EnsureCacheFolderExists()
    {
        string cacheFolderPath = GetCacheFolderPath();
        if (!Directory.Exists(cacheFolderPath))
        {
            Directory.CreateDirectory(cacheFolderPath);
            Debug.Log($"Cache folder created: {cacheFolderPath}");
        }
    }

    public static string GetDataRecordsFolderPath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, rootFolderPath, "DataRecords");
    }

    public static void EnsureDataRecordsFolderExists()
    {
        string dataRecordsFolderPath = GetDataRecordsFolderPath();
        if (!Directory.Exists(dataRecordsFolderPath))
        {
            Directory.CreateDirectory(dataRecordsFolderPath);
            Debug.Log($"DataRecords folder created: {dataRecordsFolderPath}");
        }
    }
}

public static class ConfigPathEvents
{
    public static event System.Action<string> OnConfigPathUpdated;

    public static void RaiseConfigPathUpdated(string newPath)
    {
        OnConfigPathUpdated?.Invoke(newPath);
    }
}