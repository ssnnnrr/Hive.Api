using Hive.Api.DTOs;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hive.Api.Services
{
    public class AIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _authKey;
        private string? _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        private readonly List<string> _dangerPatterns = new()
        {
            @"(?i)анорекс", @"(?i)булим", @"(?i)похуде.*(быстро|резко|голод)",
            @"(?i)смерт", @"(?i)умер", @"(?i)суицид", @"(?i)убит",
            @"(?i)нарко", @"(?i)таблетк.*(без рецепта|кайф)", @"(?i)психотроп",
            @"(?i)рвот", @"(?i)слабительн", @"(?i)алкогол", @"(?i)спиться"
        };

        public AIService(IConfiguration config)
        {
            var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (m, c, ch, e) => true };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(100);
            _authKey = config["AI:GigaChatKey"] ?? "";
        }

        private async Task<string?> GetTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiry) return _accessToken;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://ngw.devices.sberbank.ru:9443/api/v2/oauth");
                request.Headers.Add("Authorization", $"Basic {_authKey}");
                request.Headers.Add("RqUID", Guid.NewGuid().ToString());
                request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("scope", "GIGACHAT_API_PERS") });
                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<dynamic>(content);
                _accessToken = result?.access_token;
                _tokenExpiry = DateTime.UtcNow.AddMinutes(25);
                return _accessToken;
            }
            catch { return null; }
        }

        public async Task<List<TaskDraftResponse>> GenerateTasksAsync(SmartGoalRequest req)
        {
            string combinedInput = (req.Title + " " + req.Why + " " + req.MeasurableResult).ToLower();
            if (_dangerPatterns.Any(pattern => Regex.IsMatch(combinedInput, pattern)))
            {
                return DangerDraft();
            }

            var token = await GetTokenAsync();
            if (string.IsNullOrEmpty(token)) return DefaultDraft(req.TargetDate);

            // ИСПРАВЛЕННЫЙ ПРОМПТ: Упор на равномерность
            var prompt = $"Ты профессиональный коуч. Составь план достижения цели: '{req.Title}'. " +
                         $"Результатом должно быть: {req.MeasurableResult}. Дедлайн: {req.TargetDate:yyyy-MM-dd}. " +
                         $"Напиши 15-25 шагов, в зависимости от дедлайна. РАСПРЕДЕЛИ ИХ РАВНОМЕРНО от сегодняшнего дня до {req.TargetDate:yyyy-MM-dd}. " +
                         $"Не пиши ничего лишнего ни приветсвий ни объясний не нужно!, сразу ответ , строго по формату. ФОРМАТ: Название шага | ГГГГ-ММ-ДД;";

            try
            {
                var requestBody = new { model = "GigaChat", messages = new[] { new { role = "user", content = prompt } } };
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.PostAsync("https://gigachat.devices.sberbank.ru/api/v1/chat/completions",
                    new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<dynamic>(content);
                string rawText = result?.choices[0]?.message?.content ?? "";

                var steps = new List<TaskDraftResponse>();
                var lines = rawText.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                // ВЫЧИСЛЯЕМ ШАГ ДАТЫ САМИ (если ИИ ошибется)
                DateTime start = DateTime.UtcNow;
                TimeSpan totalDuration = req.TargetDate - start;
                double intervalDays = totalDuration.TotalDays / 20;

                for (int i = 0; i < lines.Length && i < 20; i++)
                {
                    var line = Regex.Replace(lines[i].Trim(), @"^[\d\.\)\-\s\*•]+", "");
                    if (string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split('|');
                    string title = parts[0].Trim();

                    // Если ИИ не вернул дату или она некорректна, рассчитываем её математически
                    DateTime taskDate = start.AddDays(intervalDays * (i + 1));

                    if (parts.Length > 1 && DateTime.TryParse(parts[1].Trim(), out DateTime aiDate))
                    {
                        // Если дата от ИИ в пределах диапазона, берем её
                        if (aiDate > start && aiDate <= req.TargetDate) taskDate = aiDate;
                    }

                    steps.Add(new TaskDraftResponse(title, DateTime.SpecifyKind(taskDate, DateTimeKind.Utc)));
                }
                return steps;
            }
            catch { return DefaultDraft(req.TargetDate); }
        }

        public async Task<string?> GenerateTestJsonAsync(string topic, string format, int questionsCount)
        {
            var token = await GetTokenAsync();
            if (string.IsNullOrEmpty(token)) return null;

            // Определяем специфичные инструкции для каждого формата
            string formatInstructions = format switch
            {
                "multiple" => "Каждый вопрос должен иметь несколько правильных ответов. Поле 'correctAnswer' ДОЛЖНО быть массивом строк (например, [\"ответ1\", \"ответ2\"]).",
                "boolean" => "Каждый вопрос должен быть в формате 'Верно/Неверно'. Поле 'options' всегда должно быть [\"Верно\", \"Неверно\"]. 'correctAnswer' - одна строка.",
                _ => "Каждый вопрос должен иметь только один правильный ответ. Поле 'correctAnswer' должно быть строкой."
            };

            // Формируем промпт
            var prompt = $@"Ты профессиональный преподаватель. Составь учебный тест на тему: '{topic}'.
Количество вопросов: {questionsCount}.
Формат вопросов: {format}.

ИНСТРУКЦИИ:
1. {formatInstructions}
2. Ответ должен быть СТРОГО в формате JSON массива объектов.
3. Не пиши никакого текста, приветствий или пояснений до и после JSON.
4. Все тексты внутри JSON должны быть на русском языке.

СТРУКТУРА ОБЪЕКТА:
{{
  ""question"": ""текст вопроса"",
  ""options"": [""вариант 1"", ""вариант 2"", ""вариант 3""],
  ""correctAnswer"": ""строка_или_массив"",
  ""type"": ""{format}""
}}";

            try
            {
                var requestBody = new
                {
                    model = "GigaChat",
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.7 // Немного креативности для разнообразия вопросов
                };

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.PostAsync("https://gigachat.devices.sberbank.ru/api/v1/chat/completions",
                    new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<dynamic>(content);
                string rawJson = result?.choices[0]?.message?.content ?? "";

                // Очистка от markdown-разметки (```json ... ```), которую часто добавляют нейросети
                rawJson = Regex.Replace(rawJson, "```json|```", "").Trim();

                return rawJson;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI ERROR]: {ex.Message}");
                return null;
            }
        }


        public async Task<string?> GenerateGoalInvitationSummaryAsync(string inviterName, string title, string description, string result)
        {
            var token = await GetTokenAsync();
            if (string.IsNullOrEmpty(token)) return null;

            var prompt = $@"Ты — дружелюбный ассистент. Составь очень краткое (20-30 слов) и вдохновляющее приглашение для пользователя.
Имя приглашающего: {inviterName}
Цель: {title}
Описание: {description}
Ожидаемый результат: {result}

Инструкция: Напиши только текст приглашения и ничего лишнего. Сделай упор на то, какой крутой результат ждет участников. Не используй кавычки.";

            try
            {
                var requestBody = new { model = "GigaChat", messages = new[] { new { role = "user", content = prompt } } };
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.PostAsync("https://gigachat.devices.sberbank.ru/api/v1/chat/completions",
                    new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));

                var content = await response.Content.ReadAsStringAsync();
                var resObj = JsonConvert.DeserializeObject<dynamic>(content);
                string aiSummary = resObj?.choices[0]?.message?.content ?? "";

                return aiSummary.Trim();
            }
            catch { return null; }
        }


        private async Task<string?> GetCompletionAsync(string prompt)
        {
            var token = await GetTokenAsync();
            if (string.IsNullOrEmpty(token)) return null;

            try
            {
                var requestBody = new { model = "GigaChat", messages = new[] { new { role = "user", content = prompt } } };
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.PostAsync("https://gigachat.devices.sberbank.ru/api/v1/chat/completions",
                    new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<dynamic>(content);
                string rawText = result?.choices[0]?.message?.content ?? "";

                // --- УЛУЧШЕННЫЙ ПАРСИНГ ---
                // Ищем начало [ и конец ] массива, чтобы отсечь лишний текст от ИИ
                int start = rawText.IndexOf('[');
                int end = rawText.LastIndexOf(']');

                if (start != -1 && end != -1 && end > start)
                {
                    return rawText.Substring(start, (end - start) + 1);
                }

                return null;
            }
            catch { return null; }
        }

        // Hive.Api/Services/AIService.cs

        public async Task<string?> GenerateComplexVerificationTest(string skillName)
        {
            // 1. Улучшенный промпт с жестким контролем языка
            var prompt = $@"Ты — профессиональный эксперт-аттестатор. 
Создай уникальный, сложный тест из 10 вопросов по теме: '{skillName}'.

ПРАВИЛА ЯЗЫКА:
- Если '{skillName}' — это иностранный язык (например, English, German), пиши вопросы и ответы на этом языке.
- ДЛЯ ВСЕХ ОСТАЛЬНЫХ ТЕМ (программирование, дизайн, маркетинг и т.д.) ПИШИ СТРОГО НА РУССКОМ ЯЗЫКЕ.

ТРЕБОВАНИЯ К ТЕСТУ:
- Уровень сложности: Senior/Lead/Expert.
- Темы вопросов должны быть разнообразными и не повторяться.
-ЗАПРЕТЫ: 
   - КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО использовать комментарии вида // или /* внутри JSON.
   - ЗАПРЕЩЕНО использовать двойные кавычки внутри текста вопроса. Используй только одинарные (').
   - ЗАПРЕЩЕНО ставить точку с запятой (;) в конце строк внутри JSON.
- Формат: СТРОГО JSON массив объектов БЕЗ вводных слов.
- Структура: [{{""question"":""текст"",""options"":[""A"",""B"",""C"",""D""],""correctAnswer"":""точный текст""}}]
- В поле correctAnswer НЕ должно быть лишних кавычек или точек.
- Если внутри текста нужны кавычки, используй одинарные (').";

            try
            {
                var token = await GetTokenAsync();
                if (token == null) return null;

                var requestBody = new
                {
                    model = "GigaChat",
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.85, // Оставляем высокой для разнообразия
                    max_tokens = 3500
                };

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.PostAsync("https://gigachat.devices.sberbank.ru/api/v1/chat/completions",
                    new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<dynamic>(content);
                string rawText = result?.choices[0]?.message?.content ?? "";

                // Логируем для отладки (вы увидите это в Output Visual Studio)
                Console.WriteLine($"[AI RESPONSE]: {rawText}");

                return RobustJsonRepair(rawText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI FATAL]: {ex.Message}");
                return null;
            }
        }

        private string? RobustJsonRepair(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            try
            {
                // 1. Убираем Markdown
                string cleaned = Regex.Replace(input, @"```json|```", "").Trim();

                // 2. УДАЛЯЕМ КОММЕНТАРИИ (те самые // которые сломали парсинг)
                cleaned = Regex.Replace(cleaned, @"//.*$", "", RegexOptions.Multiline);
                cleaned = Regex.Replace(cleaned, @"/\*.*?\*/", "", RegexOptions.Singleline);

                // 3. Находим границы массива
                int startIndex = cleaned.IndexOf('[');
                if (startIndex == -1) return null;
                cleaned = cleaned.Substring(startIndex);

                // 4. Чиним обрывы
                if (!cleaned.EndsWith("]"))
                {
                    int lastGoodObjectEnd = cleaned.LastIndexOf('}');
                    if (lastGoodObjectEnd != -1) cleaned = cleaned.Substring(0, lastGoodObjectEnd + 1) + "]";
                    else return null;
                }

                // 5. Удаляем мусорные символы, которые ИИ лепит в строки
                cleaned = cleaned.Replace("\n", " ").Replace("\r", " ");

                // Убираем "; в конце строк (ошибка из лога)
                cleaned = cleaned.Replace("\";", "\"");

                // Фикс запятых
                cleaned = Regex.Replace(cleaned, @"}\s*{", "},{");
                cleaned = Regex.Replace(cleaned, @",\s*]", "]");
                cleaned = Regex.Replace(cleaned, @",\s*}", "}");

                // Убираем лишние одинарные кавычки в значениях
                cleaned = Regex.Replace(cleaned, @":\s*""'([^""]+)'""", @":""$1""");

                using (JsonDocument.Parse(cleaned)) return cleaned;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLEANUP FAILED]: {ex.Message}");
                return null;
            }
        }


        private List<TaskDraftResponse> DangerDraft() => new() { new TaskDraftResponse("Обратиться за психологической поддержкой", DateTime.UtcNow.AddDays(1)) };
        private List<TaskDraftResponse> DefaultDraft(DateTime target) => new() { new TaskDraftResponse("Начать действовать", target.AddDays(-1)) };
    }
}