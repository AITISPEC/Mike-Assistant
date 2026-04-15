using Microsoft.Speech.Recognition;
using MikeAssistant.Core;
using MikeAssistant.IO;
using MikeAssistant.Processing;
using MikeAssistant.Recognition;
using MikeAssistant.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MikeAssistant
{
    public partial class MainWindow : Window
    {
        private TrayIconManager _trayManager;
        private SherpaTtsService _tts;
        private DialogContext _dialogContext;
        private LmStudioHealthCheck _lmHealthCheck;
        private LmStudioClient _lmClient;
        private GoogleSearchClient _googleClient;
        private DispatcherTimer _waveTimer;
        private DialogCommandProcessor _commandProcessor;
        private SpeechRecognitionService _sapiService;
        private SherpaSpeechService _sherpaService;
        private double _timeOffset = 0;
        private MikeMode _currentMode = MikeMode.Normal;

        public MainWindow()
        {
            InitializeComponent();
            _trayManager = new TrayIconManager(this);
            Visibility = Visibility.Hidden;

            InitializeWaveAnimation();

            var config = Core.AppConfig.Load();
            InitializeServices(config);
            CheckAllStatus();

            MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is Button) return;
                DragMove();
            };

            Closing += MainWindow_Closing;
        }

        private void InitializeServices(Core.AppConfig config)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string sherpaPath = Path.GetFullPath(Path.Combine(baseDir, "..", "sherpa"));

            string modelPath = Path.Combine(sherpaPath, "stt", "model.onnx");
            string tokensPath = Path.Combine(sherpaPath, "stt", "tokens.txt");

            string ttsModelPath = Path.Combine(sherpaPath, "tts", "model.onnx");
            string ttsTokensPath = Path.Combine(sherpaPath, "tts", "tokens.txt");
            string espeakDataPath = Path.Combine(sherpaPath, "tts", "data");
            _tts = new SherpaTtsService();
            _tts.OnError += (msg) => UpdateInfo($"TTS error: {msg}");

            try
            {
                _tts.Initialize(ttsModelPath, ttsTokensPath, espeakDataPath);
            }
            catch (Exception ex)
            {
                UpdateInfo($"TTS error: {ex.Message}");
            }

            _sapiService = new SpeechRecognitionService();
            _sapiService.SpeechRecognized += OnSpeechRecognized;
            _sapiService.SpeechRejected += OnSpeechRejected;
            _sapiService.ErrorOccurred += (s, ex) => UpdateInfo($"Ошибка распознавания: {ex.Message}");
            _sapiService.StartPassMode();

            _dialogContext = new DialogContext(5);
            _googleClient = new GoogleSearchClient();
            _lmClient = new LmStudioClient(config.LmStudioUrl);
            _lmClient.SetPreset("mike_default");
            _lmHealthCheck = new LmStudioHealthCheck(config.LmStudioUrl);

            _commandProcessor = new DialogCommandProcessor(
                _dialogContext, _googleClient, _lmHealthCheck, _lmClient,
                UpdateInfo, Speak, EndDialogMode, CheckAllStatus, SetMikeMode);

            _sherpaService = new SherpaSpeechService();
            _sherpaService.OnFinalResult += OnSherpaFinalResult;
            _sherpaService.OnError += OnSherpaError;
            _sherpaService.OnPartialResult += (text) => Dispatcher.Invoke(() => UpdateInfo($"Распознаётся: {text}"));
        }

        private void SetMikeMode(Core.MikeMode mode)
        {
            _currentMode = mode;
            Speak(mode == Core.MikeMode.Normal ? "Режим Майка" : "Режим нейросети");
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            var current = Application.Current.Resources.MergedDictionaries[0].Source?.OriginalString;
            string newTheme = current?.Contains("LightTheme") == true ? "DarkTheme.xaml" : "LightTheme.xaml";
            var uri = new Uri($"Themes/{newTheme}", UriKind.Relative);
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary() { Source = uri };
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void EndDialogMode()
        {
            _currentMode = MikeMode.Normal;

            if (_sherpaService != null)
            {
                _sherpaService.OnFinalResult -= OnSherpaFinalResult;
                _sherpaService.OnError -= OnSherpaError;
                _sherpaService.Stop();
            }

            _sapiService.StartPassMode();
            _waveTimer.Stop();
            _trayManager.SetActiveMode(false);
            UpdateInfo("Ожидаю...");
        }

        private async void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            string text = e.Result.Text;
            string ruleName = e.Result.Grammar?.Name ?? "unknown";

            Debug.WriteLine($"[SAPI] Recognized: '{text}', Grammar: {ruleName}");
            Dispatcher.Invoke(() => InfoText.Text = $"Распознано: {text}");
            _dialogContext.AddUserMessage(text);

            if (ruleName == "PassGrammar")
            {
                if (text == "отбой")
                {
                    EndDialogMode();
                }
                else if (text == "алло майк")
                {
                    _currentMode = MikeMode.Normal;
                    StartSherpaAndListen();
                }
                else if (text == "алло квен")
                {
                    _currentMode = MikeMode.LlmRelay;
                    StartSherpaAndListen();
                }
                return;
            }

            if (_sapiService.IsDialogMode)
            {
                await _commandProcessor.ProcessCommandAsync(text, e.Result);
            }
        }

        private void StartSherpaAndListen()
        {
            try
            {
                UpdateInfo("Запуск Sherpa...");
                _sapiService.Stop();

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string sherpaPath = Path.GetFullPath(Path.Combine(baseDir, "..", "sherpa"));
                string modelPath = Path.Combine(sherpaPath, "stt", "model.onnx");
                string tokensPath = Path.Combine(sherpaPath, "stt", "tokens.txt");

                _sherpaService.Start(modelPath, tokensPath);

                _trayManager.SetActiveMode(true);
                _waveTimer.Start();
                UpdateInfo("Sherpa активен. Говорите...");
                Speak("Я слушаю");
            }
            catch (Exception ex)
            {
                UpdateInfo($"Sherpa error: {ex.Message}");
                _sapiService.StartPassMode();
            }
        }

        private async void OnSherpaFinalResult(string text)
        {
            if (_sherpaService == null || !_sherpaService.IsRunning) return;

            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"[Sherpa] Final: {text}");
                _dialogContext.AddUserMessage(text);

                if (_currentMode == Core.MikeMode.Normal)
                {
                    string response = await _commandProcessor.ProcessCommandAsync(text, null);
                    if (response == null)
                        Speak("Извините, я не понял.");
                }
                else if (_currentMode == Core.MikeMode.LlmRelay)
                {
                    string lowerText = text.Trim().ToLower();

                    // 1. Команда выхода
                    if (lowerText == "отбой")
                    {
                        EndDialogMode();
                        return;
                    }

                    // 2. Команды переключения режима
                    if (lowerText == "алло майк")
                    {
                        _currentMode = Core.MikeMode.Normal;
                        Speak("Режим Майка");
                        return;
                    }
                    if (lowerText == "алло квен")
                    {
                        _currentMode = Core.MikeMode.LlmRelay;
                        Speak("Режим нейросети");
                        return;
                    }

                    // 3. Отправка в LLM с поддержкой контекста (без ручной истории)
                    string llmResponse = null;
                    if (_lmHealthCheck.IsServerReachable && _lmHealthCheck.IsModelLoaded)
                    {
                        llmResponse = await _lmClient.SendMessageWithContextAsync(text, resetContext: false);
                        if (string.IsNullOrEmpty(llmResponse))
                        {
                            llmResponse = "Не удалось получить ответ от нейросети.";
                        }
                    }
                    else
                    {
                        llmResponse = "Нейросеть недоступна.";
                    }
                    Speak(llmResponse);
                }
            });
        }

        private void OnSherpaError(string msg)
        {
            Dispatcher.Invoke(() => UpdateInfo($"Sherpa: {msg}"));
        }

        private void OnSpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e)
            => UpdateInfo("Не распознано. Повторите.");

        private async void Speak(string text)
        {
            bool wasSherpaRunning = _sherpaService != null && _sherpaService.IsRunning;
            if (wasSherpaRunning)
                _sherpaService.PauseRecording();

            try
            {
                if (_tts == null) return;
                _waveTimer?.Start();
                await _tts.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                UpdateInfo($"TTS error: {ex.Message}");
            }
            finally
            {
                _waveTimer?.Stop();
                if (wasSherpaRunning && _sherpaService != null)
                    _sherpaService.ResumeRecording();
            }
        }

        private void UpdateInfo(string message) => Dispatcher.Invoke(() => InfoText.Text = message);

        private void CheckAllStatus()
        {
            _lmHealthCheck.CheckHealth();
            UpdateLmStudioStatus();
            UpdateInternetStatus();
        }

        private void UpdateLmStudioStatus() => Dispatcher.Invoke(() =>
            StatusIndicator.Fill = _lmHealthCheck.IsServerReachable && _lmHealthCheck.IsModelLoaded ? Brushes.Green :
                                    _lmHealthCheck.IsServerReachable ? Brushes.Orange : Brushes.Red);

        private void UpdateInternetStatus() => Dispatcher.Invoke(() =>
            InternetIndicator.Fill = _googleClient.IsInternetAvailable() ? Brushes.Green : Brushes.Red);

        private void InitializeWaveAnimation()
        {
            _waveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _waveTimer.Tick += AnimateWave;
        }

        private void AnimateWave(object sender, EventArgs e)
        {
            WaveCanvas.Children.Clear();
            double width = WaveCanvas.ActualWidth;
            double height = WaveCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            int bars = 40;
            double barWidth = width / bars;
            double centerY = height / 2;
            double amplitude = 15;

            _timeOffset += 0.15;

            for (int i = 0; i < bars; i++)
            {
                double x = i * barWidth;
                double y = centerY + Math.Sin(i * 0.3 + _timeOffset) * amplitude;
                double barHeight = 4 + Math.Sin(i * 0.5 + _timeOffset) * amplitude * 0.6;

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = barWidth - 1,
                    Height = Math.Abs(barHeight),
                    Fill = (Brush)FindResource("WaveBrush") ?? Brushes.SteelBlue,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, centerY - barHeight / 2);
                WaveCanvas.Children.Add(rect);
            }
        }

        private async void LmStudioStatusButton_Click(object sender, RoutedEventArgs e)
        {
            string presetId = "mike_default";
            var dialog = new LmStudioSettingsWindow(_lmHealthCheck, _lmClient, presetId);
            if (dialog.ShowDialog() == true)
            {
                if (!string.IsNullOrEmpty(dialog.SelectedModelKey))
                {
                    _lmClient.SetCurrentModel(dialog.SelectedModelKey);
                    _lmClient.SetPreset(dialog.SelectedPresetId);
                    UpdateInfo($"Модель LM Studio: {dialog.SelectedModelKey}");
                    CheckAllStatus();
                }
                else
                {
                    UpdateInfo("Модель не выбрана. Используйте стандартную.");
                }
            }
        }

        private void RefreshStatusButton_Click(object sender, RoutedEventArgs e) => CheckAllStatus();

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Visibility = Visibility.Hidden;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
                _trayManager.HideWindow();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_sherpaService != null)
            {
                _sherpaService.OnFinalResult -= OnSherpaFinalResult;
                _sherpaService.OnError -= OnSherpaError;
            }
            if (_sapiService != null)
            {
                _sapiService.SpeechRecognized -= OnSpeechRecognized;
                _sapiService.SpeechRejected -= OnSpeechRejected;
            }
            if (_tts != null)
            {
                _tts.OnError -= (msg) => UpdateInfo($"TTS error: {msg}");
            }

            _sherpaService?.Stop();
            _sapiService?.Stop();

            _sherpaService?.Dispose();
            _sapiService?.Dispose();
            _tts?.Dispose();
            _lmHealthCheck?.Dispose();
            _lmClient?.Dispose();
            _trayManager?.Dispose();

            base.OnClosed(e);
        }
    }
}