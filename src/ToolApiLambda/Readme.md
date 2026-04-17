# ToolApiLambda

.NET 10 Lambda function used by this starter:

- Handles API Gateway requests for `GET /tool/insight`
- Handles Bedrock Agent action-group requests and forwards tool calls to API Gateway

Main handler: `ToolApiLambda::ToolApiLambda.Function::FunctionHandler`
