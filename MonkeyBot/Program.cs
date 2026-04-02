// Monkey - Бот для записи трат

using System.Text.Encodings.Web;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

// Настройки сериализации JSON
var options = new JsonSerializerOptions 
{ 
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

// Инициализация бота
using var cts = new CancellationTokenSource();
var bot = new TelegramBotClient("XXX", cancellationToken: cts.Token); // ТОКЕН БОТА
var me = await bot.GetMe();
bot.OnMessage += OnMessage;

Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");
Console.ReadLine();
cts.Cancel();

async Task OnMessage(Message msg, UpdateType type)
{
    // Обрабатываем только текстовые сообщения
    if (msg.Text is null) return;   
    
    Console.WriteLine($"Received {type} '{msg.Text}' in {msg.Chat}");
    string userText = msg.Text.ToLower();
    string[] addRecord = userText.Split(' ');
    
    // Фиксируем текущее время по МСК (UTC+3)
    var mskNow = DateTime.UtcNow.AddHours(3);
    
    switch (userText)
    {
        // Парсинг новой траты (формат: <сумма> <комментарий>)
        case string str when decimal.TryParse(addRecord[0], out decimal sum) && addRecord.Length >= 2:
            string name = string.Join(" ", addRecord.Skip(1));
            var newExpense = new Expense 
            { 
                userId = msg.From.Id,
                amount = sum,
                reason = name,
                createdAt = mskNow // Сохраняем с учетом МСК
            };
            
            await ExpenseRepository.SaveAsync(newExpense);
            await bot.SendMessage(msg.Chat, $"Сохранено: {name} — {sum} руб.");
            break;
        
        case "/start":
            string startMessage = 
                "Привет! Я Monkey 🐒 — твоя помощница для учета расходов.\n\n" +
                "Чтобы добавить трату, просто напиши сумму и комментарий. Например: 500 кофе или 1200 такси.\n\n" +
                "Доступные команды:\n" +
                "/today — траты за сегодня\n" +
                "/week — за неделю\n" +
                "/month — за месяц\n" +
                "/last — последние 10 трат";
                
            await bot.SendMessage(msg.Chat, startMessage);
            break;
        
        case "/today":
            var allExpenses = await ExpenseRepository.GetAllAsync();
            var todayMsk = mskNow.Date;
            
            // Фильтр за сегодня по московскому времени
            var todayExpenses = allExpenses
                .Where(e => e.createdAt.Date == todayMsk && e.userId == msg.From.Id)
                .ToList();
    
            if (todayExpenses.Count == 0)
            {
                await bot.SendMessage(msg.Chat, "Сегодня еще не было трат.");
            }
            else
            {
                await bot.SendMessage(msg.Chat, $"За сегодня: {todayExpenses.Sum(e => e.amount)} руб. ({todayExpenses.Count} зап.)");
            }
            break;

        case "/week":
            allExpenses = await ExpenseRepository.GetAllAsync();
            var weekAgo = mskNow.Date.AddDays(-7);
            
            // Фильтр за последние 7 дней
            var weeklyExpenses = allExpenses
                .Where(e => e.createdAt.Date >= weekAgo && e.userId == msg.From.Id)
                .ToList();
    
            if (weeklyExpenses.Count == 0)
            {
                await bot.SendMessage(msg.Chat, "За последние 7 дней трат не найдено.");
            }
            else
            {
                await bot.SendMessage(msg.Chat, $"За неделю: {weeklyExpenses.Sum(e => e.amount)} руб. ({weeklyExpenses.Count} зап.)");
            }
            break;

        case "/month":
            allExpenses = await ExpenseRepository.GetAllAsync();
            var monthAgo = mskNow.Date.AddDays(-30);
            
            // Фильтр за последние 30 дней
            var monthlyExpenses = allExpenses
                .Where(e => e.createdAt.Date >= monthAgo && e.userId == msg.From.Id)
                .ToList();
    
            if (monthlyExpenses.Count == 0)
            {
                await bot.SendMessage(msg.Chat, "За последние 30 дней трат не найдено.");
            }
            else
            {
                await bot.SendMessage(msg.Chat, $"За месяц: {monthlyExpenses.Sum(e => e.amount)} руб. ({monthlyExpenses.Count} зап.)");
            }
            break;
            
        case "/last":
            allExpenses = await ExpenseRepository.GetAllAsync();
            
            // Получаем 10 последних записей
            var lastExpenses = allExpenses
                .Where(e => e.userId == msg.From.Id)
                .OrderByDescending(e => e.createdAt) 
                .Take(10) 
                .ToList();
    
            if (lastExpenses.Count == 0)
            {
                await bot.SendMessage(msg.Chat, "У вас пока нет сохраненных трат.");
            }
            else
            {
                // Форматируем вывод
                var lines = lastExpenses.Select((e, index) => 
                    $"{index + 1}. {e.amount} руб. | {e.reason} ({e.createdAt:dd.MM HH:mm})");
                
                string responseText = string.Join("\n", lines);
                await bot.SendMessage(msg.Chat, responseText);
            }
            break;
        
        default:
            await bot.SendMessage(msg.Chat, "Используйте формат: <сумма> <комментарий> (пример: 500 яблоки)" +
                                            " или управляющие команды (/start)");
            break;
    }
}

// Модель данных
public class Expense
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public long userId { get; set; }
    public decimal amount { get; set; }
    public string reason { get; set; }
    public DateTime createdAt { get; set; }
}

// Репозиторий для работы с файлом
public static class ExpenseRepository
{
    private const string FilePath = "expenses.json";
    
    private static readonly JsonSerializerOptions Options = new() 
    { 
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true 
    };

    public static async Task<List<Expense>> GetAllAsync()
    {
        if (!File.Exists(FilePath)) return new List<Expense>();
        
        string json = await File.ReadAllTextAsync(FilePath);
        if (string.IsNullOrWhiteSpace(json)) return new List<Expense>();

        return JsonSerializer.Deserialize<List<Expense>>(json, Options) ?? new List<Expense>();
    }

    public static async Task SaveAsync(Expense newExpense)
    {
        var all = await GetAllAsync();
        all.Add(newExpense);
        string json = JsonSerializer.Serialize(all, Options);
        await File.WriteAllTextAsync(FilePath, json);
    }
}