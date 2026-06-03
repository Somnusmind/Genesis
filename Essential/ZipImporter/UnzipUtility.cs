using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using UnityEngine;

public class UnzipUtility : MonoBehaviour
{
    public static void ExtractZipFile(string zipFilePath, string location)
    {
        if (!Directory.Exists(location))
            Directory.CreateDirectory(location);

        using (ZipInputStream s = new ZipInputStream(File.OpenRead(zipFilePath)))
        {
            ZipEntry theEntry;
            while ((theEntry = s.GetNextEntry()) != null)
            {
                string directoryName = Path.GetDirectoryName(theEntry.Name);
                string fileName = Path.GetFileName(theEntry.Name);

                if (!string.IsNullOrEmpty(directoryName))
                {
                    Directory.CreateDirectory(Path.Combine(location, directoryName));
                }

                if (!string.IsNullOrEmpty(fileName))
                {
                    string fullPath = Path.Combine(location, theEntry.Name);
                    string fullDirectory = Path.GetDirectoryName(fullPath);

                    if (!string.IsNullOrEmpty(fullDirectory) && !Directory.Exists(fullDirectory))
                    {
                        Directory.CreateDirectory(fullDirectory);
                    }

                    using (FileStream streamWriter = File.Create(fullPath))
                    {
                        int size = 2048;
                        byte[] data = new byte[2048];
                        while (true)
                        {
                            size = s.Read(data, 0, data.Length);
                            if (size > 0)
                            {
                                streamWriter.Write(data, 0, size);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}