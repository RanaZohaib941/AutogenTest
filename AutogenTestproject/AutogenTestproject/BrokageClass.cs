using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AutogenTestproject.LlmConfig;
using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutogenTestproject
{
    public  class BrokageClass
    {
        private static readonly Dictionary<string, ProductInfo> AvailableItems = new Dictionary<string, ProductInfo>
    {
        { "laptop", new ProductInfo("High-performance laptop", 1200.00m) },
        { "smartphone", new ProductInfo("Latest model smartphone", 800.00m) },
        { "tablet", new ProductInfo("Large-screen tablet", 500.00m) },
        { "smartwatch", new ProductInfo("Fitness tracking smartwatch", 250.00m) },
        { "headphones", new ProductInfo("Noise-cancelling headphones", 150.00m) }
    };
        [Function]
        public async Task<string> DoesItemExistsInInventory(string ItemName)
        {
            return AvailableItems.ContainsKey(ItemName).ToString();
        }
        private class InventorySchema
        {
            [JsonPropertyName(@"ItemName")]
            public string ItemName { get; set; }
        }
        public Task<string> DoesItemExistsInInventoryWrapper(string arguments)
        {
            var schema = JsonSerializer.Deserialize<InventorySchema>(
                arguments,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });

            return DoesItemExistsInInventory(schema.ItemName);
        }

        public FunctionContract DoesItemExistsInInventoryFunctionContract
        {
            get => new FunctionContract
            {
                ClassName = @"BrokageClass",
                Name = @"DoesItemExistsInInventory",
                Description = @"It looks up the list of available items and match if the item with same name exists in the dictionary.",
                ReturnType = typeof(Task<string>),
                Parameters = new[]
                {
                    new FunctionParameterContract
                    {
                        Name = @"ItemName",
                        Description = @"name of the item type like headphones or laptops etc",
                        ParameterType = typeof(string),
                        IsRequired = true,
                    }
                },
            };
        }

        public static async Task RunAsync()
        {
            var function =new  BrokageClass();
            var gpt35 = LLMConfiguration.GetAzureOpenAIGPT3_5_Turbo();

            var broker = new AssistantAgent(
                name: "Broker",
                systemMessage: @"You are a friendly and professional broker named Alex. Your role is to facilitate transactions between buyers and sellers. Ask what type of item the buyer wants to buy.. Check if the item is available in your inventory
through function DoesItemExistsInInventory. no need to get in details with the buyer. The seller will discuss all the details. If available, try to arrange a meeting with the seller. If not, suggest alternatives. Always use a polite and helpful tone.",
                llmConfig: new ConversableAgentConfig
                {
                    Temperature = 0.7f,
                    ConfigList = [gpt35],
                    FunctionContracts= new List<FunctionContract>() { function.DoesItemExistsInInventoryFunctionContract}
                },
                functionMap: new Dictionary<string, Func<string, Task<string>>>
                {
                    {   function.DoesItemExistsInInventoryFunctionContract.Name, function.DoesItemExistsInInventoryWrapper }
                },
                humanInputMode: HumanInputMode.NEVER
            ).RegisterPrintMessage();

            var buyer = new AssistantAgent(
                name: "Buyer",
                systemMessage: @"You are a buyer named Sam looking to purchase an electronic device.You are looking for headphones, and nothing specific. you just want headphones. Use a casual and friendly tone.",
                llmConfig: new ConversableAgentConfig
                {
                    Temperature = 0.8f,
                    ConfigList = [gpt35],
                },
                humanInputMode: HumanInputMode.NEVER
            ).RegisterPrintMessage();

            var seller = new AssistantAgent(
                name: "Seller",
                systemMessage: @"You are a seller named Jamie with various electronic items. Interact with the broker and buyers to discuss product details and potentially make a sale. You are available to take a meeting with the buyer right now. Use a professional and enthusiastic tone.",
                llmConfig: new ConversableAgentConfig
                {
                    Temperature = 0.6f,
                    ConfigList = [gpt35],
                },
                humanInputMode: HumanInputMode.NEVER
            ).RegisterPrintMessage();

            try
            {
                await InitiateConversation(buyer, broker);
                var desiredItem = await ExtractDesiredItem(buyer, broker);

                if (AvailableItems.ContainsKey(desiredItem))
                {
                    var productInfo = AvailableItems[desiredItem];
                    await ProvideProductInfo(broker, buyer, desiredItem, productInfo);

                    bool sellerAvailable = await CheckSellerAvailability(broker, seller, desiredItem);

                    if (sellerAvailable)
                    {
                        Console.WriteLine("\n--- Starting group conversation ---\n");
                        await FacilitateGroupConversation(broker, buyer, seller, desiredItem);
                    }
                    else
                    {
                        await NotifySellerUnavailable(broker, buyer);
                    }
                }
                else
                {
                    await SuggestAlternatives(broker, buyer, desiredItem);
                }

                await EndConversation(broker, buyer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private static async Task InitiateConversation(MiddlewareAgent<AssistantAgent> buyer, MiddlewareAgent<AssistantAgent> broker)
        {
            await buyer.InitiateChatAsync(
                receiver: broker,
                message: "Hi there! I'm looking to buy a new electronic device. Can you help me out?",
                maxRound: 4);
        }

        private static async Task<string> ExtractDesiredItem(MiddlewareAgent<AssistantAgent> buyer, MiddlewareAgent<AssistantAgent> broker)
        {
            var conversation = await broker.InitiateChatAsync(
                receiver: buyer,
                message: "Of course! I'd be happy to help. What specific electronic device are you interested in?",
                maxRound: 5);

            return ExtractItemFromConversation(conversation.Last().GetContent());
        }

        private static async Task ProvideProductInfo(MiddlewareAgent<AssistantAgent> broker, MiddlewareAgent<AssistantAgent> buyer, string item, ProductInfo info)
        {
            await broker.InitiateChatAsync(
                receiver: buyer,
                message: $"Great choice! The {item} we have in stock is a {info.Description}. It's priced at ${info.Price}. Would you like more details about it?",
                maxRound: 5);
        }

        private static async Task<bool> CheckSellerAvailability(MiddlewareAgent<AssistantAgent> broker, MiddlewareAgent<AssistantAgent> seller, string item)
        {
            var conversation = await broker.InitiateChatAsync(
                receiver: seller,
                message: $"Hello Jamie, I have a potential buyer for the {item}. Are you available for a meeting to discuss the sale?",
                maxRound: 3);
            if (conversation.Last().GetContent().Contains("not available", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return conversation.Last().GetContent().Contains("available", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task NotifySellerUnavailable(MiddlewareAgent<AssistantAgent> broker, MiddlewareAgent<AssistantAgent> buyer)
        {
            await broker.InitiateChatAsync(
                receiver: buyer,
                message: "I apologize, but our seller is not available at the moment. Would you like me to notify you when they become available?",
                maxRound: 3);
        }

        private static async Task SuggestAlternatives(MiddlewareAgent<AssistantAgent> broker, MiddlewareAgent<AssistantAgent> buyer, string desiredItem)
        {
            var alternatives = AvailableItems.Keys.Where(item => item != desiredItem).Take(2);
            await broker.InitiateChatAsync(
                receiver: buyer,
                message: $"I'm sorry, but we don't have the {desiredItem} in stock. Would you be interested in a {alternatives.First()} or a {alternatives.Last()} instead?",
                maxRound: 5);
        }

        private static async Task FacilitateGroupConversation(MiddlewareAgent<AssistantAgent> broker, MiddlewareAgent<AssistantAgent> buyer, MiddlewareAgent<AssistantAgent> seller, string item)
        {
            await broker.InitiateChatAsync(
                receiver: buyer,
                message: $"Great news! Our seller Jamie is available to discuss the {item}. I'll introduce you two.",
                maxRound: 1);

            await broker.InitiateChatAsync(
                receiver: seller,
                message: $"Jamie, this is Sam, who's interested in the {item}. Sam, meet Jamie, our product expert. Please feel free to discuss the details.",
                maxRound: 1);

            await buyer.InitiateChatAsync(
                receiver: seller,
                message: $"Hi Jamie! I'm really interested in the {item}. Can you tell me more about its features?",
                maxRound: 10);
        }

        private static async Task EndConversation(MiddlewareAgent<AssistantAgent> broker, MiddlewareAgent<AssistantAgent> buyer)
        {
            await broker.InitiateChatAsync(
                receiver: buyer,
                message: "Thank you for your interest in our products. Is there anything else I can help you with today?",
                maxRound: 3);

            await buyer.InitiateChatAsync(
                receiver: broker,
                message: "No, that's all for now. Thank you for your help!",
                maxRound: 1);

            await broker.InitiateChatAsync(
                receiver: buyer,
                message: "You're welcome! Have a great day, and don't hesitate to reach out if you need any further assistance.",
                maxRound: 1);
        }

        private static string ExtractItemFromConversation(string content)
        {
            foreach (var item in AvailableItems.Keys)
            {
                if (content.Contains(item, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            return "unknown item";
        }

        private class ProductInfo
        {
            public string Description { get; }
            public decimal Price { get; }

            public ProductInfo(string description, decimal price)
            {
                Description = description;
                Price = price;
            }
        }

    }
}
