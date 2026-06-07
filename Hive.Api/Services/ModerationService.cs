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

            if (_isLibraryLoaded)
            {
                return !BadWordFilter.Instance.Contains(text);
            }
            var blackList = new[] { "дура", "тупой" };
            return !blackList.Any(badWord => text.ToLower().Contains(badWord));
        }
    }
}