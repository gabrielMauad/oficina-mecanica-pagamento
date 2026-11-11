using Pagamento.Domain.Events;
using SharedKernel.Domain;

namespace Pagamento.Domain;

/// <summary>
/// Pagamento do orçamento aprovado de uma ordem de serviço. Nasce <see cref="StatusPagamento.Pendente"/>
/// e só sai desse estado uma vez: aprovado ou recusado pelo processador de pagamento.
/// </summary>
public sealed class Pagamento : AggregateRoot<PagamentoId>
{
    public Guid OrdemServicoId { get; private set; }
    public Guid OrcamentoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public decimal Valor { get; private set; }
    public StatusPagamento Status { get; private set; }
    public string? MotivoRecusa { get; private set; }
    public long? MercadoPagoPaymentId { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    private Pagamento(
        PagamentoId id,
        Guid ordemServicoId,
        Guid orcamentoId,
        Guid clienteId,
        decimal valor,
        StatusPagamento status,
        string? motivoRecusa,
        long? mercadoPagoPaymentId,
        DateTime criadoEm,
        DateTime atualizadoEm)
        : base(id)
    {
        OrdemServicoId = ordemServicoId;
        OrcamentoId = orcamentoId;
        ClienteId = clienteId;
        Valor = valor;
        Status = status;
        MotivoRecusa = motivoRecusa;
        MercadoPagoPaymentId = mercadoPagoPaymentId;
        CriadoEm = criadoEm;
        AtualizadoEm = atualizadoEm;
    }

    public static Result<Pagamento> Criar(Guid ordemServicoId, Guid orcamentoId, Guid clienteId, decimal valor)
    {
        if (ordemServicoId == Guid.Empty)
            return Error.Validation("Pagamento.OrdemServicoInvalida", "Id da ordem de serviço é obrigatório.");
        if (orcamentoId == Guid.Empty)
            return Error.Validation("Pagamento.OrcamentoInvalido", "Id do orçamento é obrigatório.");
        if (clienteId == Guid.Empty)
            return Error.Validation("Pagamento.ClienteInvalido", "Id do cliente é obrigatório.");
        if (valor <= 0)
            return Error.Validation("Pagamento.ValorInvalido", "Valor do pagamento deve ser maior que zero.");

        var agora = DateTime.UtcNow;
        return new Pagamento(
            PagamentoId.Novo(), ordemServicoId, orcamentoId, clienteId, valor,
            StatusPagamento.Pendente, motivoRecusa: null, mercadoPagoPaymentId: null, agora, agora);
    }

    public static Pagamento Reconstituir(
        PagamentoId id,
        Guid ordemServicoId,
        Guid orcamentoId,
        Guid clienteId,
        decimal valor,
        StatusPagamento status,
        string? motivoRecusa,
        long? mercadoPagoPaymentId,
        DateTime criadoEm,
        DateTime atualizadoEm) =>
        new(id, ordemServicoId, orcamentoId, clienteId, valor, status, motivoRecusa, mercadoPagoPaymentId, criadoEm, atualizadoEm);

    public Result<Pagamento> Aprovar(long mercadoPagoPaymentId)
    {
        if (Status != StatusPagamento.Pendente)
            return StatusInvalido();

        Status = StatusPagamento.Aprovado;
        MercadoPagoPaymentId = mercadoPagoPaymentId;
        AtualizadoEm = DateTime.UtcNow;
        AddDomainEvent(new PagamentoAprovado(Id, OrdemServicoId, OrcamentoId, AtualizadoEm));
        return this;
    }

    public Result<Pagamento> Recusar(string motivo, long? mercadoPagoPaymentId)
    {
        if (Status != StatusPagamento.Pendente)
            return StatusInvalido();
        if (string.IsNullOrWhiteSpace(motivo))
            return Error.Validation("Pagamento.MotivoVazio", "Motivo da recusa é obrigatório.");

        Status = StatusPagamento.Recusado;
        MotivoRecusa = motivo;
        MercadoPagoPaymentId = mercadoPagoPaymentId;
        AtualizadoEm = DateTime.UtcNow;
        AddDomainEvent(new PagamentoRecusado(Id, OrdemServicoId, OrcamentoId, motivo, AtualizadoEm));
        return this;
    }

    private Error StatusInvalido() =>
        Error.Conflict(
            "Pagamento.StatusInvalido",
            $"O pagamento está {Status} e só pode ser aprovado ou recusado enquanto estiver {StatusPagamento.Pendente}.");
}
