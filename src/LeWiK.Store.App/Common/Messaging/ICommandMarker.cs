using LeWiK.Store.App.Common.Results;
using MediatR;

namespace LeWiK.Store.App.Common.Messaging;

public interface ICommandMarker;

public interface ICommand : ICommandMarker, IRequest<Result>;
public interface ICommand<TResponse> : ICommandMarker, IRequest<Result<TResponse>>;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;