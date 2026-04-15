using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MikeAssistant.Services
{
    public class LmStudioClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _chatEndpoint;
        private string _currentResponseId = null;
        private string _currentModelKey;
        private string _currentPresetId;

        public LmStudioClient(string lmStudioUrl)
        {
            _chatEndpoint = $"{lmStudioUrl.TrimEnd('/')}/v1/chat/completions";
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public void SetCurrentModel(string modelKey) => _currentModelKey = modelKey;
        public void SetPreset(string presetId) => _currentPresetId = presetId;
        public void ResetChat() => _currentResponseId = null;

        // Простой запрос (без истории, без пресета)
        public async Task<string> SendMessageAsync(string userMessage, string systemPrompt = null)
        {
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
                messages.Add(new { role = "system", content = systemPrompt });
            messages.Add(new { role = "user", content = userMessage });

            var requestBody = new
            {
                model = _currentModelKey ?? "local-model",
                messages = messages,
                temperature = 0.7,
                max_tokens = 500,
                stream = false
            };
            return await SendRequestAsync(requestBody);
        }

        // Запрос с контекстом через previous_response_id (без ручной истории)
        public async Task<string> SendMessageWithContextAsync(string userMessage, bool resetContext = false)
        {
            if (resetContext)
                _currentResponseId = null;

            var messages = new List<object>();
            messages.Add(new { role = "user", content = userMessage });

            var requestBody = new
            {
                model = _currentModelKey ?? "local-model",
                messages = messages,
                temperature = 0.7,
                max_tokens = 500,
                store = true,
                previous_response_id = _currentResponseId,
                preset = string.IsNullOrEmpty(_currentPresetId) ? null : _currentPresetId
            };

            string responseJson = await SendRequestRawAsync(requestBody);
            if (responseJson != null)
            {
                dynamic data = JsonConvert.DeserializeObject(responseJson);
                if (data.response_id != null)
                    _currentResponseId = data.response_id;
                if (data.choices != null && data.choices.Count > 0)
                    return data.choices[0].message.content;
            }
            return null;
        }

        private async Task<string> SendRequestAsync(object requestBody)
        {
            string responseJson = await SendRequestRawAsync(requestBody);
            if (responseJson != null)
            {
                dynamic data = JsonConvert.DeserializeObject(responseJson);
                return data.choices?[0]?.message?.content;
            }
            return null;
        }

        private async Task<string> SendRequestRawAsync(object requestBody)
        {
            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            try
            {
                var response = await _httpClient.PostAsync(_chatEndpoint, content);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[LMStudio] HTTP error: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LMStudio] Exception: {ex.Message}");
            }
            return null;
        }

        public void Dispose() => _httpClient?.Dispose();
    }
}