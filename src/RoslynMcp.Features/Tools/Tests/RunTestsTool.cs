using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using ModelContextProtocol.Server;

namespace RoslynMcp.Features.Tools.Tests;

public sealed class RunTestsTool : Tool
{
    [McpServerTool(Name = "run_tests", Title = "Run Tests", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to run all tests.")]
    public Task<IReadOnlyList<TestResult>?> ExecuteAsync()
    {
        const string trxFile = "FailureReport.trx";
        const string jsonFile = "FailureReport.json";
        
        var dir = Directory.GetCurrentDirectory();
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test {dir} --logger \"trx;LogFileName={trxFile}\" --results-directory \"{dir}\"",
            WorkingDirectory = dir,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        
        process.StartInfo = startInfo;
        process.Start();
        
        process.WaitForExit();

        if(Directory.GetFiles(dir, "*" + jsonFile, SearchOption.AllDirectories).FirstOrDefault() is { } file)
            return Task.FromResult(ParseJsonFile(file));
        
        return Task.FromResult(ParseTrxFile(trxFile));
    }

    private static IReadOnlyList<TestResult>? ParseTrxFile(string file)
    {
        if (!File.Exists(file))
            return new List<TestResult>();
        
        var doc = XDocument.Load(file);

        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        var fails = doc.Descendants(ns + "UnitTestResult")
            .Where(x => (string)x.Attribute("outcome") == "Failed")
            .Select(x => new
            {
                TestName = (string)x.Attribute("testName"),
                Duration = (string)x.Attribute("duration"),
                ErrorMessage = x.Element(ns + "Output")?
                    .Element(ns + "ErrorInfo")?
                    .Element(ns + "Message")?.Value,
                StackTrace = x.Element(ns + "Output")?
                    .Element(ns + "ErrorInfo")?
                    .Element(ns + "StackTrace")?.Value
            })
            .ToList();

        return fails.ConvertAll(f => new TestResult { Message = f.ErrorMessage });
    }

    private static IReadOnlyList<TestResult>? ParseJsonFile(string file)
    {
        if (!File.Exists(file))
            return new List<TestResult>();
        
        var json = File.ReadAllText(file);
        
        var results = JsonSerializer.Deserialize<List<TestResult>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return results;
    }
    
    public record TestResult
    {
        public string? Message { get; set; }
        public object Actual { get; set; }
        public object Expected { get; set; }
        public string Method { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
        public string Code { get; set; }
    }
}
