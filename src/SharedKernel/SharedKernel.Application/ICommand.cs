using MediatR;
using SharedKernel.Domain;

namespace SharedKernel.Application;

public interface ICommand;

public interface ICommand<TResponse> : ICommand, IRequest<Result<TResponse>>;
