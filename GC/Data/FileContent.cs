namespace GC.Data;

public readonly struct FileContent
{
    public readonly FileEntry Entry;
    public readonly string Content;
    public readonly long Size;

    public FileContent(FileEntry entry, string content, long size)
    {
        Entry = entry;
        Content = content;
        Size = size;
    }
}
