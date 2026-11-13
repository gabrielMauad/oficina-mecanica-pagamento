using Pagamento.Application.Gateways.Dtos;

namespace Pagamento.Application.Gateways;

/// <summary>
/// Port do processador de pagamento (Mercado Pago). A Application não conhece HTTP nem o
/// protocolo de autenticação do processador. Falhas técnicas (processador fora do ar, credenciais
/// recusadas) propagam como exceção; uma cobrança recusada é um resultado normal.
/// </summary>
public interface IProcessadorPagamentoGateway
{
    Task<ResultadoProcessamentoPagamento> Processar(SolicitacaoPagamento solicitacao, CancellationToken ct = default);
}
