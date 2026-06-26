using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.Api.Common;

public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);
    
    public static IResult ToHttpResult(this Result result) => result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    private static IResult Problem(Error error) => error.Type switch
    {
        ErrorType.Validation => Results.Problem(statusCode: 400, title: error.Code, detail: error.Message),
        ErrorType.NotFound => Results.Problem(statusCode: 404, title: error.Code, detail: error.Message),
        ErrorType.Conflict => Results.Problem(statusCode: 409, title: error.Code, detail: error.Message),
        ErrorType.Unauthorized => Results.Problem(statusCode: 401, title: error.Code, detail: error.Message),
        ErrorType.Forbidden => Results.Problem(statusCode: 403, title: error.Code, detail: error.Message),
        _ => Results.Problem(statusCode: 500, title: error.Code, detail: error.Message),
    };
}