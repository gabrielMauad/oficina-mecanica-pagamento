using Pagamento.Adapters.DataSources;
using Pagamento.Adapters.DataSources.Mappers;
using Pagamento.Application.Gateways;
using Pagamento.Domain;
using SharedKernel.Application;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Adapters.Gateways;

public sealed class PagamentoGateway : IPagamentoGateway
{
    private readonly IPagamentoRepository _repository;
    private readonly IDomainEventCollector _collector;

    public PagamentoGateway(IPagamentoRepository repository, IDomainEventCollector collector)
    {
        _repository = repository;
        _collector = collector;
    }

    public Task Adicionar(PagamentoEntity pagamento, CancellationToken ct = default)
    {
        _collector.Registrar(pagamento);
        return _repository.Adicionar(PagamentoMapper.ToRecord(pagamento), ct);
    }

    public Task Atualizar(PagamentoEntity pagamento, CancellationToken ct = default)
    {
        _collector.Registrar(pagamento);
        return _repository.Atualizar(PagamentoMapper.ToRecord(pagamento), ct);
    }

    public async Task<PagamentoEntity?> ObterPorOrdemServicoEOrcamento(
        Guid ordemServicoId, Guid orcamentoId, CancellationToken ct = default)
    {
        var record = await _repository.ObterPorOrdemServicoEOrcamento(ordemServicoId, orcamentoId, ct);
        return record is null ? null : PagamentoMapper.ToDomain(record);
    }

    public async Task<PagamentoEntity?> ObterPendentePorOrdemServico(Guid ordemServicoId, CancellationToken ct = default)
    {
        var pagamentos = await ListarPorOrdemServico(ordemServicoId, ct);
        return pagamentos
            .Where(p => p.Status == StatusPagamento.Pendente)
            .OrderByDescending(p => p.CriadoEm)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<PagamentoEntity>> ListarPorOrdemServico(Guid ordemServicoId, CancellationToken ct = default)
    {
        var records = await _repository.ListarPorOrdemServico(ordemServicoId, ct);
        return [.. records.Select(PagamentoMapper.ToDomain).OrderBy(p => p.CriadoEm)];
    }
}
