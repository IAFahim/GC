using System.IO;

namespace GC.Data;


public readonly struct FileEntry
{
    public readonly string Path;
    public readonly string Extension;
    public readonly string Language;
    public readonly long Size;

    public FileEntry(string path, string extension, string language)
    {
        Path = path;
        Extension = extension;
        Language = language;
        Size = File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    public FileEntry(string path, string extension, string language, long size)
    {
        Path = path;
        Extension = extension;
        Language = language;
        Size = size;
    }
}