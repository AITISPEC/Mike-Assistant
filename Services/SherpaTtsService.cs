using NAudio.Wave;
using SherpaOnnx;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;

namespace MikeAssistant.Services
{
    public class SherpaTtsService : IDisposable
    {
        private OfflineTts _tts;
        private WaveOutEvent _waveOut;
        private readonly object _lock = new object();
        private bool _disposed;

        public event Action<string> OnError;

        public void Initialize(string modelPath, string tokensPath, string dataDir)
        {
            lock (_lock)
            {
                if (_tts != null) return;
                var config = new OfflineTtsConfig();
                config.Model = new OfflineTtsModelConfig();
                config.Model.Vits = new OfflineTtsVitsModelConfig();
                config.Model.Vits.Model = modelPath;
                config.Model.Vits.Tokens = tokensPath;
                config.Model.Vits.DataDir = dataDir;
                if (!string.IsNullOrEmpty(dataDir))
                    config.Model.Vits.DataDir = dataDir;
                config.Model.Provider = "cpu";
                config.Model.NumThreads = 1;
                config.Model.Debug = 0;
                _tts = new OfflineTts(config);
            }
        }

        public async Task SpeakAsync(string text, float speed = 1.0f, int speakerId = 0)
        {
            if (_tts == null)
            {
                OnError?.Invoke("TTS not initialized");
                return;
            }

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    try
                    {
                        var audio = _tts.Generate(text, speed, speakerId);
                        if (audio?.Samples == null || audio.Samples.Length == 0) return;

                        var waveFormat = new WaveFormat(audio.SampleRate, 16, 1);
                        int byteCount = audio.Samples.Length * 2;
                        byte[] byteSamples = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount);
                        try
                        {
                            for (int i = 0; i < audio.Samples.Length; i++)
                            {
                                short sample = (short)(audio.Samples[i] * 32767);
                                var bytes = BitConverter.GetBytes(sample);
                                byteSamples[i * 2] = bytes[0];
                                byteSamples[i * 2 + 1] = bytes[1];
                            }

                            using (var ms = new MemoryStream(byteSamples, 0, byteCount))
                            using (var waveStream = new RawSourceWaveStream(ms, waveFormat))
                            {
                                if (_waveOut == null)
                                    _waveOut = new WaveOutEvent();
                                else if (_waveOut.PlaybackState == PlaybackState.Playing)
                                    _waveOut.Stop();

                                _waveOut.Init(waveStream);
                                _waveOut.Play();

                                // Ожидание завершения без блокировки потока пула
                                var tcs = new TaskCompletionSource<bool>();
                                EventHandler<StoppedEventArgs> handler = null;
                                handler = (s, e) =>
                                {
                                    _waveOut.PlaybackStopped -= handler;
                                    tcs.SetResult(true);
                                };
                                _waveOut.PlaybackStopped += handler;
                                tcs.Task.Wait();
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(byteSamples);
                        }
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke($"TTS speak error: {ex.Message}");
                    }
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _tts?.Dispose();
            }
        }
    }
}