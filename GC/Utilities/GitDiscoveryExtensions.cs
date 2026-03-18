using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GC.Data;

namespace GC.Utilities;

public static class GitDiscoveryExtensions
{
    public static string[] DiscoverFiles(this CliArguments args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "ls-files -z --cached --others --exclude-standard",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return[];
        }

        var files = new List<string>(1024);
        var buffer = new byte[4096];
        var currentFile = new List<byte>(256);

        using var stream = process.StandardOutput.BaseStream;
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    files.Add(Encoding.UTF8.GetString(currentFile.ToArray()));
                    currentFile.Clear();
                }
                else
                {
                    currentFile.Add(buffer[i]);
                }
            }
        }

        process.WaitForExit();
        return files.ToArray();
    }
}