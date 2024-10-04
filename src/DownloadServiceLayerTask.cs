using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Build.Utilities;

namespace Kent.DbCli;

public class DownloadServiceLayerTask : Task
{   
    static readonly (string, string)[] Tarballs = new[]
    {
        ("osx-arm64", "https://github.com/microsoft/sqltoolsservice/releases/download/5.0.20241003.1/Microsoft.SqlTools.ServiceLayer-osx-arm64-net8.0.tar.gz"),
        ("linux-x64", "https://github.com/microsoft/sqltoolsservice/releases/download/5.0.20241003.1/Microsoft.SqlTools.ServiceLayer-linux-x64-net8.0.tar.gz"),
        ("win-x64", "https://github.com/microsoft/sqltoolsservice/releases/download/5.0.20241003.1/Microsoft.SqlTools.ServiceLayer-win-x64-net8.0.zip"),
    };

    public string ServiceLayerPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        Console.Write("Downloading Microsoft.SqlTools.ServiceLayer... ");
        
        if (string.IsNullOrEmpty(ServiceLayerPath))
        {
            Console.WriteLine($"{ServiceLayerPath} was not set.");
            return false;
        }

        if (!Directory.Exists(ServiceLayerPath))
        {
            Directory.CreateDirectory(ServiceLayerPath);
        }

        var handler = new HttpClientHandler()
        {
            AllowAutoRedirect = true,
        };
        using var client = new HttpClient(handler, disposeHandler: true);

        foreach (var (dir, url) in Tarballs)
        {
            var name = Path.GetFileName(url);
            var filePath = Path.GetFullPath(Path.Combine(ServiceLayerPath, name));
            var extractDir = Path.GetFullPath(Path.Combine(ServiceLayerPath, dir));

            if (!Directory.Exists(extractDir))
            {
                Directory.CreateDirectory(extractDir);
            }
            
            if (!File.Exists(filePath))
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                var res = client.SendAsync(req).Result;

                using (var handle = File.OpenWrite(filePath))
                {
                    res.Content.ReadAsStreamAsync()
                        .Result
                        .CopyToAsync(handle)
                        .Wait();
                }
            }

            if (Directory.GetFiles(extractDir).Length == 0)
            {
                var ext = Path.GetExtension(filePath);
                switch (ext)
                {
                    case ".gz":
                        InvokeProcess("tar", "-xzf", filePath, "-C", extractDir);
                        break;
                    case ".zip":
                        ZipFile.ExtractToDirectory(filePath, extractDir);
                        break;
                }
            }
        }

        Console.WriteLine("OK");

        return true;
    }

    static string InvokeProcess(string bin, params string[] args)
    {
        var opts = new ProcessStartInfo(bin)
        {
            Arguments = string.Join(" ", args),
            // Arguments = string.Join(' ', args),
            UseShellExecute = false,

            // if we don't redirect the output 'Process' prints everything to stdout.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using (var proc = Process.Start(opts))
        {
            proc!.WaitForExit();

            var messages = new[] { proc.StandardOutput.ReadToEnd(), proc.StandardError.ReadToEnd() }
                .Where(m => !string.IsNullOrEmpty(m));

            var message = string.Join(Environment.NewLine, messages);

            if (proc.ExitCode != 0)
            {
                var name = Path.GetFileName(bin);
                throw new Exception($"{name} exited with code {proc.ExitCode}\n{message}");
            }

            return message;
        }
    }
}
