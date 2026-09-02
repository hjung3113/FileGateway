using Microsoft.OpenApi;

namespace FileGateway.Api.Endpoints;

/// <summary>
/// Logs/Configurations 엔드포인트는 HttpRequest에서 쿼리를 수동 파싱한다(attr.* 와일드카드 등 때문에 타입 바인딩 불가).
/// Minimal API의 리플렉션 기반 OpenAPI 생성은 메서드 파라미터만 보므로, 이 경로의 쿼리 파라미터는
/// 문서에 전혀 노출되지 않는다(Issue #19-4). 엔드포인트별로 파라미터를 명시적으로 선언해 보강한다.
/// </summary>
internal static class OpenApiQueryParameterExtensions
{
    public static RouteHandlerBuilder WithQueryParameters(
        this RouteHandlerBuilder builder, params ReadOnlySpan<(string Name, bool Required, string Description)> parameters)
    {
        var snapshot = parameters.ToArray();
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Parameters ??= [];
            foreach (var p in snapshot)
            {
                // 타입 바인딩 파라미터(예: FileEndpoints의 fileId)는 리플렉션이 이미 항목을 만들어뒀을 수 있다 —
                // 그 경우 설명/필수 여부만 보강하고 중복 추가하지 않는다.
                if (operation.Parameters.FirstOrDefault(x => x.Name == p.Name) is OpenApiParameter existing)
                {
                    existing.Description = p.Description;
                    existing.Required = p.Required;
                    continue;
                }
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = p.Name,
                    In = ParameterLocation.Query,
                    Required = p.Required,
                    Description = p.Description,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
            }
            return Task.CompletedTask;
        });
        return builder;
    }
}
