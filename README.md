# bedrockagents_starter

Starter boilerplate showing how to provision an Amazon Bedrock Agent with CloudFormation, and connect it to an API Gateway tool backed by a .NET 10 Lambda function.

## What is included

- **CloudFormation template**: `/infra/template.yaml`
  - Creates an Amazon Bedrock Agent with prompt overrides and session memory settings
  - Creates an API Gateway HTTP API (`GET /tool/insight`)
  - Creates a .NET 10 Lambda function behind API Gateway
  - Configures a Bedrock Agent action group with an OpenAPI schema so the agent can use the API Gateway endpoint as a tool
- **.NET solution**: `/BedrockAgentsStarter.sln`
  - Lambda project: `/src/ToolApiLambda`
  - Lambda handler supports both:
    - API Gateway events (returns tool data)
    - Bedrock action-group events (calls API Gateway and returns Bedrock-compatible response payload)

## Build the .NET 10 Lambda artifact

### PowerShell (Windows)

```powershell
cd C:\path\to\bedrockagents_starter

dotnet restore .\BedrockAgentsStarter.sln

dotnet publish .\src\ToolApiLambda\ToolApiLambda.csproj -c Release -o .\artifacts\ToolApiLambda

Compress-Archive -Path .\artifacts\ToolApiLambda\* -DestinationPath .\artifacts\ToolApiLambda.zip -Force
```

### Bash

```bash
cd /path/to/bedrockagents_starter

dotnet restore ./BedrockAgentsStarter.sln

dotnet publish ./src/ToolApiLambda/ToolApiLambda.csproj -c Release -o ./artifacts/ToolApiLambda

cd ./artifacts/ToolApiLambda
zip -r ../ToolApiLambda.zip .
```

## Deploy with CloudFormation

1. Upload `artifacts/ToolApiLambda.zip` to S3.
2. Deploy the stack:

```bash
aws cloudformation deploy \
  --stack-name bedrock-agent-starter \
  --template-file infra/template.yaml \
  --capabilities CAPABILITY_NAMED_IAM \
  --parameter-overrides \
    ProjectName=bedrock-agent-starter \
    FoundationModelId=anthropic.claude-3-haiku-20240307-v1:0 \
    ToolApiLambdaS3Bucket=<your-artifact-bucket> \
    ToolApiLambdaS3Key=<path/to/ToolApiLambda.zip>
```

## Runtime flow

1. User asks the Bedrock agent for an insight.
2. Agent orchestration calls the action group (`tool-api-action-group`).
3. Action-group Lambda logic calls the API Gateway endpoint.
4. API Gateway invokes the .NET 10 Lambda tool route (`GET /tool/insight`).
5. Tool output is returned to the agent and used in the final response.
