using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Cli;

internal static class Program {
    private const int Success = 0;
    private const int InvalidArguments = 2;
    private const int InvalidProject = 3;
    private const int MissingSinger = 4;
    private const int RenderFailure = 5;

    public static int Main(string[] args) {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try {
            if (args.Length == 0 || args[0] is "-h" or "--help") {
                PrintUsage();
                return args.Length == 0 ? InvalidArguments : Success;
            }
            return args[0] switch {
                "render" => Render(ParseOptions(args[1..])),
                "list-singers" => ListSingers(ParseOptions(args[1..])),
                _ => Fail(InvalidArguments, $"Unknown command: {args[0]}"),
            };
        } catch (ArgumentException exception) {
            return Fail(InvalidArguments, exception.Message);
        } catch (Exception exception) {
            return Fail(RenderFailure, exception.ToString());
        }
    }

    private static int Render(IReadOnlyDictionary<string, string?> options) {
        string projectPath = Require(options, "--project");
        string outputPath = Require(options, "--output");
        Initialize(options);

        UProject project;
        try {
            project = Ustx.Load(Path.GetFullPath(projectPath), validate: false);
        } catch (Exception exception) {
            return Fail(InvalidProject, exception.ToString());
        }
        var missing = project.tracks
            .Where(track => track.Singer == null || !track.Singer.Found)
            .Select(track => track.singer ?? track.TrackName)
            .Distinct()
            .ToArray();
        if (missing.Length > 0) {
            return Fail(MissingSinger, $"Missing singers: {string.Join(", ", missing)}");
        }
        try {
            HeadlessRenderer.RenderMixdown(
                project,
                outputPath,
                TaskScheduler.Default,
                applyMixFx: !options.ContainsKey("--no-mix-fx"));
        } catch (Exception exception) {
            return Fail(RenderFailure, exception.ToString());
        }
        WriteJson(new {
            status = "succeeded",
            project = Path.GetFullPath(projectPath),
            output = Path.GetFullPath(outputPath),
            singers = project.tracks.Select(track => track.Singer?.Id).ToArray(),
        });
        return Success;
    }

    private static int ListSingers(IReadOnlyDictionary<string, string?> options) {
        Initialize(options);
        WriteJson(new {
            singers = SingerManager.Inst.Singers.Values
                .OrderBy(singer => singer.Id)
                .Select(singer => new { singer.Id, singer.Name, type = singer.SingerType.ToString() })
                .ToArray(),
        });
        return Success;
    }

    private static void Initialize(IReadOnlyDictionary<string, string?> options) {
        if (options.TryGetValue("--data-path", out string? dataPath) && dataPath != null) {
            options.TryGetValue("--cache-path", out string? cachePath);
            PathManager.Inst.ConfigureDataPath(dataPath, cachePath);
        }
        ToolsManager.Inst.Initialize();
        SingerManager.Inst.Initialize();
        DocManager.Inst.InitializeHeadless(TaskScheduler.Default);
    }

    private static Dictionary<string, string?> ParseOptions(string[] args) {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++) {
            string key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal)) {
                throw new ArgumentException($"Unexpected argument: {key}");
            }
            if (key == "--no-mix-fx") {
                options[key] = null;
                continue;
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
                throw new ArgumentException($"Missing value for {key}");
            }
            if (!options.TryAdd(key, args[++index])) {
                throw new ArgumentException($"Duplicate option: {key}");
            }
        }
        return options;
    }

    private static string Require(IReadOnlyDictionary<string, string?> options, string key) {
        if (!options.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException($"Required option missing: {key}");
        }
        return value;
    }

    private static int Fail(int code, string message) {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", code, error = message }));
        return code;
    }

    private static void WriteJson<T>(T payload) {
        Console.Out.WriteLine(JsonSerializer.Serialize(payload));
    }

    private static void PrintUsage() {
        Console.Out.WriteLine(
            "Usage:\n" +
            "  openutau-cli render --project FILE --output FILE [--data-path DIR] " +
            "[--cache-path DIR] [--no-mix-fx]\n" +
            "  openutau-cli list-singers [--data-path DIR] [--cache-path DIR]");
    }
}
