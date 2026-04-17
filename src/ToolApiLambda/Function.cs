using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ToolApiLambda;

public class Function
{
    private static readonly HttpClient HttpClient = new();

    public async Task<object> FunctionHandler(JsonElement input, ILambdaContext context)
    {
        if (IsApiGatewayEvent(input))
        {
            return HandleApiGatewayRequest(input);
        }

        if (IsBedrockActionGroupEvent(input))
        {
            return await HandleBedrockActionRequestAsync(input, context);
        }

        context.Logger.LogInformation($"Received unsupported event payload: {input}");
        throw new InvalidOperationException("Unsupported event payload.");
    }

    private static bool IsApiGatewayEvent(JsonElement input)
    {
        return input.TryGetProperty("version", out _) && input.TryGetProperty("requestContext", out _);
    }

    private static bool IsBedrockActionGroupEvent(JsonElement input)
    {
        return input.TryGetProperty("actionGroup", out _) && input.TryGetProperty("apiPath", out _);
    }

    private static APIGatewayHttpApiV2ProxyResponse HandleApiGatewayRequest(JsonElement input)
    {
        var topic = "bedrock";

        if (input.TryGetProperty("queryStringParameters", out var queryParameters)
            && queryParameters.ValueKind == JsonValueKind.Object
            && queryParameters.TryGetProperty("topic", out var topicValue)
            && topicValue.ValueKind == JsonValueKind.String)
        {
            topic = topicValue.GetString() ?? topic;
        }

        var payload = JsonSerializer.Serialize(new
        {
            topic,
            insight = $"Starter insight for {topic}: you can use Bedrock action groups with API Gateway backed tools.",
            runtime = "dotnet10"
        });

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            },
            Body = payload
        };
    }

    private static async Task<object> HandleBedrockActionRequestAsync(JsonElement input, ILambdaContext context)
    {
        var actionGroup = input.GetProperty("actionGroup").GetString() ?? "ToolApiActionGroup";
        var apiPath = input.GetProperty("apiPath").GetString() ?? "/tool/insight";
        var httpMethod = input.TryGetProperty("httpMethod", out var methodProperty) && methodProperty.ValueKind == JsonValueKind.String
            ? methodProperty.GetString() ?? "GET"
            : "GET";

        var parameters = ExtractParameters(input);
        using var response = await CallToolApiAsync(apiPath, httpMethod, parameters, context);
        var responseBody = await response.Content.ReadAsStringAsync();

        return new
        {
            messageVersion = "1.0",
            response = new
            {
                actionGroup,
                apiPath,
                httpMethod,
                httpStatusCode = (int)response.StatusCode,
                responseBody = new Dictionary<string, object>
                {
                    ["application/json"] = new
                    {
                        body = responseBody
                    }
                }
            }
        };
    }

    private static Dictionary<string, string> ExtractParameters(JsonElement input)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!input.TryGetProperty("parameters", out var parameterArray) || parameterArray.ValueKind != JsonValueKind.Array)
        {
            return parameters;
        }

        foreach (var parameter in parameterArray.EnumerateArray())
        {
            if (!parameter.TryGetProperty("name", out var nameProperty)
                || nameProperty.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(nameProperty.GetString()))
            {
                continue;
            }

            var value = parameter.TryGetProperty("value", out var valueProperty)
                ? valueProperty.GetString() ?? string.Empty
                : string.Empty;

            parameters[nameProperty.GetString()!] = value;
        }

        return parameters;
    }

    private static async Task<HttpResponseMessage> CallToolApiAsync(
        string apiPath,
        string httpMethod,
        IReadOnlyDictionary<string, string> parameters,
        ILambdaContext context)
    {
        var baseUrl = Environment.GetEnvironmentVariable("TOOL_API_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("TOOL_API_BASE_URL environment variable is required.");
        }

        var requestUri = BuildRequestUri(baseUrl, apiPath, parameters);
        context.Logger.LogInformation($"Calling API Gateway tool endpoint: {requestUri}");

        var request = new HttpRequestMessage(new HttpMethod(httpMethod), requestUri);
        return await HttpClient.SendAsync(request);
    }

    private static Uri BuildRequestUri(string baseUrl, string apiPath, IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new UriBuilder(baseUrl)
        {
            Path = apiPath.TrimStart('/')
        };

        if (parameters.Count > 0)
        {
            builder.Query = string.Join("&", parameters.Select(p => $"{UrlEncoder.Default.Encode(p.Key)}={UrlEncoder.Default.Encode(p.Value)}"));
        }

        return builder.Uri;
    }
}
