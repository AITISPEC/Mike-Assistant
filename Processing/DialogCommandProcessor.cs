using Microsoft.Speech.Recognition;
using MikeAssistant.Commands;
using MikeAssistant.Core;
using MikeAssistant.IO;
using MikeAssistant.Responses;
using MikeAssistant.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MikeAssistant.Processing
{
    public class DialogCommandProcessor
    {
        private readonly DialogContext _context;
        private readonly GoogleSearchClient _googleClient;
        private readonly LmStudioHealthCheck _lmHealthCheck;
        private readonly LmStudioClient _lmClient;
        private readonly IntentClassifier _intentClassifier;
        private readonly Action<string> _updateInfo;
        private readonly Action<string> _speak;
        private readonly Action _endDialogMode;
        private readonly Action _checkAllStatus;
        private readonly Action<MikeMode> _setMode;

        private bool _waitingForSearchQuery = false;
        public bool IsModeSwitchCommand { get; private set; }
        public bool IsExitCommand { get; private set; }

        public DialogCommandProcessor(
            DialogContext context,
            GoogleSearchClient googleClient,
            LmStudioHealthCheck lmHealthCheck,
            LmStudioClient lmClient,
            Action<string> updateInfo,
            Action<string> speak,
            Action endDialogMode,
            Action checkAllStatus,
            Action<MikeMode> setMode)
        {
            _setMode = setMode;
            _context = context;
            _googleClient = googleClient;
            _lmHealthCheck = lmHealthCheck;
            _lmClient = lmClient;
            _updateInfo = updateInfo;
            _speak = speak;
            _endDialogMode = endDialogMode;
            _checkAllStatus = checkAllStatus;
            _intentClassifier = new IntentClassifier(context);
        }

        public void Reset()
        {
            _waitingForSearchQuery = false;
        }

        // Основной метод обработки команды (вызывается из MainWindow)
        public async Task<string> ProcessCommandAsync(string text, RecognitionResult result)
        {
            IsExitCommand = false;
            IsModeSwitchCommand = false;

            // 1. Обработка точной команды (включая "отбой" и другие команды выхода)
            if (TryProcessExactCommand(text, out string exactResponse, out bool isExit))
            {
                if (isExit)
                {
                    IsExitCommand = true;
                    return null;   // ответа для озвучивания нет
                }
                // Обычная команда: exactResponse уже озвучен через SendResponse
                return exactResponse;
            }

            // 2. Режим ожидания поискового запроса
            if (_waitingForSearchQuery)
            {
                _waitingForSearchQuery = false;
                if (!string.IsNullOrEmpty(text))
                {
                    _checkAllStatus();
                    string searchResponse = _googleClient.IsInternetAvailable()
                        ? _googleClient.OpenBrowserSearch(text)
                        : ResponseManager.Get("no_internet");
                    SendResponse(searchResponse, "search_result");
                    return searchResponse;
                }
                else
                {
                    string errorResponse = "Не понял запрос. Попробуйте ещё раз.";
                    SendResponse(errorResponse, "search_error");
                    return errorResponse;
                }
            }

            // 3. Семантическая обработка из RecognitionResult (поиск, llm)
            if (result != null)
            {
                // Поиск через семантику
                if (result.Semantics.ContainsKey("query"))
                {
                    string query = result.Semantics["query"].Value?.ToString();
                    if (!string.IsNullOrEmpty(query) && query != "что-нибудь")
                    {
                        _checkAllStatus();
                        string searchResponse = _googleClient.IsInternetAvailable()
                            ? _googleClient.OpenBrowserSearch(query)
                            : ResponseManager.Get("no_internet");
                        SendResponse(searchResponse, "search_immediate");
                        return searchResponse;
                    }
                }
                // LLM через семантику
                if (result.Semantics.ContainsKey("llm_query"))
                {
                    string query = result.Semantics["llm_query"].Value?.ToString();
                    _checkAllStatus();
                    string llmResponse;
                    if (!_lmHealthCheck.IsServerReachable || !_lmHealthCheck.IsModelLoaded)
                        llmResponse = ResponseManager.Get("llm_unavailable");
                    else if (!string.IsNullOrEmpty(query) && query != "что-нибудь")
                        llmResponse = $"Запрос к нейросети: {query}";
                    else
                        llmResponse = "Что именно спросить у нейросети?";
                    SendResponse(llmResponse, "llm");
                    return llmResponse;
                }
            }

            // 4. Классификация интента
            (string intent, double weight) = _intentClassifier.Classify(text);
            string response = null;
            if (intent != "unknown")
                response = ResponseManager.GetForIntent(intent, weight);

            // 5. Обработка поиска по ключевым словам
            if (intent == "search" && string.IsNullOrEmpty(response))
            {
                _checkAllStatus();
                if (!_googleClient.IsInternetAvailable())
                    response = ResponseManager.Get("no_internet");
                else
                {
                    _waitingForSearchQuery = true;
                    response = "Что нужно найти?";
                }
                SendResponse(response, intent);
                return response;
            }
            // 6. Обработка запроса к LLM по ключевым словам
            else if (intent == "llm" && string.IsNullOrEmpty(response))
            {
                _checkAllStatus();
                if (!_lmHealthCheck.IsServerReachable || !_lmHealthCheck.IsModelLoaded)
                    response = ResponseManager.Get("llm_unavailable");
                else
                    response = "Что именно спросить у нейросети?";
                SendResponse(response, intent);
                return response;
            }

            // 7. Если есть ответ от классификатора
            if (!string.IsNullOrEmpty(response))
            {
                SendResponse(response, intent);
                return response;
            }

            // 8. Если ничего не подошло — пробуем LLM
            if (_lmHealthCheck.IsServerReachable && _lmHealthCheck.IsModelLoaded)
            {
                try
                {
                    string llmResponse = await _lmClient.SendMessageAsync(text);
                    if (!string.IsNullOrEmpty(llmResponse))
                    {
                        SendResponse(llmResponse, "llm_generated");
                        return llmResponse;
                    }
                    else
                    {
                        response = ResponseManager.Get("not_understand");
                        SendResponse(response, "unknown");
                        return response;
                    }
                }
                catch
                {
                    response = ResponseManager.Get("not_understand");
                    SendResponse(response, "unknown");
                    return response;
                }
            }
            else
            {
                // LLM недоступен и команда не распознана → возвращаем null для эха
                return null;
            }
        }

        // Обработка точной команды (синхронная, без LLM)
        private bool TryProcessExactCommand(string text, out string response, out bool isExit)
        {
            isExit = false;
            response = null;

            // Список точных команд (из CommandLists)
            if (!CommandLists.ExactCommands.Contains(text))
                return false;

            switch (text)
            {
                case "отбой":
                    _endDialogMode();
                    isExit = true;
                    return true;
                case "алло майк":
                    _setMode?.Invoke(Core.MikeMode.Normal);
                    IsModeSwitchCommand = true;
                    return true;
                case "алло квен":
                    _setMode?.Invoke(Core.MikeMode.LlmRelay);
                    IsModeSwitchCommand = true;
                    return true;

                // Приветствия
                case "привет":
                case "здравствуй":
                case "доброе утро":
                case "добрый день":
                case "добрый вечер":
                case "здорово":
                case "приветствую":
                    response = ResponseManager.Get("greeting");
                    break;

                // Благодарности
                case "спасибо":
                case "благодарю":
                case "спасибо большое":
                case "мерси":
                    response = ResponseManager.Get("thanks");
                    break;

                // Извинения
                case "извини":
                case "прости":
                case "извините":
                case "прошу прощения":
                    response = ResponseManager.Get("sorry");
                    break;

                // Согласие
                case "да":
                case "хорошо":
                case "ладно":
                case "конечно":
                case "согласен":
                case "отлично":
                    response = ResponseManager.Get("affirmative");
                    break;

                // Отказ
                case "нет":
                case "не надо":
                    response = ResponseManager.Get("negative");
                    break;

                // Вопросы о состоянии
                case "как дела":
                case "как ты":
                case "как настроение":
                case "как жизнь":
                case "как поживаешь":
                case "что нового":
                case "что слышно":
                    response = ResponseManager.Get("how_are_you");
                    break;

                // Вопросы о возможностях
                case "что ты умеешь":
                case "какие у тебя функции":
                case "какие у тебя есть функции":
                case "какие у тебя возможности":
                case "что ты можешь":
                case "чем ты полезен":
                case "что ты делаешь":
                case "для чего ты нужен":
                    response = ResponseManager.Get("capabilities");
                    break;

                // Вопросы о личности
                case "кто ты":
                case "как тебя зовут":
                case "твоё имя":
                case "как твоё имя":
                case "расскажи о себе":
                case "что ты такое":
                case "откуда ты":
                    response = ResponseManager.Get("identity");
                    break;

                // Поиск
                case "найди в интернете":
                case "поищи в интернете":
                case "загугли":
                case "найди":
                case "поищи":
                case "открой браузер":
                    _checkAllStatus();
                    if (!_googleClient.IsInternetAvailable())
                        response = ResponseManager.Get("no_internet");
                    else
                    {
                        _waitingForSearchQuery = true;
                        response = "Что нужно найти?";
                    }
                    break;

                // Запрос к нейросети (локальной LLM)
                case "спроси у нейросети":
                case "спроси у ии":
                case "обратись к нейросети":
                case "пусть нейросеть ответит":
                case "спроси сам":
                case "нейросеть":
                    _checkAllStatus();
                    if (!_lmHealthCheck.IsServerReachable || !_lmHealthCheck.IsModelLoaded)
                        response = ResponseManager.Get("llm_unavailable");
                    else
                        response = "Что именно спросить у нейросети?";
                    break;

                default:
                    return false;
            }

            if (!string.IsNullOrEmpty(response))
                SendResponse(response, text);
            return true;
        }

        // Отправка ответа (озвучивание, логирование)
        private void SendResponse(string response, string intent)
        {
            _context.AddMikeMessage(response, "local", intent);
            _updateInfo(response);
            _speak(response);
        }
    }
}