# Azure Voting App (.NET)

This sample is the .NET implementation of the [official Azure Voting App](https://github.com/Azure-Samples/azure-voting-app-redis). This sample creates a multi-container application in a Kubernetes cluster like the original one.

The application interface is built using ASP.NET Core Blazor Server. The data component uses Redis.

This sample also includes the following features:

1. Instrumented to produce OpenTelemetry traces and export the traces to the console.
2. Request to GET `/traced-exception` endpoint will throw an exception. It shows how unhandled exceptions are logged in the active OpenTelemtry span.
3. Instrumented to emit built-in .NET metrics and custom metrics with [prometheus-net](https://github.com/prometheus-net/prometheus-net). The metrics are available on the `/metrics` endpoint in the OpenMetrics format.
4. Deployment with Helm, Kubernetes manifest, or Docker compose.

## Demo

![Demo](demo.gif)
