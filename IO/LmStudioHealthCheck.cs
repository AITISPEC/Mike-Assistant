using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json;

namespace MikeAssistant.Services
{
    public class LmStudioHealthCheck : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelsEndpoint;

        public bool IsServerReachable { get; private set; }
        public bool IsModelLoaded { get; private set; }
        public string CurrentModelName { get; private set; }

        public LmStudioHealthCheck(string lmStudioUrl)
        {
            _modelsEndpoint = $"{lmStudioUrl.TrimEnd('/')}/v1/models";
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        public void CheckHealth()
        {
            try
            {
                Debug.WriteLine($"[LMStudio] Checking {_modelsEndpoint}");
                var response = _httpClient.GetAsync(_modelsEndpoint).GetAwaiter().GetResult();
                Debug.WriteLine($"[LMStudio] Response status: {response.StatusCode}");
                IsServerReachable = response.IsSuccessStatusCode;

                if (IsServerReachable)
                {
                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    IsModelLoaded = json.Contains("\"id\"");

                    var start = json.IndexOf("\"id\":\"") + 6;
                    if (start > 6)
                    {
                        var end = json.IndexOf("\"", start);
                        if (end > start)
                            CurrentModelName = json.Substring(start, end - start);
                    }
                }
                else
                {
                    IsModelLoaded = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LMStudio] Exception: {ex.Message}");
                IsServerReachable = false;
                IsModelLoaded = false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public (bool IsReachable, string Url, string LoadedModelName, int ContextLength, List<ModelInfo> AvailableModels, string ErrorMessage) CheckHealthDetailed()
        {
            var availableModels = new List<ModelInfo>();
            string loadedModel = null;
            int contextLength = 8192; // значение по умолчанию
            bool reachable = false;
            string error = null;

            try
            {
                var response = _httpClient.GetAsync(_modelsEndpoint).GetAwaiter().GetResult();
                reachable = response.IsSuccessStatusCode;
                if (reachable)
                {
                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);

                    // Ожидаем массив "data" (OpenAI-совместимый формат)
                    if (data.data != null)
                    {
                        foreach (var model in data.data)
                        {
                            string id = model.id;
                            if (id != null)
                            {
                                availableModels.Add(new ModelInfo
                                {
                                    Key = id,
                                    DisplayName = id,
                                    IsLoaded = true // в LM Studio через этот эндпоинт не определить, ставим true
                                });
                                if (string.IsNullOrEmpty(loadedModel))
                                    loadedModel = id;
                            }
                        }
                    }
                    // Если нет поля data, пробуем старый формат (для обратной совместимости)
                    else if (data.models != null)
                    {
                        foreach (var model in data.models)
                        {
                            string key = model.key;
                            string displayName = model.display_name ?? key;
                            bool isLoaded = model.loaded_instances != null && model.loaded_instances.Count > 0;
                            availableModels.Add(new ModelInfo { Key = key, DisplayName = displayName, IsLoaded = isLoaded });
                            if (isLoaded)
                            {
                                loadedModel = displayName;
                                if (model.loaded_instances?[0]?.config?.context_length != null)
                                    contextLength = (int)model.loaded_instances[0].config.context_length;
                            }
                        }
                    }
                }
                else
                {
                    error = $"HTTP {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                reachable = false;
                error = ex.Message;
            }

            return (reachable, _modelsEndpoint, loadedModel, contextLength, availableModels, error);
        }
    }
    public class ModelInfo
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool IsLoaded { get; set; }
    }
}