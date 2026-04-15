using System;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MikeAssistant.IO
{
    public class GoogleSearchClient
    {
        public bool IsInternetAvailable()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var addresses = new[] { "8.8.8.8", "1.1.1.1" };
                    foreach (var address in addresses)
                    {
                        try
                        {
                            var reply = ping.Send(address, 2000);
                            if (reply?.Status == IPStatus.Success)
                                return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        public string OpenBrowserSearch(string query)
        {
            if (string.IsNullOrEmpty(query))
                query = "привет мир";

            try
            {
                Process.Start($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
                return Responses.ResponseManager.Get("search");
            }
            catch
            {
                return "Не удалось открыть браузер.";
            }
        }
    }
}