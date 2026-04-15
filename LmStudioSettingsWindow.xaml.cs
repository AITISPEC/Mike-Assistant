using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MikeAssistant.Services;

namespace MikeAssistant
{
    public partial class LmStudioSettingsWindow : Window
    {
        private readonly LmStudioHealthCheck _healthCheck;
        private readonly LmStudioClient _client;
        private readonly string _presetId;

        public string SelectedModelKey { get; private set; }
        public string SelectedPresetId { get; private set; }

        public LmStudioSettingsWindow(LmStudioHealthCheck healthCheck, LmStudioClient client, string presetId)
        {
            InitializeComponent();
            _healthCheck = healthCheck;
            _client = client;
            _presetId = presetId;
            LoadData();
        }

        private async void LoadData()
        {
            var status = _healthCheck.CheckHealthDetailed();
            if (status.IsReachable)
            {
                ServerStatusText.Text = $"Статус: ✅ Доступен (модель: {status.LoadedModelName ?? "не загружена"})";
                ServerUrlText.Text = $"URL: {status.Url}";
                ServerContextText.Text = $"Контекст: {status.ContextLength} токенов";

                // Заполняем список моделей
                ModelsComboBox.ItemsSource = status.AvailableModels;
                if (status.AvailableModels.Any())
                {
                    // Выбираем загруженную модель, если есть
                    var loaded = status.AvailableModels.FirstOrDefault(m => m.IsLoaded);
                    if (loaded != null)
                    {
                        ModelsComboBox.SelectedItem = loaded;
                        SelectedModelKey = loaded.Key;
                        ModelStatusText.Text = $"Выбрана модель: {loaded.DisplayName}";
                    }
                    else if (status.AvailableModels.Count == 1)
                    {
                        ModelsComboBox.SelectedIndex = 0;
                        SelectedModelKey = status.AvailableModels[0].Key;
                        ModelStatusText.Text = $"Выбрана модель: {status.AvailableModels[0].DisplayName}";
                    }
                    else
                    {
                        ModelStatusText.Text = "Выберите модель из списка";
                        SelectedModelKey = null;
                    }
                }
                else
                {
                    ModelStatusText.Text = "Модели не найдены. Загрузите модель в LM Studio.";
                    SelectedModelKey = null;
                }
            }
            else
            {
                ServerStatusText.Text = $"Статус: ❌ Недоступен ({status.ErrorMessage})";
                ServerUrlText.Text = $"URL: {status.Url}";
                ModelsComboBox.IsEnabled = false;
                PresetsComboBox.IsEnabled = false;
            }

            // Загружаем пресеты из файловой системы
            var presets = PresetLoader.LoadPresetsFromUserFolder();
            PresetsComboBox.Items.Clear();
            PresetsComboBox.Items.Add(new PresetItem { Id = "", Name = "Стандартный (без пресета)" });
            foreach (var p in presets)
            {
                PresetsComboBox.Items.Add(new PresetItem { Id = p.Id, Name = $"{p.Name} ({p.FileName})" });
            }

            // Выбираем сохранённый пресет, если он есть в списке
            if (!string.IsNullOrEmpty(_presetId))
            {
                bool found = false;
                foreach (PresetItem item in PresetsComboBox.Items)
                {
                    if (item.Id == _presetId)
                    {
                        PresetsComboBox.SelectedItem = item;
                        SelectedPresetId = item.Id;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    PresetsComboBox.SelectedIndex = 0;
                    SelectedPresetId = "";
                }
            }
            else
            {
                PresetsComboBox.SelectedIndex = 0;
                SelectedPresetId = "";
            }
        }

        private void ModelsComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ModelsComboBox.SelectedItem is ModelInfo model)
            {
                ModelStatusText.Text = $"Выбрана модель: {model.DisplayName}";
                SelectedModelKey = model.Key;
            }
            else
            {
                ModelStatusText.Text = "Модель не выбрана";
                SelectedModelKey = null;
            }
        }

        private void PresetsComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PresetsComboBox.SelectedItem is PresetItem preset)
                SelectedPresetId = preset.Id;
            else
                SelectedPresetId = "";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Если модель не выбрана, но есть доступные - выбираем первую
            if (string.IsNullOrEmpty(SelectedModelKey) && ModelsComboBox.HasItems && ModelsComboBox.SelectedItem == null)
            {
                if (ModelsComboBox.Items.Count > 0)
                {
                    ModelsComboBox.SelectedIndex = 0;
                }
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class ModelInfo
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool IsLoaded { get; set; }
    }

    public class PresetItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}