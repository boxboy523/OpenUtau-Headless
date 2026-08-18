using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    /// <summary>
    /// Public synchronous export boundary for non-GUI hosts.
    /// </summary>
    public static class HeadlessRenderer {
        public static void RenderMixdown(
            UProject project,
            string outputPath,
            TaskScheduler scheduler,
            bool applyMixFx = true) {
            string fullOutputPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }
            CancellationTokenSource cancellation = null;
            try {
                var engine = new RenderEngine(project);
                var mix = engine.RenderMixdown(
                    scheduler,
                    ref cancellation,
                    wait: true,
                    applyMixFx: applyMixFx).Item1;
                WaveFileWriter.CreateWaveFile16(fullOutputPath, new ExportAdapter(mix));
            } finally {
                cancellation?.Dispose();
            }
        }
    }
}
