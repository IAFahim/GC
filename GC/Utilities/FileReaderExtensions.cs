using System;
using System.IO;
using System.Linq;
using GC.Data;

namespace GC.Utilities;

public static class FileReaderExtensions
{
    public static FileContent[] ReadContents(this FileEntry[] entries)
    {
        return entries
            .AsParallel()
            .Where(entry => File.Exists(entry.Path))
            .Select(entry =>
            {
                var fileInfo = new FileInfo(entry.Path);
                if (fileInfo.Length == 0 || fileInfo.Length > Constants.MaxFileSize)
                {
                    return (FileContent?)null;
                }

                try
                {
                    var text = File.ReadAllText(entry.Path);
                    return new FileContent(entry, text, fileInfo.Length);
                }
                catch
                {
                    return (FileContent?)null;
                }
            })
            .OfType<FileContent>()
            .ToArray();
    }
}