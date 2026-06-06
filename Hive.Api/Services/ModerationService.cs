using BogaNet.BWF;
using BogaNet.BWF.Filter;

namespace Hive.Api.Services
{
    public class ModerationService
    {
        private readonly bool _isLibraryLoaded = false;

        public ModerationService()
        {
            try
            {
                // Проверяем наличие папки, чтобы не было крэша
                if (Directory.Exists("./Resources/Filters"))
                {
                    BadWordFilter.Instance.LoadFiles(true, BWFConstants.BWF_LTR);
                    _isLibraryLoaded = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MODERATION ERROR]: Library files not found. {ex.Message}");
            }
        }

        public bool IsTextClean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            // Если библиотека загружена — используем её
            if (_isLibraryLoaded)
            {
                return !BadWordFilter.Instance.Contains(text);
            }

            // Запасной вариант: простейший фильтр (можно расширить список)
            var blackList = new[] { "мат1", "мат2" }; // Добавьте сюда слова, если нужно
            return !blackList.Any(badWord => text.ToLower().Contains(badWord));
        }
    }
}