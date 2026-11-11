namespace Pagamento.Adapters.Models.ViewModels;

public sealed record PagamentoViewModel(
    Guid PagamentoId,
    Guid OrdemServicoId,
    Guid OrcamentoId,
    decimal Valor,
    string Status,
    string? MotivoRecusa,
    DateTime CriadoEm,
    DateTime AtualizadoEm);
