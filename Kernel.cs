
global using Azure;
global using Azure.AI.OpenAI;
global using System.Text.Json;
global using Microsoft.Extensions.Configuration;
global using Newtonsoft.Json;
global using System.Net.Http.Headers;
global using System.Text.RegularExpressions;
global using System.Text;
global using System.ComponentModel.DataAnnotations;
global using System.Reflection;

namespace CoPilotApp;

class Kernel
{
    public static readonly bool PluginsEnabled = true;
    public static ChatCompletionsOptions Session = new()
    {
        DeploymentName = MODEL,
        Temperature = 0.0f,
        MaxTokens = 200,
        NucleusSamplingFactor = 0.95f,
        FrequencyPenalty = 0.2f,
        PresencePenalty = 0.2f,
    };
    public static AzureKeyCredential Credentials => new(APIKEY);
    public static OpenAIClient Client = new(new Uri(ENDPOINT), Credentials);
    private static Dictionary<string, IBasePlugin> _plugins;

    public static string APIKEY => Load("OpenAI:ApiKey");
    public static string ENDPOINT => Load("OpenAI:Endpoint");
    public static string MODEL => Load("OpenAI:Model");
    public static string GHTOKEN => Load("GitHub:PersonalToken");
    public static string GHORG => Load("GitHub:Org");
    public static string BINGENDPOINT => Load("Bing:Endpoint");
    public static string BINGKEY => Load("Bing:Apikey");
    static string Load(string key)
          => new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).Build()[key];

    static async Task Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($" \n Model {MODEL}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(PluginsEnabled ? " Plugins enabled\n" : " Plugins disabled\n");
        Session.Messages.Add(new ChatRequestSystemMessage($"You are an AI assistant.\n\nCurrent date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));

        _plugins = LoadPluginsByReflection();

        if (PluginsEnabled)
            foreach (var plugin in _plugins.Values)
                Session.Functions.Add(plugin.FunctionDefinition);

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("> ");
            string input = Console.ReadLine().ToLower().Trim();

            if (input == "exit") break;
                Session.Messages.Add(new ChatRequestUserMessage(input));

            string kernelAnswer = await KernelThread();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"> {kernelAnswer}\n");
            Console.ResetColor();
            Session.Messages.Add(new ChatRequestAssistantMessage(kernelAnswer));
        }
    }


    static async Task<string> KernelThread()
    {
        Response<ChatCompletions> response = await Client.GetChatCompletionsAsync(Session);

        if (response.Value.Choices[0].Message?.FunctionCall?.Name is string name)
        {
            string pluginLookupName = name.Replace("Plugin", "");

            if (!_plugins.TryGetValue(pluginLookupName, out var plugin))
            {
                throw new Exception($"Unknown plugin name: {name}");
            }

            string argumentsJson = response.Value.Choices[0].Message?.FunctionCall?.Arguments;

            if (string.IsNullOrEmpty(argumentsJson))
            {
                throw new Exception("Function call arguments are missing or invalid.");
            }

            string result = await plugin.ProcessAsync(argumentsJson); 
            return result;
        }
        else
        {
            return response.Value.Choices[0].Message.Content.Trim();
        }
    }

    private static Dictionary<string, IBasePlugin> LoadPluginsByReflection()
    {
        var plugins = new Dictionary<string, IBasePlugin>();

        Assembly assembly = Assembly.GetExecutingAssembly();
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IBasePlugin).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);

        foreach (Type type in pluginTypes)
        {
            IBasePlugin pluginInstance = (IBasePlugin)Activator.CreateInstance(type);
            string pluginName = type.Name.Replace("Plugin", "");
            plugins.Add(pluginName, pluginInstance);
        }

        return plugins;
    }


}
