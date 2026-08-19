using System;
using System.IO;
using System.Linq;
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
            PrepareProject(project);
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

        private static void PrepareProject(UProject project) {
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var parts = project.parts.OfType<UVoicePart>().Where(part => part.notes.Count > 0).ToArray();
            using var waiter = new PhonemizedWaiter();
            DocManager.Inst.AddSubscriber(waiter);
            try {
                project.ValidateFull();
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                do {
                    int remainingMs = Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (remainingMs == 0 || !waiter.Wait(Math.Min(remainingMs, 250))) {
                        if (parts.All(part => part.PhonemesUpToDate && part.renderPhrases.Count > 0)) {
                            break;
                        }
                        if (remainingMs == 0) {
                            throw new TimeoutException("Timed out waiting for phonemization and render phrases.");
                        }
                    }
                } while (true);
                // Debounce duplicate requests queued while loading the USTX.
                while (waiter.Wait(100)) { }
            } finally {
                DocManager.Inst.RemoveSubscriber(waiter);
            }
            if (parts.Sum(part => part.phonemes.Count) == 0) {
                throw new InvalidDataException("Phonemization produced no renderable phonemes.");
            }
            if (parts.Sum(part => part.renderPhrases.Count) == 0) {
                string errors = string.Join("; ", parts
                    .SelectMany(part => part.phonemes)
                    .Where(phoneme => phoneme.Error)
                    .Select(phoneme => phoneme.ErrorException?.Message)
                    .Where(message => !string.IsNullOrEmpty(message))
                    .Distinct()
                    .Take(10));
                throw new InvalidDataException($"Phonemization produced no render phrases. {errors}");
            }
        }

        private sealed class PhonemizedWaiter : ICmdSubscriber, IDisposable {
            private readonly AutoResetEvent signal = new(false);

            public void OnNext(UCommand command, bool isUndo) {
                if (command is PhonemizedNotification) {
                    signal.Set();
                }
            }

            public bool Wait(int millisecondsTimeout) => signal.WaitOne(millisecondsTimeout);

            public void Dispose() => signal.Dispose();
        }
    }
}
