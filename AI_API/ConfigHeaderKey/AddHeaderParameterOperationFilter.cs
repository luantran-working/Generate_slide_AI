using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AI_API.ConfigHeaderKey
{
    public class AddHeaderParameterOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation.Parameters == null)
                operation.Parameters = new List<OpenApiParameter>();

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "headerKey",
                In = ParameterLocation.Header,
                Required = true,
                Description = "API Key bảo mật để gọi Generate Slide",
                Schema = new OpenApiSchema
                {
                    Type = "string"
                }
            });
        }
    }
}
