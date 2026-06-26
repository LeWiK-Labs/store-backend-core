using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Results;
using MediatR;

namespace LeWiK.Store.App._Smoke;

public sealed record PingQuery(string Name) : IQuery<string>;

public sealed class PingValidator : AbstractValidator<PingQuery>
{
    public PingValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed class PingHandler : IRequestHandler<PingQuery, Result<string>>
{
    public Task<Result<string>> Handle(PingQuery request, CancellationToken ct) =>
        Task.FromResult(Result.Success($"pong: {request.Name}"));
}