using System.Diagnostics;

namespace CoPilotApp;
public class Utils
{
    public async static Task<string> QuickPromptAsync(string data, string prompt = "Summarize this: ")
    {
        try
        {
            prompt = $"{prompt} {data}";
            var summaryCompletion = new ChatCompletionsOptions()
            {
                DeploymentName = "gpt-35-turbo",
            };
            summaryCompletion.Messages.Add(new ChatRequestUserMessage(prompt));
            ChatCompletions response = await Kernel.Client.GetChatCompletionsAsync(summaryCompletion);

            return response.Choices.First().Message.Content?.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return "Sorry, I don't know how to summarize that.";
        }
    }

    public async static Task<string> GenerateImageAsync(string prompt = "")
    {
        string apiVersion = "2023-12-01-preview";

        var url = $"{Kernel.ENDPOINT}/openai/deployments/Dalle3/images/generations?api-version={apiVersion}";

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("api-key", Kernel.APIKEY);

            var body = new
            {
                prompt = prompt,
                size = "1024x1024",
                n = 1,
                quality = "hd",
                style = "vivid"
            };

            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.PostAsync(url, content);

            string imageUrl = "";

            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(result);
                imageUrl = data.data[0].url;
                OpenUrlInBrowser(imageUrl);
            }
            else
            {
                Console.WriteLine("Error: " + response.StatusCode);
            }
            return imageUrl;
        }
    }

    public static void OpenUrlInBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true // Use the operating system's shell to start the process  
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            // In case of an error, write the exception to the console  
            Console.WriteLine("An error occurred while trying to open the URL: " + ex.Message);
        }
    }


}


