namespace GC.Data;


public readonly struct FileEntry
{
    public readonly string Path;
    public readonly string Extension;
    public readonly string Language;

    public FileEntry(string path, string extension, string language)
    {
        Path = path;
        Extension = extension;
        Language = language;
    }
}