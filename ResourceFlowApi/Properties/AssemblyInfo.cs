using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ResourceFlowApi.Tests")]
// Lets the OpenAPI export tool (tools/OpenApiExport) boot the API in-process
// via WebApplicationFactory<Program>, the same technique ResourceFlowApi.Tests already uses — see
// tools/OpenApiExport/Program.cs for why this replaced build-time MSBuild document generation.
[assembly: InternalsVisibleTo("ResourceFlowApi.OpenApiExport")]
