using LeWiK.Store.App.Common.Results;
using MediatR;

namespace LeWiK.Store.App.Common.Messaging;

public interface ICommandMarker;

// Opts a command out of the tenant guard — and ONLY that guard; validation, retry and the
// unit of work still apply. Two shapes need it: commands that run before any store exists
// (creating one) and commands that deliberately act across stores (suspending one). Marking
// has to be explicit so "no tenant" is a decision on the command, never an accident of a
// missing header.
public interface ITenantAgnostic;

public interface ICommand : ICommandMarker, IRequest<Result>;
public interface ICommand<TResponse> : ICommandMarker, IRequest<Result<TResponse>>;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;