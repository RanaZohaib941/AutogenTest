using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoGen;
using AutoGen.OpenAI;

namespace AutogenTestproject
{
    public class LlmConfig
    {


        public static class LLMConfiguration
        {

            public static AzureOpenAIConfig GetAzureOpenAIGPT3_5_Turbo(string? deployName = null)
            {
                var azureOpenAIKey = "";
                var endpoint = "https://bwa.openai.azure.com";
                deployName = deployName ?? "gpt-35-turbo";
                return new AzureOpenAIConfig(endpoint, deployName, azureOpenAIKey);
            }


        }


    }
}
 
