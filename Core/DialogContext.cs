using System;
using System.Collections.Generic;
using System.Linq;

namespace MikeAssistant.Core
{
    public class DialogContext
    {
        private readonly int _maxPairs;
        private readonly List<DialogEntry> _entries;
        private readonly DialogWeights _weights;

        public DialogWeights Weights => _weights;

        public DialogContext(int maxPairs = 10)
        {
            _maxPairs = maxPairs;
            _entries = new List<DialogEntry>();
            _weights = new DialogWeights();
        }

        public void AddUserMessage(string text)
        {
            _entries.Add(new DialogEntry { Role = "user", Text = text, Timestamp = DateTime.Now });
            Trim();
        }

        public void AddMikeMessage(string text, string source, string intent = null)
        {
            _entries.Add(new DialogEntry { Role = "mike", Text = text, Source = source, Intent = intent, Timestamp = DateTime.Now });
            Trim();
        }

        public List<DialogEntry> GetLastPairs(int count)
        {
            int start = Math.Max(0, _entries.Count - count);
            return _entries.GetRange(start, _entries.Count - start);
        }

        public string GetLastUserMessage()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Role == "user")
                    return _entries[i].Text;
            return null;
        }

        public List<string> GetRecentIntents(int count)
        {
            var result = new List<string>();
            for (int i = _entries.Count - 1; i >= 0 && result.Count < count; i--)
                if (_entries[i].Role == "mike" && !string.IsNullOrEmpty(_entries[i].Intent))
                    result.Insert(0, _entries[i].Intent);
            return result;
        }

        public bool ContainsTopic(string topic) => _entries.Any(e => e.Text.ToLower().Contains(topic));

        public DateTime GetLastMentionTime(string topic)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Text.ToLower().Contains(topic))
                    return _entries[i].Timestamp;
            return DateTime.MinValue;
        }

        public double GetContextWeightForIntent(string intent)
        {
            double weight = _weights.GetIntentWeight(intent);
            var recentIntents = GetRecentIntents(3);
            if (recentIntents.Contains(intent))
                weight *= 1.2;

            var lastUserMsg = GetLastUserMessage();
            if (!string.IsNullOrEmpty(lastUserMsg) && lastUserMsg.Contains("не"))
                weight *= 0.5;

            var lastMention = GetLastMentionTime(intent);
            if (lastMention > DateTime.MinValue)
                weight *= _weights.CalculateContextWeight(lastMention, DateTime.Now);

            return Math.Min(1.0, Math.Max(0.1, weight));
        }

        private void Trim()
        {
            while (_entries.Count > _maxPairs * 2)
                _entries.RemoveAt(0);
        }

        public void Clear() => _entries.Clear();
    }

    public class DialogEntry
    {
        public string Role { get; set; }
        public string Text { get; set; }
        public string Source { get; set; }
        public string Intent { get; set; }
        public DateTime Timestamp { get; set; }
    }
}