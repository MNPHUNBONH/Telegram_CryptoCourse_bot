using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot
{
	public class Program
	{
		static void Main(string[] args)
		{
			var currentBot = new CurrencyBot(ApiConstants.BOT_API);
			currentBot.CreateCommand();
			currentBot.StartReceiving();


			Console.ReadKey();
		}

		public static class CoinMarket
		{
			private static readonly string API_KEY = ApiConstants.COIN_MARKET_API;

			public static async Task<decimal> GetPriceAsync(string currencyCode)
			{
				using (var httpClient = new HttpClient())
				{
					httpClient.DefaultRequestHeaders.Add("X-CMC_PRO_API_KEY", API_KEY);
					
					var respons = await httpClient.GetAsync($"https://pro-api.coinmarketcap.com/v1/cryptocurrency/" +
					                                        $"quotes/latest?symbol={currencyCode}&convert=USD");
					var responsString = await respons.Content.ReadAsStreamAsync();
					var jsonResponse = JsonNode.Parse(responsString);
					var price = (decimal)jsonResponse["data"][currencyCode]["quote"]["USD"]["price"];
					return price;
				}
			} 
		}

		public class CurrencyBot
		{
			private readonly TelegramBotClient _telegramBotClient;

			private readonly List<string> _currencyCodes = new()
			{
				CurrencyCode.BTC, CurrencyCode.ETH, CurrencyCode.BNB, CurrencyCode.DOGE
			};

			public CurrencyBot(string token)
			{
				_telegramBotClient = new TelegramBotClient(token);
			}
			public void CreateCommand()
			{
				_telegramBotClient.SetMyCommandsAsync(new List<BotCommand>()
				{
					new()
					{
						Command = CustomBotCommand.START,
						Description = "Запуск бота"
					},
					new()
					{
						Command = CustomBotCommand.SHOW_CURRENCIES,
						Description = "Вывод сообщения с выбором 1 из 4 валют, для получения ее цены в данный момент"
					}
				});
			}
			public void StartReceiving()
			{
				var cancellationTokenSource = new CancellationTokenSource();
				var cancellationToken = cancellationTokenSource.Token;

				var receiverOption = new ReceiverOptions
				{
					AllowedUpdates = new UpdateType[]
					{
						UpdateType.Message, UpdateType.CallbackQuery
					}
				};
				
				_telegramBotClient.StartReceiving(
					HandleUpdateAsync,
					HandleError,
					receiverOption,
					cancellationToken);
			}
			
			private Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
			{
				Console.WriteLine(exception);
				return Task.CompletedTask;
			}

			private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
				CancellationToken cancellationToken)
			{
				switch (update.Type)
				{
					case UpdateType.Message:
						await HandleMessegeAsync(update, cancellationToken);
						break;
					case UpdateType.CallbackQuery:
						await HandleCallbackQueryAsync(update, cancellationToken);
						break;
				}
			}

			private async Task HandleCallbackQueryAsync(Update update, CancellationToken cancellationToken)
			{
				if (update.CallbackQuery?.Message == null) return;

				var chatId = update.CallbackQuery.Message.Chat.Id;
				var callbackData = update.CallbackQuery.Data;
				var messageId = update.CallbackQuery.Message.MessageId;

				if (callbackData == CustomCallbackdata.SHOW_CURRENCIES_MENU)
				{
					await DeleteMessage(chatId, messageId, cancellationToken);
					await ShowCurrncySelectionAsync(chatId, cancellationToken);
					return;
				}
				
				if (_currencyCodes.Contains(callbackData))
				{
					await DeleteMessage(chatId, messageId, cancellationToken);
					await SendCurrencyPriceAsync(chatId, callbackData, cancellationToken);
					return;
				}

				if (callbackData == CustomCallbackdata.RETURN_TO_CURRENCIES_MENU)
				{
					await ShowCurrncySelectionAsync(chatId, cancellationToken);
				}
			}
			

			private async Task HandleMessegeAsync(Update update, CancellationToken cancellationToken)
			{
				if(update.Message == null) return;
				var chatId = update.Message.Chat.Id;
				await DeleteMessage(chatId, update.Message.MessageId, cancellationToken);

				if (update.Message.Text == null)
				{
					await _telegramBotClient.SendTextMessageAsync(chatId: chatId, text: "Бот принимает только команды из меню.", cancellationToken: cancellationToken);
					return;
				}

				var messageText = update.Message.Text;
				if (IsShowCommand(messageText))
				{
					await SendStartMessageAsync(chatId, cancellationToken);
				}

				if (IsStartCommand(messageText))
				{
					await ShowCurrncySelectionAsync(chatId, cancellationToken);
				}
			}

			

			private async Task SendStartMessageAsync(long? chatId, CancellationToken cancellationToken)
			{
				var inlineKeyboard = new InlineKeyboardMarkup(new[]
				{
					new[]
					{
						InlineKeyboardButton.WithCallbackData("Выбрать валюту.",CustomCallbackdata.SHOW_CURRENCIES_MENU), 
					}

				});

				await _telegramBotClient.SendTextMessageAsync(
					chatId, "Привет!\n" + "Данный бот показывает текущий курс выбранной валюты.\n",
					replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
			}

			private async Task SendCurrencyPriceAsync(long? chatId, string currencyCode, CancellationToken cancellationToken)
			{
				var price = await CoinMarket.GetPriceAsync(currencyCode);

				var inlineKeyboard = new InlineKeyboardMarkup(new[]
				{
					new []
					{
						InlineKeyboardButton.WithCallbackData("Выбрать другую валюту.",CustomCallbackdata.RETURN_TO_CURRENCIES_MENU)
					}
				});

				await _telegramBotClient.SendTextMessageAsync(chatId,
					text: $"Валюта: {currencyCode}, стоимость: {Math.Round(price, 3)}$", 
					replyMarkup: inlineKeyboard,
					cancellationToken: cancellationToken);
			}

			private async Task ShowCurrncySelectionAsync(long? chatId, CancellationToken cancellationToken)
			{
				var inlineKeyboard = new InlineKeyboardMarkup(new[]
				{
					new[]
					{
						InlineKeyboardButton.WithCallbackData("Bitcoin",CurrencyCode.BTC), 
						InlineKeyboardButton.WithCallbackData("Ethereum",CurrencyCode.ETH), 
					},
					new []
					{
						InlineKeyboardButton.WithCallbackData("BNB",CurrencyCode.BNB), 
						InlineKeyboardButton.WithCallbackData("Dogecoin",CurrencyCode.DOGE), 
					}

				});

				await _telegramBotClient.SendTextMessageAsync(chatId: chatId,
					text: "Выберите валюту:",
					replyMarkup: inlineKeyboard,
					cancellationToken: cancellationToken);
			}

			private async Task DeleteMessage(long chatId, int messageId, CancellationToken cancellationToken)
			{
				try
				{
					await _telegramBotClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
				}
				catch (ApiRequestException exception)
				{
					if (exception.ErrorCode == 400)
					{
						Console.WriteLine("User deleted message");
					}
				}
			}
			

			private bool IsStartCommand(string messageText)
			{
				return messageText.ToLower() == CustomBotCommand.START;
			}
			private bool IsShowCommand(string messageText)
			{
				return messageText.ToLower() == CustomBotCommand.SHOW_CURRENCIES;
			}
			
			
		}
		public class CurrencyCode
		{
			public const string BTC = "BTC";
			public const string ETH = "ETH";
			public const string BNB = "BNB";
			public const string DOGE = "DOGE";
		}
		
		public static class CustomCallbackdata
		{
			public const string SHOW_CURRENCIES_MENU = "ShowCurrenciesMenu";
			public const string RETURN_TO_CURRENCIES_MENU = "ReturnToCurrenciesMenu";
		}
		
		public static class ApiConstants
		{
			public const string BOT_API = "7951482368:AAFSZKw0splSyZIbVxvH5L_lNqQHEqnxCd0";
			public const string COIN_MARKET_API = "882aeadb-17aa-4247-8347-2bd49c835361";
		}

		public static class CustomBotCommand
		{
			public const string START = "/start";
			public const string SHOW_CURRENCIES = "/show";
		}
	}
}