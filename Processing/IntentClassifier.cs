using MikeAssistant.Core;
using System.Collections.Generic;
using System.Linq;

namespace MikeAssistant.Processing
{
    public class IntentClassifier
    {
        private readonly DialogContext _context;
        private readonly Dictionary<string, List<string>> _intentKeywords = new Dictionary<string, List<string>>
        {
            { "greeting", new List<string> { "привет", "здравствуй", "доброе утро", "добрый день", "здорово" } },
            { "farewell", new List<string> { "пока", "до свидания", "всего хорошего", "удачи", "прощай" } },
            { "how_are_you", new List<string> { "как дела", "как ты", "как настроение", "как жизнь", "как поживаешь", "что нового" } },
            { "capabilities", new List<string> { "что ты умеешь", "какие функции", "что можешь", "чем полезен" } },
            { "identity", new List<string> { "кто ты", "как зовут", "твоё имя", "расскажи о себе" } },
            { "search", new List<string> { "найди", "поищи", "загугли", "открой браузер" } },
            { "llm", new List<string> { "спроси у нейросети", "нейросеть", "ии" } },
            { "thanks", new List<string> { "спасибо", "благодарю", "мерси" } },
            { "sorry", new List<string> { "извини", "прости", "прошу прощения" } },
            { "affirmative", new List<string> { "да", "хорошо", "ладно", "конечно", "согласен", "отлично" } },
            { "negative", new List<string> { "нет", "не надо", "не хочу", "отмена" } },
            { "nutrition", new List<string> { "поесть", "питание", "еда", "завтрак", "обед", "ужин", "рецепт", "диета" } },
            { "schedule", new List<string> { "расписание", "план", "дела", "завтра", "сегодня", "вечером", "утром" } },
            { "weather", new List<string> { "погода", "дождь", "солнце", "температура", "ветер" } },
            { "news", new List<string> { "новости", "события", "что случилось" } },
            { "reminder", new List<string> { "напомни", "запомни", "не забыть", "важно" } }
        };

        public IntentClassifier(DialogContext context) => _context = context;

        public (string intent, double weight) Classify(string text)
        {
            string lowerText = text.ToLower();
            var scores = new Dictionary<string, double>();

            foreach (var intent in _intentKeywords.Keys)
            {
                double score = _intentKeywords[intent].Count(k => lowerText.Contains(k));
                if (score > 0)
                {
                    score = score / _intentKeywords[intent].Count;
                    score *= _context.GetContextWeightForIntent(intent);
                    scores[intent] = score;
                }
            }

            if (scores.Count == 0)
                return ("unknown", 0.2);

            var best = scores.OrderByDescending(x => x.Value).First();
            return (best.Key, best.Value);
        }
    }
}