using System;
using System.Globalization;
using Microsoft.Speech.Recognition;
using MikeAssistant.Commands;

namespace MikeAssistant.Recognition
{
    public class SpeechRecognitionService : IDisposable
    {
        private SpeechRecognitionEngine _engine;
        private Grammar _passGrammar;
        private Grammar _dialogGrammar;
        private bool _isDialogMode;
        private bool _disposed;

        public event EventHandler<SpeechRecognizedEventArgs> SpeechRecognized;
        public event EventHandler<SpeechRecognitionRejectedEventArgs> SpeechRejected;
        public event EventHandler<Exception> ErrorOccurred;

        public bool IsDialogMode => _isDialogMode;

        public SpeechRecognitionService()
        {
            try
            {
                _engine = new SpeechRecognitionEngine(new CultureInfo("ru-RU"));
                _engine.SetInputToDefaultAudioDevice();
                _engine.SpeechRecognized += OnSpeechRecognized;
                _engine.SpeechRecognitionRejected += OnSpeechRejected;
                BuildGrammars();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }

        private void BuildGrammars()
        {
            _passGrammar = new Grammar(new GrammarBuilder(new Choices("алло майк", "алло квен", "отбой"))) { Name = "PassGrammar" };
            _dialogGrammar = new Grammar(new GrammarBuilder(CommandLists.BuildDialogChoices())) { Name = "DialogGrammar" };
        }

        public void StartPassMode()
        {
            if (_disposed) return;
            try
            {
                lock (_engine)
                {
                    _isDialogMode = false;
                    _engine.RecognizeAsyncStop();
                    _engine.UnloadAllGrammars();
                    _engine.LoadGrammar(_passGrammar);
                    _engine.RecognizeAsync(RecognizeMode.Multiple);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public void StartDialogMode()
        {
            if (_disposed) return;
            try
            {
                lock (_engine)
                {
                    _isDialogMode = true;
                    _engine.RecognizeAsyncStop();
                    _engine.UnloadAllGrammars();
                    _engine.LoadGrammar(_dialogGrammar);
                    _engine.RecognizeAsync(RecognizeMode.Multiple);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public void Stop()
        {
            if (_disposed) return;
            try
            {
                _engine?.RecognizeAsyncStop();
            }
            catch { }
        }

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e) => SpeechRecognized?.Invoke(this, e);
        private void OnSpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e) => SpeechRejected?.Invoke(this, e);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _engine?.Dispose();
        }
    }
}