using Pagamento.Adapters.DataSources.Records;
using Pagamento.Domain;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Adapters.DataSources.Mappers;

public static class PagamentoMapper
{
    public static PagamentoRecord ToRecord(PagamentoEntity pagamento) =>
        new(
            pagamento.Id.Value,
            pagamento.OrdemServicoId,
            pagamento.OrcamentoId,
            pagamento.ClienteId,
            pagamento.Valor,
            pagamento.Status.ToString(),
            pagamento.MotivoRecusa,
            pagamento.MercadoPagoPaymentId,
            pagamento.CriadoEm,
            pagamento.AtualizadoEm);

    public static PagamentoEntity ToDomain(PagamentoRecord record) =>
        PagamentoEntity.Reconstituir(
            new PagamentoId(record.PagamentoId),
            record.OrdemServicoId,
            record.OrcamentoId,
            record.ClienteId,
            record.Valor,
            Enum.Parse<StatusPagamento>(record.Status),
            record.MotivoRecusa,
            record.MercadoPagoPaymentId,
            record.CriadoEm,
            record.AtualizadoEm);
}
