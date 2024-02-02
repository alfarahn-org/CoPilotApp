
namespace CoPilotApp;

public interface IBasePlugin
{
    FunctionDefinition FunctionDefinition { get; }
    Task<string> ProcessAsync(string requestJson);
}

public abstract class BasePlugin<TRequest> : IBasePlugin
{
    public abstract FunctionDefinition FunctionDefinition { get; }

    public async Task<string> ProcessAsync(string requestJson)
    {
        try
        {
            TRequest typedRequest = JsonConvert.DeserializeObject<TRequest>(requestJson);

            if (typedRequest == null)
            {
                throw new JsonSerializationException($"Deserialization of JSON to {typeof(TRequest).Name} resulted in null.");
            }

            ValidateRequiredProperties(typedRequest);

            return await ProcessAsync(typedRequest);
        }
        catch (JsonSerializationException jsonEx)
        {   
            return $"Error deserializing request to type {typeof(TRequest).Name}: {jsonEx.Message}";
        }
        catch (ArgumentNullException argNullEx)
        {  
            return $"Error: Required property '{argNullEx.ParamName}' is null.";
        }
        catch (Exception ex)
        {    
            return $"Error: {ex.Message}";
        }
    }

    protected abstract Task<string> ProcessAsync(TRequest request);

    private void ValidateRequiredProperties(TRequest request)
    {
        // Use reflection to check for properties with the 'required' attribute that are null    
        var properties = typeof(TRequest).GetProperties();
        foreach (var property in properties)
        {
            // Check if the property is marked as 'required' and if it is null    
            bool isRequired = property.CustomAttributes.Any(attr => attr.AttributeType == typeof(RequiredAttribute));
            if (isRequired && property.GetValue(request) == null)
            {
                // If a required property is null, throw an exception    
                throw new ArgumentNullException(property.Name, "Required property cannot be null.");
            }
        }
    }
}

public class ImagePlugin : BasePlugin<ImageRequest>
{
    public override FunctionDefinition FunctionDefinition => new ()
    {
        Name = nameof(ImagePlugin),
        Description = "This function generates an image based on the prompt",
        Parameters = BinaryData.FromObjectAsJson(
                   new
                   {
            Type = "object",
            Properties = new
            {
                ImagePrompt = new
                {
                    Type = "string",
                    Description = "Prompt describing the image the user wants. The user should give a detailed description of the image they want.",
                }
            },
            Required = new[] { "ImagePrompt" },
        }, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    protected override async Task<string> ProcessAsync(ImageRequest request)
    {
        string imageUrl = await Utils.GenerateImageAsync(request.ImagePrompt);
        return imageUrl;
    }
}

public class WorkflowStatusPlugin : BasePlugin<WorkflowStatusRequest>
{
    public override FunctionDefinition FunctionDefinition => new ()
    {
        Name = nameof(WorkflowStatusPlugin),
        Description = "Get the status of a GitHub action workflows by the name of the repository",
        Parameters = BinaryData.FromObjectAsJson(
        new
        {
            Type = "object",
            Properties = new
            {
                Repository = new
                {
                    Type = "string",
                    Description = "Repository in the GitHub organization",
                }
            },
            Required = new[] { "repository" },
        },
        new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    protected override async Task<string> ProcessAsync(WorkflowStatusRequest request)
    {
            string apiUrl = $"https://api.github.com/repos/{Kernel.GHORG}/{request.Repository}/actions/runs?status=completed&per_page=3";
            Utils.OpenUrlInBrowser($"https://github.com/{Kernel.GHORG}/{request.Repository}/actions");
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Kernel.GHTOKEN);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyAppName/1.0)");

            HttpResponseMessage r = await httpClient.GetAsync(apiUrl);
            r.EnsureSuccessStatusCode();

            if(!r.IsSuccessStatusCode)
            {
                return $"Error: {r.StatusCode}";
            }

            string responseBody = await r.Content.ReadAsStringAsync();
            responseBody = await Utils.QuickPromptAsync(responseBody, "Summarize the build status");
            return responseBody;
    }
}

public class WorkflowAutomationPlugin : BasePlugin<WorkflowAutomationRequest>
{
    public override FunctionDefinition FunctionDefinition => new ()
    {
        Name = nameof(WorkflowAutomationPlugin),
        Description = "Runs a workflow by the name, first user always says project name and then workflow name",
        Parameters = BinaryData.FromObjectAsJson(
        new
        {
            Type = "object",
            Properties = new
            {
                Project = new
                {
                    Type = "string",
                    Description = "Name of the project",
                },
                Workflow = new
                {
                    Type = "string",
                    Description = "Name of the workflow",
                }
            },
            Required = new[] { "project", "workflow" },
        },
        new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    protected override async Task<string> ProcessAsync(WorkflowAutomationRequest request)
    {
            string getWorkflowsUrl = $"https://api.github.com/repos/{Kernel.GHORG}/{request.Project}/actions/workflows";
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Kernel.GHTOKEN);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyAppName/1.0)");

            HttpResponseMessage response = await httpClient.GetAsync(getWorkflowsUrl);
            response.EnsureSuccessStatusCode();
            string workflowsResponse = await response.Content.ReadAsStringAsync();

            // Parse the response to get the workflow ID    
            dynamic workflows = JsonConvert.DeserializeObject(workflowsResponse);
            int workflowId = -1;


            foreach (var workflow in workflows.workflows)
            {
                if (workflow.name.ToString().ToLower() == request.Workflow.ToLower())
                {
                    workflowId = workflow.id;
                    break;
                }
            }

            if (workflowId != -1)
            {
                string dispatchWorkflowUrl = $"https://api.github.com/repos/{Kernel.GHORG}/{request.Project}/actions/workflows/{workflowId}/dispatches";
                Utils.OpenUrlInBrowser($"https://github.com/{Kernel.GHORG}/{request.Project}/actions");
                var content = new StringContent(JsonConvert.SerializeObject(new { @ref = "main" }), Encoding.UTF8, "application/json");
                HttpResponseMessage dispatchResponse = await httpClient.PostAsync(dispatchWorkflowUrl, content);
                dispatchResponse.EnsureSuccessStatusCode();

                return $"Workflow '{request.Workflow}' dispatched in repository '{request.Project}' Status code: {dispatchResponse.StatusCode}";
            }
            else
            {
                return $"Workflow '{request.Workflow}' not found in repository '{request.Project}'";
            }
    }
}

public class ParkingRegistrationPlugin : BasePlugin<ParkingRegistrationRequest>
{
    public override FunctionDefinition FunctionDefinition => new ()
    {
        Name = nameof(ParkingRegistrationPlugin),
        Description = "You register attendees of the conference with mandatory fields: Name, Company Name, Role, License Plate.",
        Parameters = BinaryData.FromObjectAsJson(
        new
        {
            Type = "object",
            Properties = new
            {
                Name = new
                {
                    Type = "string",
                    Description = "Name of the attendee, e.g. John Doe",
                },
                CompanyName = new
                {
                    Type = "string",
                    Description = "Name of the company, e.g. Contoso",
                },
                Role = new
                {
                    Type = "string",
                    Description = "Role of the attendee, e.g. Developer",
                },
                LicensePlate = new
                {
                    Type = "string",
                    Description = "License plate of the car, e.g. 123ABC",
                }
            },
            Required = new[] { "name", "companyname", "role", "licenseplate" },
        },
        new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    protected override async Task<string> ProcessAsync(ParkingRegistrationRequest request)
    {
        return $"Car registered Name: {request.Name} CompanyName: {request.CompanyName} Role: {request.Role} LicensePlate: {request.LicensePlate}";
    }
}

public class NewsPlugin : BasePlugin<NewsRequest>
{
    public override FunctionDefinition FunctionDefinition => new ()
    {
        Name = nameof(NewsPlugin),
        Description = "Get the news related to a specific topic. The topic is a mandatory field so require this from the user.",
        Parameters = BinaryData.FromObjectAsJson(
        new
        {
            Type = "object",
            Properties = new
            {
                Topic = new
                {
                    Type = "string",
                    Description = "Topic or category of the news",
                }
            },
            Required = new[] { "topic" },
        },
        new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    protected override async Task<string> ProcessAsync(NewsRequest request)
    {
        // Set the desired number of search results and text formatting options
        int resultCount = 30;
        string textDecorations = "false";
        string textFormat = "Raw"; // Options are "Raw" or "HTML"
        string safeSearch = "Strict"; // Options are "Off", "Moderate", or "Strict"

        var queryString = $"?q=Give me the latest News about: {request.Topic}&mkt=en-us&count={resultCount}" +
                          $"&textDecorations={textDecorations}&textFormat={textFormat}&safeSearch={safeSearch}";

        var client = new HttpClient();

        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Kernel.BINGKEY);

        HttpResponseMessage response = await client.GetAsync(Kernel.BINGENDPOINT + queryString);

        if (response.IsSuccessStatusCode)
        {
            var contentString = await response.Content.ReadAsStringAsync();
            Dictionary<string, object> searchResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(contentString);

            // If you need to further remove any image URLs from the searchResponse, you can process it here before returning

            return JsonConvert.SerializeObject(searchResponse, Formatting.Indented);
        }
        else
        {
            return $"Error: {response.StatusCode}";
        }
    }

}

public class WeatherPlugin : BasePlugin<WeatherRequest>
{
    public override FunctionDefinition FunctionDefinition => new ()
    {
        Name = nameof(WeatherPlugin),
        Description = "Get the weather forecast of a given location.\n\n" +
        "Location is mandatory so dont guess this. If users express its cold you can ask for location or try to guess if user might be interested in weather.",
        Parameters = BinaryData.FromObjectAsJson(
        new
        {
            Type = "object",
            Properties = new
            {
                Location = new
                {
                    Type = "string",
                    Description = "The city and state, e.g. San Francisco, CA",
                }
            },
            Required = new[] { "location" },
        },
        new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    protected async override Task<string> ProcessAsync(WeatherRequest request)
    {
        string prompt = $"What is longitude and latitude for:";


        var response = await Utils.QuickPromptAsync((request.Location), prompt);

        double latitude = 0;
        double longitude = 0;

        Regex regex = new(@"(-?\d+\.\d+)");
        MatchCollection matches = regex.Matches(response);

        if (matches.Count >= 2)
        {
            double firstValue = double.Parse(matches[0].Value);
            double secondValue = double.Parse(matches[1].Value);

            latitude = firstValue;
            longitude = secondValue;
        }
        var weather = await new HttpClient().GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current_weather=true");
        string weatherSummary = await Utils.QuickPromptAsync($"Location: {request.Location} , Weather: {weather}", "summarize the weather");

        return weatherSummary;
    }
}

public class DoctorPlugin : BasePlugin<DoctorRequest>
{
    public override FunctionDefinition FunctionDefinition => new ()
    {
        Name = nameof(DoctorPlugin),
        Description = "You consult patient if they are sick. Gather patient information for a medical record so a doctor can help by the automated medical record platform.",
        Parameters = BinaryData.FromObjectAsJson(
        new
        {
            Type = "object",
            Properties = new
            {
                PatientName = new
                {
                    Type = "string",
                    Description = "Full name of the patient",
                },
                DateOfBirth = new
                {
                    Type = "string",
                    Description = "Date of birth of the patient (YYYY-MM-DD)",
                },
                CurrentMedications = new
                {
                    Type = "string",
                    Description = "List of current medications",
                },
                CurrentSickness = new
                {
                    Type = "string",
                    Description = "Description of the current sickness or symptoms",
                }
            },
            Required = new[] { "patientName", "dateOfBirth", "currentMedications", "currentSickness"},
        },
        new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    protected override async Task<string> ProcessAsync(DoctorRequest request)
    {

        string patientInfo = $"\nPatient Name: {request.PatientName}\n" +
                                $"Date of Birth: {request.DateOfBirth}\n" +
                                $"Current Medications: {request.CurrentMedications}\n" +
                                $"Current Sickness: {request.CurrentSickness}\n";

        // Replace the following line with actual data storage logic if needed
        await Task.CompletedTask;

        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine(patientInfo);

        var doctorRecommendation = await Utils.QuickPromptAsync(patientInfo, "Give me a potential cure as a doctor and recommendation");

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(doctorRecommendation + "\n");    
        Console.ResetColor();
        Console.WriteLine("Thank you for using the automated medical record platform. Have a nice day!");

        return patientInfo;
    }
}

public class WorkflowStatusRequest
{
    public required string Repository { get; set; }
}

public class WorkflowAutomationRequest
{
    public required string Project { get; set; }
    public required string Workflow { get; set; }
}

public class ParkingRegistrationRequest
{
    public required string Name { get; set; }
    public required string CompanyName { get; set; }
    public required string Role { get; set; }
    public required string LicensePlate { get; set; }
}

public class NewsRequest
{
    public required string Topic { get; set; }
}

public class WeatherRequest
{
    public required string Location { get; set; }
}

public class DoctorRequest
{
    public required string PatientName { get; set; }
    public required string DateOfBirth { get; set; }
    public required string CurrentMedications { get; set; }
    public required string CurrentSickness { get; set; }
}

public class ImageRequest
{
    public required string ImagePrompt { get; set; }
}
