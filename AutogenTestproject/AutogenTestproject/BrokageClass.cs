using static AutogenTestproject.LlmConfig;
using AutoGen;
using AutoGen.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace AutogenTestproject
{
    public class BrokageClass
    {
        public static string CallScheduleTime = "9pm";
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
            var function = new BrokageClass();
            var gpt35 = LLMConfiguration.GetAzureOpenAIGPT3_5_Turbo();
            var brokerForBuyer = new AssistantAgent(
                name: "brokerForBuyer",
                systemMessage: @"you are a broker. please dont say i am AI model.please dont say 'As an AI language model'. please dont mention AI.
                    you only talk with the buyer and discuss his needs. 
                    If the buyer wants to know any details of a product
                    you tell them that the seller will provide all the information. 
                    your purpose is to just schedule a meeting with the buyer and seller. and besides that you don't know anything.Once the buyer tells you
                    what they want to buy you ask them if they are available to have a meeting with the seller and ask what time they want to have the meeting?. 
                    you already have contact information of the buyer
                    so no need to ask that. once the buyer tells you their availability time slot. 
                    tell the buyer that we will contact them on their designated time. after that you terminate the conversation",
                llmConfig: new ConversableAgentConfig
                {
                    Temperature = 0.7f,
                    ConfigList = [gpt35]
                },
                humanInputMode: HumanInputMode.NEVER
            )
                .RegisterPrintMessage();
            var brokerForSeller = new AssistantAgent(
              name: "brokerForSeller",
              systemMessage: @"you are a broker. please dont say i am AI model. please dont say 'As an AI language model'. please dont mention AI.
                you discuss and schedule meeting with a seller. you only talk with the seller to schedule a meeting with them with a prospect/lead. 
                once the seller tells you they are available for the meeting you say them goodbye and terminate the conversation. ",
              llmConfig: new ConversableAgentConfig
              {
                  Temperature = 0.7f,
                  ConfigList = [gpt35]
              },
              humanInputMode: HumanInputMode.NEVER
            )
              .RegisterPrintMessage();
            var buyer = new AssistantAgent(
                name: "Buyer",
                systemMessage: @"you want to buy some headphones. please dont say i am AI model. please dont say 'As an AI language model'. please dont mention AI,
                    you are an acting buyer. Please if a broker asks you to schedule a meeting. please plase tell them that you are
                    available at 9pm for the meeting. dont ask any other questions. 
                    just ask that you want to buy headphones only. do not provide any details on the headphones. Don't talk anymore just please
                    please say you want headphoines and then if asked to schedule a meeting with a seller. tell them you are available at 9 pm.",
                llmConfig: new ConversableAgentConfig
                {
                    Temperature = 0.8f,
                    ConfigList = [gpt35],
                },
                humanInputMode: HumanInputMode.NEVER
            ).RegisterPrintMessage();
            var seller = new AssistantAgent(
                name: "jamie",
                systemMessage: @"You are a seller named Jamie with various electronic items. please dont say i am AI model. please dont say 'As an AI language model'. please dont mention AI.
                    you are available for meetings all the tim. you are desperate to sell your items.
                    and broker will provide you details on the meeting. No need to ask any other information just say yes.",
                llmConfig: new ConversableAgentConfig
                {
                    Temperature = 0.6f,
                    ConfigList = [gpt35],
                },
                humanInputMode: HumanInputMode.NEVER
            ).RegisterPrintMessage();
            try
            {
                await InitiateConversation(buyer, brokerForBuyer);
                await CheckSellerAvailability(brokerForSeller, seller, "headphones");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
        private static async Task InitiateConversation(MiddlewareAgent<AssistantAgent> buyer, MiddlewareAgent<AssistantAgent> broker)
        {
            var response = await broker.InitiateChatAsync(
                  receiver: buyer,
                  message: "Hello! what would you like to buy?",
                  maxRound: 15
                 );
            if (response != null)
            {
                var list = response.ToList();
                foreach (var item in list)
                {
                    if (item.GetContent().Contains("pm"))
                    {
                        string content = item.GetContent();
                        string[] parts = content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        int pmIndex = Array.FindIndex(parts, part => part.Contains("pm"));
                        if (pmIndex >= 0)
                        {
                            string numberBeforePm = parts[pmIndex].Replace("pm", "");
                            if (string.IsNullOrEmpty(numberBeforePm) && pmIndex > 0)
                            {
                                numberBeforePm = parts[pmIndex - 1];
                            }
                            CallScheduleTime = numberBeforePm;
                        }
                    }
                }
            }
        }
        private static async Task<bool> CheckSellerAvailability(MiddlewareAgent<AssistantAgent> broker, MiddlewareAgent<AssistantAgent> seller, string item)
        {
            var conversation = await broker.InitiateChatAsync(
                receiver: seller,
                message: $"Hello Jamie, I have a potential buyer for the {item}. Are you available for a meeting at time {CallScheduleTime} to discuss the sale?",
                maxRound: 1);
            if (conversation.Last().GetContent().Contains("not available", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return conversation.Last().GetContent().Contains("available", StringComparison.OrdinalIgnoreCase);
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
