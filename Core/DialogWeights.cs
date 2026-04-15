using System;
using System.Collections.Generic;

namespace MikeAssistant.Core
{
    public class DialogWeights
    {
        public Dictionary<string, double> IntentWeights { get; set; } = new Dictionary<string, double>
        {
            { "greeting", 0.8 }, { "farewell", 0.9 }, { "how_are_you", 0.7 }, { "capabilities", 0.6 },
            { "identity", 0.5 }, { "search", 0.9 }, { "llm", 0.4 }, { "thanks", 0.3 }, { "sorry", 0.3 },
            { "affirmative", 0.7 }, { "negative", 0.7 }, { "nutrition", 0.6 }, { "schedule", 0.6 },
            { "weather", 0.5 }, { "news", 0.4 }, { "reminder", 0.5 }, { "unknown", 0.2 }
        };

        public double ContextDecayLambda { get; set; } = 0.001;

        public double GetIntentWeight(string intent) =>
            IntentWeights.ContainsKey(intent) ? IntentWeights[intent] : 0.5;

        public double CalculateContextWeight(DateTime lastMention, DateTime currentTime) =>
            Math.Exp(-ContextDecayLambda * (currentTime - lastMention).TotalSeconds);
    }
}