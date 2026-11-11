using Moq;
using Pagamento.Application.Gateways;
using Pagamento.Application.Queries.ListarPagamentosPorOrdemServico;
using PagamentoEntity = Pagamento.Domain.Pagamento;

namespace Pagamento.Application.Tests.Queries;

public class ListarPagamentosPorOrdemServicoHandlerTests
{
    private readonly Mock<IPagamentoGateway> _gatewayMock = new();
    private readonly ListarPagamentosPorOrdemServicoHandler _handler;

    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();

    public ListarPagamentosPorOrdemServicoHandlerTests()
    {
        _handler = new ListarPagamentosPorOrdemServicoHandler(_gatewayMock.Object);
    }

    private List<PagamentoEntity> PagamentosDaOrdem()
    {
        List<PagamentoEntity> pagamentos =
        [
            PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, 100m).Value,
            PagamentoEntity.Criar(_ordemServicoId, Guid.NewGuid(), _clienteId, 200m).Value
        ];
        _gatewayMock
            .Setup(g => g.ListarPorOrdemServico(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamentos);
        return pagamentos;
    }

    [Fact(DisplayName = "Oficina — lista todos os pagamentos da ordem de serviço")]
    public async Task Handle_Oficina_DeveListarTodos()
    {
        var pagamentos = PagamentosDaOrdem();

        var result = await _handler.Handle(
            new ListarPagamentosPorOrdemServicoQuery(_ordemServicoId, ClienteIdSolicitante: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(pagamentos, result.Value);
    }

    [Fact(DisplayName = "Cliente dono — lista os pagamentos da própria ordem de serviço")]
    public async Task Handle_ClienteDono_DeveListar()
    {
        var pagamentos = PagamentosDaOrdem();

        var result = await _handler.Handle(
            new ListarPagamentosPorOrdemServicoQuery(_ordemServicoId, _clienteId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(pagamentos, result.Value);
    }

    [Fact(DisplayName = "Cliente de outra ordem — acesso negado")]
    public async Task Handle_OutroCliente_DeveNegarAcesso()
    {
        PagamentosDaOrdem();

        var result = await _handler.Handle(
            new ListarPagamentosPorOrdemServicoQuery(_ordemServicoId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Pagamento.AcessoNegado", result.Error.Code);
    }

    [Fact(DisplayName = "Sem pagamentos — devolve lista vazia")]
    public async Task Handle_SemPagamentos_DeveDevolverListaVazia()
    {
        _gatewayMock
            .Setup(g => g.ListarPorOrdemServico(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _handler.Handle(
            new ListarPagamentosPorOrdemServicoQuery(_ordemServicoId, _clienteId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact(DisplayName = "Validator — ordem de serviço vazia é rejeitada")]
    public void Validator_OrdemServicoVazia_DeveRejeitar()
    {
        var validator = new ListarPagamentosPorOrdemServicoValidator();

        Assert.False(validator.Validate(new ListarPagamentosPorOrdemServicoQuery(Guid.Empty, null)).IsValid);
        Assert.True(validator.Validate(new ListarPagamentosPorOrdemServicoQuery(Guid.NewGuid(), null)).IsValid);
    }
}
