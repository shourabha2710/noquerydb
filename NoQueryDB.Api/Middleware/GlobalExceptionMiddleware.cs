using System.Data.SqlClient;
using System.Net;
using System.Text.Json;

namespace NoQueryDB.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred during the request.");
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            var statusCode = (int)HttpStatusCode.InternalServerError;
            var errorCode = "INTERNAL_SERVER_ERROR";
            var message = "An unexpected error occurred. Please try again later.";

            switch (exception)
            {
                case SqlException sqlEx:
                    statusCode = (int)HttpStatusCode.InternalServerError;
                    errorCode = "DATABASE_ERROR";
                    message = "A database error occurred while processing the request.";
                    break;
                case UnauthorizedAccessException:
                    statusCode = (int)HttpStatusCode.Unauthorized;
                    errorCode = "UNAUTHORIZED_ACCESS";
                    message = "You are not authorized to perform this action.";
                    break;
                case ArgumentException argEx:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    errorCode = "INVALID_ARGUMENT";
                    message = argEx.Message;
                    break;
                case InvalidOperationException invEx:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    errorCode = "INVALID_OPERATION";
                    message = invEx.Message;
                    break;
                default:
                    // Preserve generic 500 error code for arbitrary unexpected errors
                    break;
            }

            context.Response.StatusCode = statusCode;

            var response = new
            {
                success = false,
                message = message,
                errorCode = errorCode,
                traceId = context.TraceIdentifier
            };

            var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return context.Response.WriteAsync(jsonResponse);
        }
    }
}
