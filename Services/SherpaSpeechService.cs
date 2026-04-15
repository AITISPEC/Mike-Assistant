using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using SherpaOnnx;

namespace MikeAssistant.Services
{
    public class SherpaSpeechService : IDisposable
    {
        private OnlineRecognizer _recognizer;
        private OnlineStream _stream;
        private WaveInEvent _waveIn;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private bool _isStopping;
        private readonly object _lock = new object();
        private string _lastText = string.Empty;
        private Task _processLoopTask;

        public event Action<string> OnPartialResult;
        public event Action<string> OnFinalResult;
        public event Action<string> OnError;

        public bool IsRunning => _isRunning;
        public bool IsDialogMode => false;

        public void Start(string modelPath, string tokensPath)
        {
            lock (_lock)
            {
                if (_isRunning) return;

                try
                {
                    var config = new OnlineRecognizerConfig();
                    config.FeatConfig.SampleRate = 16000;
                    config.FeatConfig.FeatureDim = 80;

                    config.ModelConfig.ToneCtc.Model = modelPath;
                    config.ModelConfig.Tokens = tokensPath;

                    //config.ModelConfig.Provider = "cuda";
                    config.ModelConfig.NumThreads = 4;
                    config.ModelConfig.Debug = 0;

                    config.DecodingMethod = "greedy_search";
                    config.MaxActivePaths = 4;

                    config.EnableEndpoint = 1;
                    config.Rule1MinTrailingSilence = 2.0f;
                    config.Rule2MinTrailingSilence = 1.0f;
                    config.Rule3MinUtteranceLength = 20.0f;

                    _recognizer = new OnlineRecognizer(config);
                    _stream = _recognizer.CreateStream();

                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = 0,
                        WaveFormat = new WaveFormat(16000, 1)
                    };
                    _waveIn.DataAvailable += OnDataAvailable;

                    _cts = new CancellationTokenSource();
                    _isRunning = true;
                    _isStopping = false;

                    _waveIn.StartRecording();
                    _processLoopTask = Task.Run(ProcessLoop);

                    Debug.WriteLine("[Sherpa] Service started");
                    OnError?.Invoke("Распознавание запущено");
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Init error: {ex.Message}");
                    Debug.WriteLine($"[Sherpa] Init error: {ex.Message}");
                }
            }
        }

        public void PauseRecording()
        {
            lock (_lock)
            {
                if (_waveIn != null)
                    _waveIn.DataAvailable -= OnDataAvailable;
            }
        }

        public void ResumeRecording()
        {
            lock (_lock)
            {
                if (_waveIn != null && _isRunning && !_isStopping)
                    _waveIn.DataAvailable += OnDataAvailable;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isStopping = true;
                _isRunning = false;
            }

            _cts?.Cancel();

            // Останавливаем запись
            var waveIn = _waveIn;
            if (waveIn != null)
            {
                try { waveIn.StopRecording(); } catch { }
                waveIn.DataAvailable -= OnDataAvailable;
                try { waveIn.Dispose(); } catch { }
                _waveIn = null;
            }

            // Дожидаемся завершения ProcessLoop
            try { _processLoopTask?.Wait(1000); } catch { }

            lock (_lock)
            {
                if (_recognizer != null)
                {
                    _recognizer.Dispose();
                    _recognizer = null;
                }
                _stream = null;
            }

            Debug.WriteLine("[Sherpa] Service stopped");
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (!_isRunning) return;

            lock (_lock)
            {
                if (_recognizer == null || _stream == null) return;

                try
                {
                    var floats = new float[e.BytesRecorded / 2];
                    float gain = 5.0f;
                    for (int i = 0; i < floats.Length; i++)
                    {
                        short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                        float amplified = sample * gain / 32768f;
                        if (amplified > 1f) amplified = 1f;
                        if (amplified < -1f) amplified = -1f;
                        floats[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                    }
                    _stream.AcceptWaveform(16000, floats);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Sherpa] Data error: {ex.Message}");
                }
            }
        }

        private void ProcessLoop()
        {
            bool lastWasEndpoint = false;

            while (_isRunning && !_isStopping)
            {
                Thread.Sleep(100);

                lock (_lock)
                {
                    if (_recognizer == null || _stream == null) continue;

                    try
                    {
                        while (_recognizer.IsReady(_stream))
                            _recognizer.Decode(_stream);

                        var text = _recognizer.GetResult(_stream).Text;
                        bool isEndpoint = _recognizer.IsEndpoint(_stream);

                        if (!string.IsNullOrWhiteSpace(text) && _lastText != text)
                        {
                            _lastText = text;
                            OnPartialResult?.Invoke(text);
                        }

                        if (isEndpoint && !lastWasEndpoint && !_isStopping)
                        {
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                OnFinalResult?.Invoke(text);
                            }
                            _recognizer.Reset(_stream);
                            _lastText = string.Empty;
                            lastWasEndpoint = true;
                        }
                        else if (!isEndpoint)
                        {
                            lastWasEndpoint = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Sherpa] Loop error: {ex.Message}");
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}